// ============================================================
//  FlowRunner.cs — 基于录制事件的状态机执行引擎（后台线程）
//
//  核心流程：
//    1. 从第 startStep 步开始，按 steps[] 循环执行
//    2. 每步前：调用 listcontrols 与该事件的 Snapshot 比对
//       · 完全匹配（type+name+enabled 三元组顺序一致）才执行
//       · 不匹配则 500ms 后重试，直到超时
//    3. gridnext 步骤：读取表格所有行 → 找第一条含 MatchText 的行
//       · 若找不到 → 所有数据已处理完 → 流程 DONE
//    4. 执行动作 → 推进到下一步（循环）
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GnwayController.Models;

namespace GnwayController.Engine
{
    public class FlowRunner
    {
        private readonly AgentClient         _client;
        private readonly List<AutoEvent>     _events;   // 全部事件（按ID查找）
        private readonly List<string>        _stepIds;  // 有序步骤 ID
        private readonly int                 _startStep;
        private readonly int                 _timeoutSec;
        private readonly Action<EngineEvent> _emit;

        private volatile bool _running;
        private volatile bool _paused;
        private volatile bool _skipStep;
        private Thread?       _thread;

        public bool IsRunning => _running;
        public bool IsPaused  => _paused;

        public FlowRunner(AgentClient client,
                          List<AutoEvent> events,
                          List<string> stepIds,
                          int startStep,
                          int timeoutSec,
                          Action<EngineEvent> emit)
        {
            _client     = client;
            _events     = events;
            _stepIds    = stepIds;
            _startStep  = startStep;
            _timeoutSec = timeoutSec;
            _emit       = emit;
        }

        // ── 控制接口 ─────────────────────────────────────────

        public void Start()
        {
            _running  = true;
            _paused   = false;
            _skipStep = false;
            _thread   = new Thread(RunLoop)
            {
                IsBackground = true,
                Name         = "FlowRunner"
            };
            _thread.Start();
        }

        public void Stop()     => _running  = false;
        public void Pause()    => _paused   = true;
        public void Resume()   => _paused   = false;
        public void SkipStep() => _skipStep = true;

