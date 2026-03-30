// ============================================================
//  MainForm.cs — GnwayAgent 自动化控制台 (WinForms)
//  深色主题，实时步骤面板 + 彩色日志 + 控制按钮
// ============================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GnwayController.Engine;
using GnwayController.Models;

namespace GnwayController
{
    public class MainForm : Form
    {
        // ── 颜色系 ──────────────────────────────────────────
        static readonly Color C_BG       = Color.FromArgb(20, 22, 35);
        static readonly Color C_PANEL    = Color.FromArgb(28, 32, 50);
        static readonly Color C_ACCENT   = Color.FromArgb(60, 130, 220);
        static readonly Color C_TEXT     = Color.FromArgb(210, 218, 235);
        static readonly Color C_SUBTEXT  = Color.FromArgb(130, 140, 160);
        static readonly Color C_LOG_BG   = Color.FromArgb(14, 16, 24);
        static readonly Color C_OK       = Color.FromArgb(80, 200, 140);
        static readonly Color C_WAIT     = Color.FromArgb(255, 210, 80);
        static readonly Color C_POPUP    = Color.FromArgb(80, 200, 220);
        static readonly Color C_WARN     = Color.FromArgb(255, 160, 60);
        static readonly Color C_ERR      = Color.FromArgb(230, 80, 80);
        static readonly Color C_STEP_CUR = Color.FromArgb(60, 130, 220);
        static readonly Color C_STEP_OK  = Color.FromArgb(40, 140, 90);

        // ── 控件 ────────────────────────────────────────────
        TextBox   _tbServer  = null!;
        ComboBox  _cbFlow    = null!;
        Button    _btnTest   = null!;
        Label     _lblConn   = null!;

        ListBox   _lbSteps   = null!;
        RichTextBox _rtLog   = null!;

        Button _btnStart  = null!;
        Button _btnPause  = null!;
        Button _btnSkip   = null!;
        Button _btnStop   = null!;
        Label  _lblRound  = null!;
        Label  _lblStatus = null!;

        // ── 引擎状态 ─────────────────────────────────────────
        FlowEngine?     _engine;
        FlowDefinition? _flow;
        string          _flowsDir = "";

        // 步骤 → 当前状态
        string _currentStateId = "";
        Dictionary<string, StepStatus> _stepStatus = new Dictionary<string, StepStatus>();

        enum StepStatus { Pending, Running, Done, Error }

        // =====================================================
        public MainForm()
        {
            _flowsDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "flows");

            InitUI();
            LoadFlowsList();
        }

        // =====================================================
        //  界面构建
        // =====================================================
        private void InitUI()
        {
            Text            = "GnwayAgent 自动化控制台";
            Size            = new Size(960, 620);
            MinimumSize     = new Size(780, 500);
            StartPosition   = FormStartPosition.CenterScreen;
            BackColor       = C_BG;
            ForeColor       = C_TEXT;
            Font            = new Font("Segoe UI", 9.5f);

            // ── 顶部栏 ──────────────────────────────────────
            var topPanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 48,
                BackColor = C_PANEL,
                Padding   = new Padding(10, 8, 10, 8)
            };
            Controls.Add(topPanel);

            var lblSrv = MakeLabel("服务器 IP:", topPanel);
            lblSrv.Location = new Point(10, 14);

            _tbServer = new TextBox
            {
                Text      = ".",
                Width     = 145,
                Location  = new Point(80, 11),
                BackColor = Color.FromArgb(38, 42, 62),
                ForeColor = C_TEXT,
                BorderStyle = BorderStyle.FixedSingle,
                Font      = new Font("Segoe UI", 10f)
            };
            topPanel.Controls.Add(_tbServer);

            var lblFl = MakeLabel("流程:", topPanel);
            lblFl.Location = new Point(240, 14);

            _cbFlow = new ComboBox
            {
                Width        = 200,
                Location     = new Point(280, 11),
                BackColor    = Color.FromArgb(38, 42, 62),
                ForeColor    = C_TEXT,
                DropDownStyle= ComboBoxStyle.DropDownList,
                FlatStyle    = FlatStyle.Flat
            };
            topPanel.Controls.Add(_cbFlow);

            _btnTest = MakeButton("连接测试", topPanel);
            _btnTest.Location = new Point(495, 9);
            _btnTest.Width    = 85;
            _btnTest.Click   += OnTestConnect;

            _lblConn = MakeLabel("", topPanel);
            _lblConn.Location  = new Point(590, 14);
            _lblConn.Width     = 260;
            _lblConn.ForeColor = C_SUBTEXT;

            // ── 主体：左（步骤）+ 右（日志） ────────────────
            var split = new SplitContainer
            {
                Dock         = DockStyle.Fill,
                Orientation  = Orientation.Vertical,
                SplitterWidth= 4,
                SplitterDistance = 210,
                BackColor    = C_BG
            };
            Controls.Add(split);
            split.BringToFront();

