import sys

# --- Agent.cs ---
with open('Agent/Agent.cs', 'r', encoding='utf-8') as f:
    agent_code = f.read()

agent_old = '''                if (ct == ControlType.DataItem || ct == ControlType.ListItem || ct == ControlType.TreeItem || ct == ControlType.Custom)
                {
                    var cols = new List<string>();
                    var cell = walker.GetFirstChild(child);'''

agent_new = '''                if (ct == ControlType.DataItem || ct == ControlType.ListItem || ct == ControlType.TreeItem || ct == ControlType.Custom)
                {
                    bool isSelected = false;
                    try {
                        if (child.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object? sp)) {
                            isSelected = ((SelectionItemPattern)sp).Current.IsSelected;
                        } else {
                            isSelected = child.Current.HasKeyboardFocus;
                        }
                    } catch { }

                    var cols = new List<string>();
                    cols.Add(isSelected ? "[SELECTED]" : "[UNSELECTED]");

                    var cell = walker.GetFirstChild(child);'''

if agent_old in agent_code:
    agent_code = agent_code.replace(agent_old, agent_new)
    print("Agent.cs patched.")
else:
    print("Error: Could not find target code in Agent.cs")
    
with open('Agent/Agent.cs', 'w', encoding='utf-8') as f:
    f.write(agent_code)


# --- FlowRunner.cs ---
with open('Controller/Engine/FlowRunner.cs', 'r', encoding='utf-8') as f:
    flow_code = f.read()

flow_emit_old = '''                    Emit(EngineEventType.StateChanged,
                         $"步骤 {current + 1}/{count}：{evt.Name}",
                         LogLevel.Info, stateId: stepId);'''

flow_emit_new = '''                    Emit(EngineEventType.StateChanged,
                         $"步骤 {current + 1}/{count}：{evt.Name}",
                         LogLevel.Info, stateId: stepId, round: current);'''

if flow_emit_old in flow_code:
    flow_code = flow_code.replace(flow_emit_old, flow_emit_new)
else:
    print("Warning: Emit for FlowRunner not found, might have been patched.")

flow_grid_old = '''            var lines = rowsResult.Substring(3)
                                  .Split(new[] { '\\n', '\\r' }, StringSplitOptions.RemoveEmptyEntries);

            int foundIdx = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var cols = lines[i].Split('\\t');
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
            Log($"  → 选行 [{a.ControlName}] 第{foundIdx}行（含\\"{a.MatchText}\\"）：{OkOrErr(selResult)}",
                selResult.StartsWith("OK") ? LogLevel.Ok : LogLevel.Warn);

            return false;'''

flow_grid_new = '''            var lines = rowsResult.Substring(3)
                                  .Split(new[] { '\\n', '\\r' }, StringSplitOptions.RemoveEmptyEntries);

            int foundIdx = -1;
            int selectedPendingIdx = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split(new[] { '\\t' }, 2);
                bool isSelected = parts.Length > 0 && parts[0] == "[SELECTED]";
                string rowData = parts.Length > 1 ? parts[1] : lines[i];

                var cols = rowData.Split('\\t');
                // 若 ColIndex 在范围内就按列检查，否则检查整行
                bool hit = a.ColIndex < cols.Length
                    ? cols[a.ColIndex].Contains(a.MatchText)
                    : rowData.Contains(a.MatchText);
                
                if (hit)
                {
                    if (isSelected) {
                        selectedPendingIdx = i;
                        break;
                    }
                    if (foundIdx < 0) {
                        foundIdx = i;
                    }
                }
            }

            int targetIdx = selectedPendingIdx >= 0 ? selectedPendingIdx : foundIdx;

            if (targetIdx < 0)
            {
                Log($"  ✅ 表格中已无「{a.MatchText}」行，全部处理完成", LogLevel.Ok);
                return true; // 通知 RunLoop 流程完成
            }

            if (targetIdx == selectedPendingIdx)
            {
                Log($"  → 当前第{targetIdx}行已选中且含\\"{a.MatchText}\\"，直接处理，跳过选中步骤", LogLevel.Ok);
            }
            else
            {
                string selResult = _client.Send(
                    $"gridselect|{evt.WindowName}|{a.ControlName}|{targetIdx}");
                Log($"  → 选行 [{a.ControlName}] 第{targetIdx}行（含\\"{a.MatchText}\\"）：{OkOrErr(selResult)}",
                    selResult.StartsWith("OK") ? LogLevel.Ok : LogLevel.Warn);
            }

            return false;'''

