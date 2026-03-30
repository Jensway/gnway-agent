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
                         LogLevel.Info, stateId: stepId);

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
                    return true;

                if (_timeoutSec > 0 && DateTime.Now >= deadline)
                    return false;

                Thread.Sleep(500);
            }
            return false;
        }

        private bool SnapshotMatches(AutoEvent evt)
        {
            try
            {
                string result = _client.Send($"listcontrols|{evt.WindowName}|10");
                if (!result.StartsWith("OK:")) return false;

                var current  = EventStore.ParseControlList(result);
                var expected = evt.Snapshot.Controls;

                if (current.Count != expected.Count) return false;

                for (int i = 0; i < current.Count; i++)
                {
                    if (current[i].Type    != expected[i].Type)    return false;
                    if (current[i].Name    != expected[i].Name)    return false;
                    if (current[i].Enabled != expected[i].Enabled) return false;
                }
                return true;
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
                    string r = _client.Send($"click|{evt.WindowName}|{a.ControlName}");
                    Log($"  → 点击 [{a.ControlName}]：{OkOrErr(r)}", r.StartsWith("OK") ? LogLevel.Ok : LogLevel.Warn);
                    break;
                }

                case "input":
                {
                    string r = _client.Send($"input|{evt.WindowName}|{a.ControlName}|{a.Value}");
                    Log($"  → 输入 [{a.ControlName}] \"{a.Value}\"：{OkOrErr(r)}", r.StartsWith("OK") ? LogLevel.Ok : LogLevel.Warn);
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
                    string r = _client.Send($"click|{evt.WindowName}|{a.ControlName}");
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

        /// <summary>gridnext 逻辑：读取所有行，找第一条含 MatchText 的，选中它</summary>
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

            int foundIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var cols = lines[i].Split('\t');
                // 若 ColIndex 在范围内就按列检查，否则检查整行
                bool hit = a.ColIndex < cols.Length
                    ? cols[a.ColIndex].Contains(a.MatchText)
                    : lines[i].Contains(a.MatchText);
                if (hit) { foundIdx = i; break; }
            }

            if (foundIdx < 0)
            {
                Log($"  ✅ 表格中已无「{a.MatchText}」行，全部处理完成", LogLevel.Ok);
                return true; // 通知 RunLoop 流程完成
            }

            string selResult = _client.Send(
                $"gridselect|{evt.WindowName}|{a.ControlName}|{foundIdx}");
            Log($"  → 选行 [{a.ControlName}] 第{foundIdx}行（含\"{a.MatchText}\"）：{OkOrErr(selResult)}",
                selResult.StartsWith("OK") ? LogLevel.Ok : LogLevel.Warn);

            return false;
        }

        // =====================================================
        //  工具
        // =====================================================
        private void WaitWhilePaused()
        {
            if (_paused)
                Emit(EngineEventType.Paused, "⏸ 已暂停", LogLevel.Warn);
            while (_paused && _running)
                Thread.Sleep(200);
            if (!_paused && _running)
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
