// ============================================================
//  FlowEngine.cs — 状态机引擎（后台线程运行）
//
//  核心思路：
//    · 每个 State 有 waitFor 前置条件 → 等到满足才执行动作
//    · 条件由 Agent 实时查询，500ms 轮询，无固定延时
//    · 弹窗通过 snapshot diff 自动感知，读取正文内容匹配
//    · ERROR 状态触发暂停，等待人工干预后恢复
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GnwayController.Models;

namespace GnwayController.Engine
{
    public class FlowEngine
    {
        private readonly AgentClient        _client;
        private readonly FlowDefinition     _flow;
        private readonly Action<EngineEvent> _emit;

        private volatile bool _running;
        private volatile bool _paused;
        private volatile bool _skipStep;
        private Thread?       _thread;

        // ── 弹窗追踪 ─────────────────────────────────────────
        private HashSet<string> _baselineWindows  = new HashSet<string>();
        private string          _popupTitle        = "";
        private string          _popupBody         = "";
        private string          _popupButtons      = "";

        // ── 公开属性（供 UI 只读）──────────────────────────
        public string  CurrentStateId  { get; private set; } = "";
        public int     CurrentRound    { get; private set; }
        public bool    IsRunning       => _running;
        public bool    IsPaused        => _paused;

        public FlowEngine(AgentClient client, FlowDefinition flow, Action<EngineEvent> emit)
        {
            _client = client;
            _flow   = flow;
            _emit   = emit;
        }

        // ── 控制接口 ─────────────────────────────────────────
        public void Start()
        {
            _running   = true;
            _paused    = false;
            _skipStep  = false;
            _thread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name         = "FlowEngine"
            };
            _thread.Start();
        }

        public void Stop()     => _running   = false;
        public void Pause()    => _paused    = true;
        public void Resume()   => _paused    = false;
        public void SkipStep() => _skipStep  = true;