if flow_grid_old in flow_code:
    flow_code = flow_code.replace(flow_grid_old, flow_grid_new)
    print("FlowRunner.cs patched.")
else:
    print("Error: Could not find target code in FlowRunner.cs")
    
with open('Controller/Engine/FlowRunner.cs', 'w', encoding='utf-8') as f:
    f.write(flow_code)


# --- MainForm.cs ---
with open('Controller/MainForm.cs', 'r', encoding='utf-8') as f:
    main_code = f.read()

# Add timer variables
main_vars_old = '''        // 当前控件树数据（type/name/enabled列表）
        List<ControlInfo> _treeData = new();'''

main_vars_new = '''        // 当前控件树数据（type/name/enabled列表）
        List<ControlInfo> _treeData = new();

        // 用于流程步骤UI状态显示
        private int _currentStepIndex = -1;
        private System.Windows.Forms.Timer _blinkTimer = new System.Windows.Forms.Timer { Interval = 400 };
        private bool _blinkState = false;'''

if main_vars_old in main_code:
    main_code = main_code.replace(main_vars_old, main_vars_new)

# Add timer logic in constructor
main_init_old = '''        public MainForm()
        {
            string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
            _store = new EventStore(baseDir);
            InitUI();
        }'''

main_init_new = '''        public MainForm()
        {
            string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
            _store = new EventStore(baseDir);
            InitUI();

            _blinkTimer.Tick += (s, e) => {
                if (_currentStepIndex >= 0 && _currentStepIndex < _lvSteps.Items.Count) {
                    _blinkState = !_blinkState;
                    var item = _lvSteps.Items[_currentStepIndex];
                    if (_blinkState) {
                        item.Text = "▶ " + (_currentStepIndex + 1);
                        item.BackColor = Color.FromArgb(250, 240, 240); // 浅红色高亮背景
                    } else {
                        item.Text = "   " + (_currentStepIndex + 1);
                        item.BackColor = C_CARD;
                    }
                }
            };
        }'''

if main_init_old in main_code:
    main_code = main_code.replace(main_init_old, main_init_new)

# Handle UI state reset
main_refresh_old = '''        private void RefreshFlowListView()
        {
            _lvSteps.Items.Clear();
            _nudStart.Maximum = Math.Max(1, _flowSteps.Count);

            for (int i = 0; i < _flowSteps.Count; i++)
            {
                string stepId = _flowSteps[i];
                var ev = _allEvents.FirstOrDefault(e => e.Id == stepId);
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(ev?.Name ?? stepId);
                item.SubItems.Add(ev?.WindowName ?? "");
                item.Tag = stepId;
                _lvSteps.Items.Add(item);
            }
        }'''

main_refresh_new = '''        private void RefreshFlowListView()
        {
            _lvSteps.Items.Clear();
            _nudStart.Maximum = Math.Max(1, _flowSteps.Count);

            for (int i = 0; i < _flowSteps.Count; i++)
            {
                string stepId = _flowSteps[i];
                var ev = _allEvents.FirstOrDefault(e => e.Id == stepId);
                var item = new ListViewItem("   " + (i + 1).ToString());
                item.SubItems.Add(ev?.Name ?? stepId);
                item.SubItems.Add(ev?.WindowName ?? "");
                item.Tag = stepId;
                item.ForeColor = Color.Gray; // 默认为灰色（待执行状态）
                _lvSteps.Items.Add(item);
            }
        }'''

if main_refresh_old in main_code:
    main_code = main_code.replace(main_refresh_old, main_refresh_new)

# Update runner event processor
main_evt_old = '''        private void OnRunnerEvent(EngineEvent evt)
        {
            if (InvokeRequired) { Invoke((Action)(() => OnRunnerEvent(evt))); return; }

            AppendLog($"[{DateTime.Now:HH:mm:ss}] {evt.Message}", evt.Level);

            switch (evt.Type)
            {
                case EngineEventType.Completed:
                case EngineEventType.Error:
                    SetRunning(false);
                    break;
                case EngineEventType.Paused:
                    _btnPause.Text = "▶ 继续";
                    break;
                case EngineEventType.Resumed:
                    _btnPause.Text = "⏸ 暂停";
                    break;
            }
        }'''