        // =====================================================
        //  主循环
        // =====================================================
        private void RunLoop()
        {
            try
            {
                int count   = _stepIds.Count;
                int current = _startStep % count;

                while (_running)
                {
                    WaitWhilePaused();
                    if (!_running) break;

                    string stepId = _stepIds[current];
                    var evt = _events.FirstOrDefault(e => e.Id == stepId);

                    if (evt == null)
                    {
                        Log($"⚠ 找不到事件 [{stepId}]，跳过", LogLevel.Warn);
                        current = (current + 1) % count;
                        continue;
                    }

                    Emit(EngineEventType.StateChanged,
                         $"步骤 {current + 1}/{count}：{evt.Name}",
                         LogLevel.Info, stateId: stepId, round: current);

                    // ── 等待控件树匹配 ───────────────────────
                    Log($"  ⏳ 等待窗口 [{evt.WindowName}] 控件树就绪...", LogLevel.Wait);

                    bool matched = WaitForSnapshot(evt);
                    if (!matched)
                    {
                        Log($"  ⏰ 超时：控件树未就绪（步骤 {current + 1} {evt.Name}）", LogLevel.Error);
                        Emit(EngineEventType.Error,
                             $"超时：窗口 [{evt.WindowName}] 控件树不匹配", LogLevel.Error);
                        _running = false;
                        break;
                    }

                    if (_skipStep) { _skipStep = false; }
                    else
                    {
                        // ── 执行动作 ───────────────────────────
                        bool done = ExecuteAction(evt);
                        Thread.Sleep(800); // 增加强制等待，增强下移一行或点击后的状态转移稳健性

                        if (done)
                        {
                            Emit(EngineEventType.Completed,
                                 "✅ 所有待处理数据已处理完成，流程结束", LogLevel.Ok);
                            return;
                        }
                    }

                    current = (current + 1) % count;
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
        //  等待控件树与快照完全匹配
        // =====================================================
        private bool WaitForSnapshot(AutoEvent evt)
        {
            var deadline = _timeoutSec > 0
                ? DateTime.Now.AddSeconds(_timeoutSec)
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

                if (SnapshotMatches(evt))
                {
                    // 控件存在且可用，进行 Data Stability Sniffing 保护
                    if (WaitForDataStability(evt.WindowName, deadline))
                    {
                        return true;
                    }
                }

                if (_timeoutSec > 0 && DateTime.Now >= deadline)
                    return false;

                Thread.Sleep(500);
            }
            return false;
        }

        private bool WaitForDataStability(string windowName, DateTime deadline)
        {
            Log($"  ⏳ 控件已就位，正在等候窗口底层数据渲染稳定...", LogLevel.Wait);

            int stableCount = 0;
            string lastHash = "";
            
            while (_running)
            {
                WaitWhilePaused();
                
                if (_timeoutSec > 0 && DateTime.Now >= deadline)
                    return false;

                try
                {
                    string res = _client.Send($"treehash|{windowName}");
                    if (res.StartsWith("OK:"))
                    {
                        string currentHash = res.Substring(3);
                        if (currentHash == lastHash && !string.IsNullOrEmpty(currentHash))
                        {
                            stableCount++;
                            if (stableCount >= 6) // 连续6次(约 3 秒)无变化即认为彻底稳定，防范多段式刷新
                            {
                                Log($"  ✅ 数据界面已彻底稳定", LogLevel.Ok);
                                return true;
                            }
                        }
                        else
                        {
                            stableCount = 0;
                            lastHash = currentHash;
                        }
                    }
                    else
                    {
                        // 若由于弹窗消失等导致获取失败，可能是界面跳走，直接认为不稳定并退出嗅探交由外层继续抢占
                        return false; 
                    }
                }
                catch { }

                Thread.Sleep(500);
            }
            return false;
        }

        private bool SnapshotMatches(AutoEvent evt)
        {
            try
            {
                if (!string.IsNullOrEmpty(evt.Action.ControlName))
                {
                    // 先测 exists，它内部已经在 Agent 端实现了多窗口查找降级
                    string existsRes = _client.Send($"exists|{evt.WindowName}|{evt.Action.ControlName}");
                    if (!existsRes.StartsWith("OK:true")) return false;

                    // 若存在，对交互动作补充 isenabled 检查防冻结误触
                    if (evt.Action.Type == "click" || evt.Action.Type == "input" || evt.Action.Type == "select")
                    {
                        string enabledRes = _client.Send($"isenabled|{evt.WindowName}|{evt.Action.ControlName}");
                        if (!enabledRes.StartsWith("OK:true")) return false;
                    }
                    return true;
                }
                else
                {
                    string result = _client.Send($"windowexists|{evt.WindowName}");
                    return result.StartsWith("OK:true");
                }
            }
            catch { return false; }
        }

        // =====================================================
        //  执行单个动作；返回 true = 流程完成（gridnext 无更多行）
        // =====================================================
        private bool ExecuteAction(AutoEvent evt)
        {
            var a = evt.Action;

            switch (a.Type)
            {
                case "click":
                {
                    string cmd = string.IsNullOrWhiteSpace(a.Value) 
                        ? $"click|{evt.WindowName}|{a.ControlName}"
                        : $"click|{evt.WindowName}|{a.ControlName}|{a.Value}";
                    string r = _client.Send(cmd);
                    Log($"  → 点击 [{a.ControlName}]：{OkOrErr(r)}", r.StartsWith("OK") ? LogLevel.Ok : LogLevel.Warn);
                    break;
                }

                case "input":
                {
                    string r = _client.Send($"input|{evt.WindowName}|{a.ControlName}|{a.Value}");
                    Log($"  → 输入 [{a.Value}] 至 [{a.ControlName}]：{OkOrErr(r)}", r.StartsWith("OK") ? LogLevel.Ok : LogLevel.Warn);
                    break;
                }

                case "sendkeys":
                {
                    string r = _client.Send($"sendkeys|{evt.WindowName}|{a.ControlName}|{a.Value}");
                    Log($"  → 裸发按键 [{a.Value}] 至 [{a.ControlName}]：{OkOrErr(r)}", r.StartsWith("OK") ? LogLevel.Ok : LogLevel.Warn);
                    break;
                }

                case "select":
                {
                    string r = _client.Send($"select|{evt.WindowName}|{a.ControlName}|{a.Value}");
                    Log($"  → 选择 [{a.ControlName}] \"{a.Value}\"：{OkOrErr(r)}", r.StartsWith("OK") ? LogLevel.Ok : LogLevel.Warn);
                    break;
                }

                case "gridnext":
                {
                    return ExecuteGridNext(evt, a);
                }

                case "sleep":
                {
                    Log($"  → 等待 {a.SleepMs} ms", LogLevel.Info);
                    Thread.Sleep(a.SleepMs);
                    break;
                }

                case "popupclick":
                {
                    string cmd = string.IsNullOrWhiteSpace(a.Value) 
                        ? $"click|{evt.WindowName}|{a.ControlName}"
                        : $"click|{evt.WindowName}|{a.ControlName}|{a.Value}";
                    string r = _client.Send(cmd);
                    Log($"  → 弹窗 [{evt.WindowName}] 点击 [{a.ControlName}]：{OkOrErr(r)}",
                        r.StartsWith("OK") ? LogLevel.Popup : LogLevel.Warn);
                    break;
                }

                default:
                    Log($"  ⚠ 未知动作类型 [{a.Type}]", LogLevel.Warn);
                    break;
            }
            return false; // 未完成，继续循环
        }

        private bool ExecuteGridNext(AutoEvent evt, EventAction a)
        {
            string rowsResult = _client.Send($"gridrows|{evt.WindowName}|{a.ControlName}");
            if (!rowsResult.StartsWith("OK:"))
            {
                Log($"  ✗ 读取表格失败：{rowsResult}", LogLevel.Warn);
                return false;
            }

            var lines = rowsResult.Substring(3)
                                  .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            int selectedIdx = -1;
            List<int> hitIndices = new List<int>();

            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split(new[] { '\t' }, 2);
                bool isSelected = parts.Length > 0 && parts[0] == "[SELECTED]";
                string rowData = parts.Length > 1 ? parts[1] : lines[i];

                if (isSelected) selectedIdx = i;

                var cols = rowData.Split('\t');
                bool hit = a.ColIndex < cols.Length
                    ? cols[a.ColIndex].Contains(a.MatchText)
                    : rowData.Contains(a.MatchText);
                
                if (hit)
                {
                    hitIndices.Add(i);
                }
            }

            if (hitIndices.Count == 0)
            {
                Log($"  ✅ 表格中已无「{a.MatchText}」行，全部处理完成", LogLevel.Ok);
                return true; // 通知 RunLoop 流程完成
            }

            int targetIdx = -1;

            // 优先检查当前选中行是否待处理
            if (selectedIdx >= 0 && hitIndices.Contains(selectedIdx))
            {
                targetIdx = selectedIdx;
                Log($"  → 当前第{targetIdx}行已选中且含\"{a.MatchText}\"，直接锁定当前行动作", LogLevel.Ok);
            }
            else
            {
                // 若当前行不是待处理，或者未识别到选中行，则尝试寻找选中行之后的待处理行
                targetIdx = hitIndices.FirstOrDefault(idx => idx > selectedIdx);
                if (targetIdx == 0 && !hitIndices.Contains(0) && selectedIdx < 0) 
                {
                    // Fallback
                    targetIdx = hitIndices[0]; 
                }
                else if (targetIdx <= 0 && selectedIdx >= 0) 
                {
                    // 如果选中行之后没有了，就回到顶部找第一个
                    targetIdx = hitIndices[0];
                }
            }

            // [重要修正] 必须无论如何都在这里触发一次强制选中事件，
            // 防范由于底层软件状态重绘导致的假选中（看似有底色实则无 Detail Panel 加载）
            string selResult = _client.Send(
                $"gridselect|{evt.WindowName}|{a.ControlName}|{targetIdx}");
            Log($"  → 强制触发选中 [{a.ControlName}] 第{targetIdx}行（含\"{a.MatchText}\"）：{OkOrErr(selResult)}",
                selResult.StartsWith("OK") ? LogLevel.Ok : LogLevel.Warn);

            return false;
        }

        // =====================================================
        //  工具
        // =====================================================
        private void WaitWhilePaused()
        {
            bool wasPaused = _paused;
            if (_paused)
                Emit(EngineEventType.Paused, "⏸ 已暂停", LogLevel.Warn);
            
            while (_paused && _running)
                Thread.Sleep(200);
            
            if (wasPaused && !_paused && _running)
                Emit(EngineEventType.Resumed, "▶ 已继续", LogLevel.Ok);
        }

        private static string OkOrErr(string r)
            => r.StartsWith("OK") ? "✓" : "✗ " + r;

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