        // =====================================================
        //  主循环
        // =====================================================
        private void RunLoop()
        {
            try
            {
                var startState = _flow.States.FirstOrDefault(s => s.StartState)
                                 ?? _flow.States[0];

                CurrentRound   = 0;
                CurrentStateId = startState.Id;

                while (_running)
                {
                    WaitWhilePaused();
                    if (!_running) break;

                    // ── 轮次计数（每次回到起始状态）────────
                    if (CurrentStateId == startState.Id)
                    {
                        CurrentRound++;
                        if (CurrentRound > _flow.MaxRounds)
                        {
                            Log($"⚠ 已达最大轮次 {_flow.MaxRounds}，强制停止", LogLevel.Warn);
                            break;
                        }
                        Emit(EngineEventType.RoundChanged,
                             $"第 {CurrentRound} / {_flow.MaxRounds} 轮",
                             LogLevel.Info, round: CurrentRound);
                    }

                    // ── 终态处理 ─────────────────────────────
                    if (CurrentStateId == "DONE")
                    {
                        Emit(EngineEventType.Completed, "✅ 全部处理完成！", LogLevel.Ok);
                        return;
                    }
                    if (CurrentStateId == "ERROR")
                    {
                        Emit(EngineEventType.NeedIntervention,
                             "⛔ 流程遇到错误，已暂停——请手动处理后点「继续」", LogLevel.Error);
                        _paused = true;
                        WaitWhilePaused();
                        if (!_running) return;
                        CurrentStateId = startState.Id;
                        continue;
                    }

                    // ── 查找当前状态 ─────────────────────────
                    var state = _flow.States.FirstOrDefault(s => s.Id == CurrentStateId);
                    if (state == null)
                    {
                        Log($"✗ 未知状态 [{CurrentStateId}]，流程终止", LogLevel.Error);
                        return;
                    }

                    Emit(EngineEventType.StateChanged,
                         $"◆ {state.Label}", LogLevel.Info, stateId: state.Id);

                    // ── 等待前置条件（waitFor）──────────────
                    if (state.WaitFor != null)
                    {
                        // anyPopup 进入时先取窗口基线
                        if (state.WaitFor.Type == "anyPopup")
                            ResetPopupBaseline();

                        bool met = WaitForCondition(state.WaitFor,
                                                     state.TimeoutSec,
                                                     state.Label);
                        if (!met)
                        {
                            Log($"  ⏰ 等待超时，跳转 → {state.OnTimeout}", LogLevel.Warn);
                            CurrentStateId = state.OnTimeout;
                            continue;
                        }
                    }

                    if (_skipStep) { _skipStep = false; continue; }

                    // ── 评估 transitions，找第一个满足的 ────
                    Transition? matched = null;
                    foreach (var t in state.Transitions)
                    {
                        if (EvalCondition(t.Condition))
                        {
                            matched = t;
                            break;
                        }
                    }

                    if (matched == null)
                    {
                        // 没有任何条件匹配，等待后重试
                        Thread.Sleep(500);
                        continue;
                    }

                    // ── 执行动作列表 ─────────────────────────
                    foreach (var action in matched.Actions)
                    {
                        if (!_running) return;
                        WaitWhilePaused();

                        ExecuteAction(action);
                    }

                    CurrentStateId = matched.Next;
                }

                if (_running)
                    Emit(EngineEventType.Completed, "引擎已停止", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Emit(EngineEventType.Error, $"引擎异常: {ex.Message}", LogLevel.Error);
            }
        }

        // =====================================================
        //  等待条件满足（轮询 500ms）
        // =====================================================
        private bool WaitForCondition(Condition cond, int timeoutSec, string stateLabel)
        {
            Log($"  ⏳ 等待: {stateLabel}...", LogLevel.Wait);
            var deadline = timeoutSec > 0
                ? DateTime.Now.AddSeconds(timeoutSec)
                : DateTime.MaxValue;

            while (_running)
            {
                WaitWhilePaused();

                if (_skipStep)
                {
                    _skipStep = false;
                    Log("  ⏭ 已跳过等待", LogLevel.Warn);
                    return true;
                }

                if (EvalCondition(cond))
                    return true;

                if (timeoutSec > 0 && DateTime.Now >= deadline)
                    return false;

                Thread.Sleep(500);
            }
            return false;
        }

        // =====================================================
        //  条件求值
        // =====================================================
        private bool EvalCondition(Condition cond)
        {
            try
            {
                switch (cond.Type)
                {
                    case "always":
                        return true;

                    case "windowExists":
                        return _client.IsTrue(
                            _client.Send($"windowexists|{cond.Window}"));

                    case "windowNotExists":
                        return !_client.IsTrue(
                            _client.Send($"windowexists|{cond.Window}"));

                    case "controlExists":
                        return _client.IsTrue(
                            _client.Send($"exists|{cond.Window}|{cond.Control}"));

                    case "controlNotExists":
                        return !_client.IsTrue(
                            _client.Send($"exists|{cond.Window}|{cond.Control}"));

                    case "controlEnabled":
                        return _client.IsTrue(
                            _client.Send($"isenabled|{cond.Window}|{cond.Control}"));

                    case "anyPopup":
                        return DetectNewPopup();

                    case "popupBodyContains":
                        return (_popupBody + _popupTitle)
                            .IndexOf(cond.Text, StringComparison.OrdinalIgnoreCase) >= 0;

                    default:
                        Log($"  ⚠ 未知条件类型: {cond.Type}", LogLevel.Warn);
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        // =====================================================
        //  执行单个动作
        // =====================================================
        private void ExecuteAction(FlowAction action)
        {
            if (action.Sleep > 0)
            {
                Thread.Sleep(action.Sleep);
                return;
            }

            if (string.IsNullOrEmpty(action.Cmd)) return;

            // ── 虚拟命令：popupclick|confirm/cancel/yes/no ──
            if (action.Cmd.StartsWith("popupclick|"))
            {
                string btnKey  = action.Cmd.Split('|')[1].ToLower();
                string btnName = btnKey switch
                {
                    "confirm" => "确定",
                    "cancel"  => "取消",
                    "yes"     => "是",
                    "no"      => "否",
                    "close"   => "关闭",
                    _         => btnKey
                };

                if (!string.IsNullOrEmpty(_popupTitle))
                {
                    var r = _client.Send($"click|{_popupTitle}|{btnName}");
                    Log($"  → 弹窗[{_popupTitle}] 点击[{btnName}] {(r.StartsWith("OK") ? "✓" : "✗ " + r)}",
                        r.StartsWith("OK") ? LogLevel.Popup : LogLevel.Warn);
                    // 点击后重置弹窗状态
                    _popupTitle   = "";
                    _popupBody    = "";
                    _popupButtons = "";
                    // 同步更新基线（弹窗已关闭）
                    ResetPopupBaseline();
                }
                return;
            }

            // ── 普通 Agent 命令 ──────────────────────────────
            string result = _client.Send(action.Cmd);

            // 失败时尝试 Fallback
            if (!result.StartsWith("OK") && !string.IsNullOrEmpty(action.Fallback))
            {
                Log($"  △ [{action.Cmd.Split('|')[0]}] 失败，尝试备用命令", LogLevel.Warn);
                result = _client.Send(action.Fallback);
            }

            string verb = action.Cmd.Split('|')[0];
            Log($"  → {verb}: {(result.StartsWith("OK") ? "✓" : "✗ " + result)}",
                result.StartsWith("OK") ? LogLevel.Ok : LogLevel.Warn);
        }

        // =====================================================
        //  弹窗检测（snapshot diff）
        // =====================================================
        private void ResetPopupBaseline()
        {
            _baselineWindows = new HashSet<string>(GetCurrentWindows());
            _popupTitle   = "";
            _popupBody    = "";
            _popupButtons = "";
        }

        private bool DetectNewPopup()
        {
            var current = GetCurrentWindows();
            var newOnes = current.Except(_baselineWindows).ToList();
            if (newOnes.Count == 0) return false;

            _popupTitle = newOnes[0];

            // 读取弹窗正文和按钮
            var info = _client.Send($"popupinfo|{_popupTitle}");
            if (info.StartsWith("OK:"))
            {
                ParsePopupInfo(info.Substring(3));
                Log($"  🪟 弹窗 [{_popupTitle}] 内容: {_popupBody} | 按钮: {_popupButtons}",
                    LogLevel.Popup);
            }
            return true;
        }

        private IEnumerable<string> GetCurrentWindows()
        {
            var snap = _client.Send("snapshot");
            if (!snap.StartsWith("OK:")) return Array.Empty<string>();
            string content = snap.Substring(3);
            if (string.IsNullOrEmpty(content)) return Array.Empty<string>();
            return content.Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries);
        }

        private void ParsePopupInfo(string raw)
        {
            // 格式: title=xxx|body=xxx|buttons=xxx
            foreach (var seg in raw.Split('|'))
            {
                int eq = seg.IndexOf('=');
                if (eq < 0) continue;
                string key = seg.Substring(0, eq);
                string val = seg.Substring(eq + 1);
                switch (key)
                {
                    case "title":   _popupTitle   = val; break;
                    case "body":    _popupBody    = val; break;
                    case "buttons": _popupButtons = val; break;
                }
            }
        }

        // =====================================================
        //  工具方法
        // =====================================================
        private void WaitWhilePaused()
        {
            if (_paused)
                Emit(EngineEventType.Paused, "⏸ 已暂停", LogLevel.Warn);
            while (_paused && _running)
                Thread.Sleep(200);
            if (!_paused && _running && CurrentRound > 0)
                Emit(EngineEventType.Resumed, "▶ 已继续", LogLevel.Ok);
        }

        private void Log(string msg, LogLevel level = LogLevel.Info)
            => Emit(EngineEventType.Log, msg, level);

        private void Emit(EngineEventType type, string msg, LogLevel level,
                          string? stateId = null, int round = 0)
        {
            _emit(new EngineEvent
            {
                Type    = type,
                Message = msg,
                Level   = level,
                StateId = stateId,
                Round   = round
            });
        }
    }
}