            // 左面板
            var leftHeader = MakeLabel("■ 执行步骤", split.Panel1);
            leftHeader.Location  = new Point(0, 0);
            leftHeader.Width     = 210;
            leftHeader.Height    = 26;
            leftHeader.BackColor = C_PANEL;
            leftHeader.ForeColor = C_ACCENT;
            leftHeader.Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            leftHeader.TextAlign = ContentAlignment.MiddleCenter;

            _lbSteps = new ListBox
            {
                Dock            = DockStyle.Fill,
                BackColor       = Color.FromArgb(22, 26, 40),
                ForeColor       = C_TEXT,
                BorderStyle     = BorderStyle.None,
                DrawMode        = DrawMode.OwnerDrawFixed,
                ItemHeight      = 36,
                SelectionMode   = SelectionMode.None,
                Font            = new Font("Segoe UI", 9f)
            };
            _lbSteps.DrawItem += DrawStepItem;
            split.Panel1.Controls.Add(_lbSteps);
            split.Panel1.Controls.Add(leftHeader);

            // 右面板（日志）
            var rightHeader = MakeLabel("■ 实时日志", split.Panel2);
            rightHeader.Dock      = DockStyle.Top;
            rightHeader.Height    = 26;
            rightHeader.BackColor = C_PANEL;
            rightHeader.ForeColor = C_ACCENT;
            rightHeader.Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            rightHeader.TextAlign = ContentAlignment.MiddleCenter;