main_evt_new = '''        private void OnRunnerEvent(EngineEvent evt)
        {
            if (InvokeRequired) { Invoke((Action)(() => OnRunnerEvent(evt))); return; }

            // 优化了执行日志的显示，对不同级别的日志进行了格式化
            string logPrefix = evt.Level == LogLevel.Ok ? "[成功]" : evt.Level == LogLevel.Error ? "[错误]" : evt.Level == LogLevel.Warn ? "[警告]" : "[信息]";
            AppendLog($"[{DateTime.Now:HH:mm:ss}] {logPrefix} {evt.Message}", evt.Level);

            switch (evt.Type)
            {
                case EngineEventType.StateChanged:
                    // 清理上一个步骤的状态为绿色（完成）
                    if (_currentStepIndex >= 0 && _currentStepIndex < _lvSteps.Items.Count)
                    {
                        var prevItem = _lvSteps.Items[_currentStepIndex];
                        prevItem.Text = "✓ " + (_currentStepIndex + 1);
                        prevItem.BackColor = C_CARD;
                        prevItem.ForeColor = Color.Green;
                    }
                    
                    _currentStepIndex = evt.Round; // 获取当前正在执行的步骤索引

                    // 设置当前步骤为红色（正在执行），并启动闪烁
                    if (_currentStepIndex >= 0 && _currentStepIndex < _lvSteps.Items.Count)
                    {
                        var curItem = _lvSteps.Items[_currentStepIndex];
                        curItem.ForeColor = Color.Red;
                        _blinkState = true;
                        curItem.Text = "▶ " + (_currentStepIndex + 1);
                        _lvSteps.EnsureVisible(_currentStepIndex); // 自动滚动
                    }
                    break;

                case EngineEventType.Completed:
                    if (_currentStepIndex >= 0 && _currentStepIndex < _lvSteps.Items.Count)
                    {
                        var lastItem = _lvSteps.Items[_currentStepIndex];
                        lastItem.Text = "✓ " + (_currentStepIndex + 1);
                        lastItem.BackColor = C_CARD;
                        lastItem.ForeColor = Color.Green;
                    }
                    SetRunning(false);
                    break;

                case EngineEventType.Error:
                    SetRunning(false);
                    if (_currentStepIndex >= 0 && _currentStepIndex < _lvSteps.Items.Count)
                    {
                        var errItem = _lvSteps.Items[_currentStepIndex];
                        errItem.Text = "✗ " + (_currentStepIndex + 1);
                        errItem.BackColor = Color.FromArgb(255, 230, 230);
                        errItem.ForeColor = Color.DarkRed;
                    }
                    break;

                case EngineEventType.Paused:
                    _btnPause.Text = "▶ 继续";
                    if (_currentStepIndex >= 0 && _currentStepIndex < _lvSteps.Items.Count)
                        _lvSteps.Items[_currentStepIndex].BackColor = Color.LightYellow;
                    break;

                case EngineEventType.Resumed:
                    _btnPause.Text = "⏸ 暂停";
                    break;
            }
        }'''

if main_evt_old in main_code:
    main_code = main_code.replace(main_evt_old, main_evt_new)

# Fix start flow logic to reset colors
main_start_old = '''            _rtLog.Clear();
            AppendLog($"▶ 流程启动，从第 {startStep + 1} 步，超时 {timeoutSec}s", C_OK);'''

main_start_new = '''            _rtLog.Clear();
            // 每次启动时重置所有步骤状态
            RefreshFlowListView();
            _currentStepIndex = -1;
            _blinkTimer.Start();
            AppendLog($"▶ 流程启动，从第 {startStep + 1} 步，超时 {timeoutSec}s(等待中)", C_OK);'''

if main_start_old in main_code:
    main_code = main_code.replace(main_start_old, main_start_new)

# Stop the timer when stopped
main_run_false_old = '''        private void SetRunning(bool running)
        {
            _btnStart.Enabled = !running;'''

main_run_false_new = '''        private void SetRunning(bool running)
        {
            if (!running && _blinkTimer.Enabled) _blinkTimer.Stop();
            _btnStart.Enabled = !running;'''

if main_run_false_old in main_code:
    main_code = main_code.replace(main_run_false_old, main_run_false_new)
    print("MainForm.cs patched.")
else:
    print("Error: Could not find target code in MainForm.cs")
    
with open('Controller/MainForm.cs', 'w', encoding='utf-8') as f:
    f.write(main_code)

