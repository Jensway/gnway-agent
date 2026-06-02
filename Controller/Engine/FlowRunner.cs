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
        private const int DefaultPostActionDelayMs = 800;
        private const int GeneratePostClickMinDelayMs = 500;
        private const int GeneratePostClickMaxDelayMs = 6000;

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
                        WaitAfterAction(evt); // 给动作后的界面刷新留缓冲，生成单据采用智能等待

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

        private void WaitAfterAction(AutoEvent evt)
        {
            if (IsGenerateClick(evt))
            {
                WaitForGenerateStability(evt.WindowName);
                return;
            }

            Thread.Sleep(DefaultPostActionDelayMs);
        }

        private static bool IsGenerateClick(AutoEvent evt)
        {
            var a = evt.Action;
            return (a.Type == "click" || a.Type == "popupclick")
                && ((a.ControlName ?? "").Contains("生成")
                    || (evt.Name ?? "").Contains("生成")
                    || (a.Value ?? "").Contains("生成"));
        }

        private void WaitForGenerateStability(string windowName)
        {
            Log($"  ⏳ 生成动作完成，正在智能等待界面稳定...", LogLevel.Wait);
            Thread.Sleep(GeneratePostClickMinDelayMs);

            DateTime deadline = DateTime.Now.AddMilliseconds(GeneratePostClickMaxDelayMs);
            string lastHash = "";
            int stableCount = 0;

            while (_running && DateTime.Now < deadline)
            {
                WaitWhilePaused();

                try
                {
                    string res = _client.Send($"treehash|{windowName}");
                    if (res.StartsWith("OK:"))
                    {
                        string currentHash = res.Substring(3);
                        if (!string.IsNullOrEmpty(currentHash) && currentHash == lastHash)
                        {
                            stableCount++;
                            if (stableCount >= 2)
                            {
                                Log($"  ✅ 生成后界面已稳定，继续下一步", LogLevel.Ok);
                                return;
                            }
                        }
                        else
                        {
                            stableCount = 0;
                            lastHash = currentHash;
                        }
                    }
                }
                catch { }

                Thread.Sleep(300);
            }

            Log($"  ⏱ 生成后智能等待已到上限，继续下一步", LogLevel.Wait);
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
            List<string> rowPreview = new List<string>();
            var matchTerms = BuildGridNextMatchTerms(a.MatchText);
            int readableRows = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split(new[] { '\t' }, 2);
                bool isSelected = parts.Length > 0 && parts[0] == "[SELECTED]";
                string rowData = parts.Length > 1 ? parts[1] : lines[i];
                string normalizedRow = NormalizeGridText(rowData);

                if (!string.IsNullOrWhiteSpace(rowData)) readableRows++;
                if (rowPreview.Count < 5)
                {
                    rowPreview.Add(string.IsNullOrWhiteSpace(rowData)
                        ? "<空行>"
                        : rowData.Length > 80 ? rowData.Substring(0, 80) + "..." : rowData);
                }

                if (isSelected && selectedIdx < 0)
                {
                    selectedIdx = i;
                }

                var cols = rowData.Split('\t');
                string targetText = a.ColIndex < cols.Length
                    ? cols[a.ColIndex]
                    : rowData;
                string normalizedTarget = NormalizeGridText(targetText);
                bool hit = matchTerms.Any(term =>
                    normalizedTarget.Contains(term) || normalizedRow.Contains(term));

                if (hit)
                {
                    hitIndices.Add(i);
                }
            }

            if (hitIndices.Count == 0)
            {
                string preview = rowPreview.Count > 0 ? string.Join(" / ", rowPreview) : "<无行>";
                Log($"  🔎 未匹配到「{a.MatchText}」。已读取 {lines.Length} 行，可读内容 {readableRows} 行；前几行：{preview}", LogLevel.Warn);
                if (lines.Length > 0 && rowPreview.All(IsGridContainerPreview))
                {
                    int nextIdx = selectedIdx >= 0 ? selectedIdx + 1 : 1;
                    string moveResult = _client.Send($"gridselect|{evt.WindowName}|{a.ControlName}|{nextIdx}");
                    Log($"  ⚠ 只读到表格容器名，尝试下移到第 {nextIdx} 行继续判断：{OkOrErr(moveResult)}",
                        moveResult.StartsWith("OK") ? LogLevel.Warn : LogLevel.Error);
                    return false;
                }
                if (lines.Length == 0)
                {
                    Log($"  ⚠ 表格未暴露可读取行，暂按当前选中行继续执行，不判定完成", LogLevel.Warn);
                    return false;
                }
                if (lines.Length > 0 && readableRows == 0)
                {
                    Log($"  ⚠ 表格行存在但内容为空，暂不判定完成；请等待下一轮或检查表格读取", LogLevel.Warn);
                    return false;
                }
                Log($"  ✅ 表格中已无「{a.MatchText}」行，全部处理完成", LogLevel.Ok);
                return true;
            }

            Log($"  📊 [智能眼] 表格总揽：共扫描到 {lines.Length} 行数据，其中 {hitIndices.Count} 行状态包含「{a.MatchText}」(需处理)。", LogLevel.Info);
            if (selectedIdx >= 0)
            {
                bool selIsPending = hitIndices.Contains(selectedIdx);
                Log($"  👁 [智能眼] 检测到当前正处于第 {selectedIdx} 行，该行状态: {(selIsPending ? "【待处理】" : "【无需处理/已完成】")}", LogLevel.Info);
            }
            else
            {
                Log($"  👁 [智能眼] 当前表格没有明确的高亮选中行，将从上到下按顺序检索", LogLevel.Info);
            }

            int targetIdx = -1;

            if (selectedIdx >= 0 && hitIndices.Contains(selectedIdx))
            {
                targetIdx = selectedIdx;
                Log($"  → 当前第 {targetIdx} 行已选中且含\"{a.MatchText}\"，直接锁定当前行动作", LogLevel.Ok);
            }
            else
            {
                var nextIndices = hitIndices.Where(idx => idx > selectedIdx).ToList();
                if (nextIndices.Any())
                {
                    targetIdx = nextIndices.First();
                    Log($"  → 越过已处理数据，锁定后续第 {targetIdx} 行为目标", LogLevel.Info);
                }
                else if (hitIndices.Any())
                {
                    int fallbackIdx = hitIndices.First();
                    if (fallbackIdx == selectedIdx)
                    {
                        Log($"  ✅ 触底且只剩当前行(大概率为界面未刷新)，视为处理完成", LogLevel.Ok);
                        return true;
                    }
                    targetIdx = fallbackIdx;
                    Log($"  → 回到列表顶部，锁定第 {targetIdx} 行为目标", LogLevel.Info);
                }
            }

            if (targetIdx < 0)
            {
                Log($"  ⚠ 未能确定下一条「{a.MatchText}」目标行，本轮跳过选行以避免误触", LogLevel.Warn);
                return false;
            }

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

        private static List<string> BuildGridNextMatchTerms(string matchText)
        {
            var rawTerms = (matchText ?? "")
                .Split(new[] { '|', ',', '，', ';', '；', '/', '、' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeGridText)
                .Where(x => !string.IsNullOrEmpty(x))
                .ToList();

            if (rawTerms.Count == 0)
                rawTerms.Add("待生成");

            if (rawTerms.Contains("待生成"))
            {
                rawTerms.Add("未生成");
                rawTerms.Add("待处理");
                rawTerms.Add("未处理");
            }

            return rawTerms.Distinct().ToList();
        }

        private static string NormalizeGridText(string text)
            => new string((text ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());

        private static bool IsGridContainerPreview(string text)
            => System.Text.RegularExpressions.Regex.IsMatch((text ?? "").Trim(), @"^(Frame|ThunderRT6Frame|ThunderRT6UserControl|Panel|Pane)\d*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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