            _rtLog = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = C_LOG_BG,
                ForeColor   = C_TEXT,
                BorderStyle = BorderStyle.None,
                ReadOnly    = true,
                Font        = new Font("Consolas", 9.5f),
                ScrollBars  = RichTextBoxScrollBars.Vertical
            };
            split.Panel2.Controls.Add(_rtLog);
            split.Panel2.Controls.Add(rightHeader);

            // ── 底部栏 ──────────────────────────────────────
            var botPanel = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 48,
                BackColor = C_PANEL
            };
            Controls.Add(botPanel);
            botPanel.BringToFront();

            _btnStart = MakeButton("▶ 启动", botPanel);
            _btnStart.Location  = new Point(10, 9);
            _btnStart.BackColor = Color.FromArgb(30, 100, 60);
            _btnStart.Click    += OnStart;

            _btnPause = MakeButton("⏸ 暂停", botPanel);
            _btnPause.Location  = new Point(105, 9);
            _btnPause.Enabled   = false;
            _btnPause.Click    += OnPause;

            _btnSkip = MakeButton("⏭ 跳过", botPanel);
            _btnSkip.Location = new Point(200, 9);
            _btnSkip.Enabled  = false;
            _btnSkip.Click   += OnSkip;

            _btnStop = MakeButton("⏹ 停止", botPanel);
            _btnStop.Location  = new Point(295, 9);
            _btnStop.BackColor = Color.FromArgb(100, 30, 30);
            _btnStop.Enabled   = false;
            _btnStop.Click    += OnStop;

            var btnClear = MakeButton("🗑 清日志", botPanel);
            btnClear.Location  = new Point(400, 9);
            btnClear.BackColor = Color.FromArgb(50, 52, 70);
            btnClear.Click    += (_, __) => _rtLog.Clear();

            var lblRoundLbl = MakeLabel("轮次:", botPanel);
            lblRoundLbl.Location = new Point(510, 15);

            _lblRound = MakeLabel("—", botPanel);
            _lblRound.Location  = new Point(548, 15);
            _lblRound.Width     = 70;
            _lblRound.ForeColor = C_ACCENT;

            _lblStatus = MakeLabel("就绪", botPanel);
            _lblStatus.Location  = new Point(630, 15);
            _lblStatus.Width     = 280;
            _lblStatus.ForeColor = C_SUBTEXT;
        }

        // =====================================================
        //  步骤列表自定义绘制
        // =====================================================
        private void DrawStepItem(object? sender, DrawItemEventArgs e)
        {
            if (_flow == null || e.Index < 0 || e.Index >= _flow.States.Count) return;
            var st = _flow.States[e.Index];

            var status = _stepStatus.TryGetValue(st.Id, out var s) ? s : StepStatus.Pending;
            bool isCurrent = st.Id == _currentStateId;

            Color bg = isCurrent ? Color.FromArgb(35, 60, 100)
                     : status == StepStatus.Done  ? Color.FromArgb(24, 44, 34)
                     : status == StepStatus.Error ? Color.FromArgb(50, 24, 24)
                     : Color.FromArgb(22, 26, 40);

            e.Graphics.FillRectangle(new SolidBrush(bg), e.Bounds);

            // 左侧状态色条
            Color barColor = isCurrent ? C_STEP_CUR
                           : status == StepStatus.Done  ? C_STEP_OK
                           : status == StepStatus.Error ? C_ERR
                           : C_SUBTEXT;
            e.Graphics.FillRectangle(new SolidBrush(barColor),
                new Rectangle(e.Bounds.X, e.Bounds.Y + 4, 4, e.Bounds.Height - 8));

            // 圆形序号
            string num = $"{e.Index + 1}";
            var numRect = new Rectangle(e.Bounds.X + 10, e.Bounds.Y + 8, 22, 20);
            e.Graphics.FillEllipse(new SolidBrush(barColor), numRect);
            TextRenderer.DrawText(e.Graphics, num,
                new Font("Segoe UI", 8f, FontStyle.Bold),
                numRect, Color.Black, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // 步骤名称
            string icon = isCurrent ? "►"
                        : status == StepStatus.Done  ? "✓"
                        : status == StepStatus.Error ? "✗"
                        : "○";
            var textRect = new Rectangle(e.Bounds.X + 38, e.Bounds.Y, e.Bounds.Width - 38, e.Bounds.Height);
            Color textColor = isCurrent ? Color.White : C_TEXT;
            TextRenderer.DrawText(e.Graphics, $"{icon} {st.Label}",
                _lbSteps.Font, textRect, textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        // =====================================================
        //  流程文件加载
        // =====================================================
        private void LoadFlowsList()
        {
            _cbFlow.Items.Clear();
            if (!Directory.Exists(_flowsDir)) return;

            foreach (string f in Directory.GetFiles(_flowsDir, "*.json"))
                _cbFlow.Items.Add(Path.GetFileNameWithoutExtension(f));

            if (_cbFlow.Items.Count > 0)
            {
                _cbFlow.SelectedIndex = 0;
                TryLoadFlow();
            }

            _cbFlow.SelectedIndexChanged += (_, __) => TryLoadFlow();
        }

        private void TryLoadFlow()
        {
            if (_cbFlow.SelectedItem == null) return;
            string path = Path.Combine(_flowsDir, _cbFlow.SelectedItem + ".json");
            try
            {
                _flow = FlowLoader.Load(path);
                _lbSteps.Items.Clear();
                _stepStatus.Clear();
                foreach (var st in _flow.States)
                {
                    _lbSteps.Items.Add(st);
                    _stepStatus[st.Id] = StepStatus.Pending;
                }
                AppendLog($"✓ 已加载流程: {_flow.Name}（{_flow.States.Count} 个状态）", LogLevel.Ok);
            }
            catch (Exception ex)
            {
                AppendLog($"✗ 流程加载失败: {ex.Message}", LogLevel.Error);
            }
        }

        // =====================================================
        //  事件处理
        // =====================================================
        private void OnTestConnect(object? sender, EventArgs e)
        {
            var client = new AgentClient(_tbServer.Text.Trim());
            _lblConn.ForeColor = C_WAIT;
            _lblConn.Text      = "测试中...";
            Update();

            string r = client.Send("snapshot");
            if (r.StartsWith("OK"))
            {
                _lblConn.ForeColor = C_OK;
                _lblConn.Text = "✓ 连接正常";
                AppendLog($"✓ Agent 连接正常，获取到 {r.Split(new[]{"|||"},0).Length} 个窗口", LogLevel.Ok);
            }
            else
            {
                _lblConn.ForeColor = C_ERR;
                _lblConn.Text = $"✗ {r}";
                AppendLog($"✗ 连接失败: {r}", LogLevel.Error);
            }
        }

        private void OnStart(object? sender, EventArgs e)
        {
            if (_flow == null)
            {
                MessageBox.Show("请先选择流程文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ResetStepStatus();
            _rtLog.Clear();
            AppendLog($"▶ 启动流程: {_flow.Name}", LogLevel.Ok);

            var client = new AgentClient(_tbServer.Text.Trim());
            _engine    = new FlowEngine(client, _flow, OnEngineEvent);
            _engine.Start();

            SetRunningState(true);
        }

        private void OnPause(object? sender, EventArgs e)
        {
            if (_engine == null) return;
            if (_engine.IsPaused)
            {
                _engine.Resume();
                _btnPause.Text = "⏸ 暂停";
            }
            else
            {
                _engine.Pause();
                _btnPause.Text = "▶ 继续";
            }
        }

        private void OnSkip(object? sender, EventArgs e) => _engine?.SkipStep();

        private void OnStop(object? sender, EventArgs e)
        {
            _engine?.Stop();
            SetRunningState(false);
            _lblStatus.Text = "已停止";
        }

        // =====================================================
        //  引擎事件处理（在 UI 线程中执行）
        // =====================================================
        private void OnEngineEvent(EngineEvent evt)
        {
            if (InvokeRequired)
            {
                Invoke((Action)(() => OnEngineEvent(evt)));
                return;
            }

            AppendLog($"[{DateTime.Now:HH:mm:ss}] {evt.Message}", evt.Level);

            switch (evt.Type)
            {
                case EngineEventType.StateChanged:
                    if (evt.StateId != null)
                    {
                        // 将上一个状态标记为 Done
                        if (!string.IsNullOrEmpty(_currentStateId)
                            && _stepStatus.ContainsKey(_currentStateId))
                            _stepStatus[_currentStateId] = StepStatus.Done;

                        _currentStateId = evt.StateId;
                        if (_stepStatus.ContainsKey(_currentStateId))
                            _stepStatus[_currentStateId] = StepStatus.Running;
                    }
                    _lbSteps.Invalidate();
                    break;

                case EngineEventType.RoundChanged:
                    _lblRound.Text = $"{evt.Round} / {_flow?.MaxRounds}";
                    ResetStepStatus();
                    break;

                case EngineEventType.NeedIntervention:
                    if (!string.IsNullOrEmpty(_currentStateId)
                        && _stepStatus.ContainsKey(_currentStateId))
                        _stepStatus[_currentStateId] = StepStatus.Error;
                    _lbSteps.Invalidate();
                    _lblStatus.Text      = "⛔ 需要人工干预";
                    _lblStatus.ForeColor = C_ERR;
                    _btnPause.Text       = "▶ 继续";
                    MessageBox.Show(evt.Message + "\n\n请手动处理后点「确定」继续。",
                                    "需要人工干预",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _engine?.Resume();
                    _btnPause.Text = "⏸ 暂停";
                    break;

                case EngineEventType.Completed:
                    SetRunningState(false);
                    _lblStatus.Text      = evt.Message;
                    _lblStatus.ForeColor = C_OK;
                    break;

                case EngineEventType.Error:
                    _lblStatus.Text      = $"✗ {evt.Message}";
                    _lblStatus.ForeColor = C_ERR;
                    break;

                case EngineEventType.Paused:
                    _lblStatus.Text = "⏸ 已暂停";
                    break;

                case EngineEventType.Resumed:
                    _lblStatus.Text      = "▶ 运行中";
                    _lblStatus.ForeColor = C_OK;
                    break;
            }
        }

        // =====================================================
        //  日志输出（带颜色）
        // =====================================================
        private void AppendLog(string text, LogLevel level = LogLevel.Info)
        {
            if (_rtLog.InvokeRequired)
            {
                _rtLog.Invoke((Action)(() => AppendLog(text, level)));
                return;
            }

            Color color = level switch
            {
                LogLevel.Ok    => C_OK,
                LogLevel.Wait  => C_WAIT,
                LogLevel.Popup => C_POPUP,
                LogLevel.Warn  => C_WARN,
                LogLevel.Error => C_ERR,
                LogLevel.Debug => C_SUBTEXT,
                _              => C_TEXT
            };

            _rtLog.SelectionStart  = _rtLog.TextLength;
            _rtLog.SelectionLength = 0;
            _rtLog.SelectionColor  = color;
            _rtLog.AppendText(text + "\n");
            _rtLog.SelectionColor  = C_TEXT;
            _rtLog.ScrollToCaret();
        }

        // =====================================================
        //  辅助
        // =====================================================
        private void SetRunningState(bool running)
        {
            _btnStart.Enabled = !running;
            _btnPause.Enabled = running;
            _btnSkip.Enabled  = running;
            _btnStop.Enabled  = running;
            _cbFlow.Enabled   = !running;
            _tbServer.Enabled = !running;
            _lblStatus.Text   = running ? "▶ 运行中" : "就绪";
            _lblStatus.ForeColor = running ? C_OK : C_SUBTEXT;
        }

        private void ResetStepStatus()
        {
            if (_flow == null) return;
            _currentStateId = _flow.States.FirstOrDefault(s => s.StartState)?.Id ?? "";
            foreach (var st in _flow.States)
                _stepStatus[st.Id] = StepStatus.Pending;
            _lbSteps.Invalidate();
        }

        // ── 工厂方法 ─────────────────────────────────────────
        private static Label MakeLabel(string text, Control parent)
        {
            var lbl = new Label
            {
                Text      = text,
                AutoSize  = true,
                ForeColor = Color.FromArgb(180, 188, 210),
                BackColor = Color.Transparent
            };
            parent.Controls.Add(lbl);
            return lbl;
        }

        private static Button MakeButton(string text, Control parent)
        {
            var btn = new Button
            {
                Text      = text,
                Width     = 88,
                Height    = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 50, 75),
                ForeColor = Color.FromArgb(210, 218, 235),
                Font      = new Font("Segoe UI", 9f)
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(70, 80, 110);
            parent.Controls.Add(btn);
            return btn;
        }
    }
}
