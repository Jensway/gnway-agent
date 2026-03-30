// ============================================================
//  MainForm.cs — GnwayAgent 自动化控制台 (WinForms · 浅色主题)
//  修复：连接测试改为后台线程，避免 UI 冻结
// ============================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GnwayController.Engine;
using GnwayController.Models;

namespace GnwayController
{
    public class MainForm : Form
    {
        // ── 设计令牌 ──────────────────────────────────────────
        static readonly Font   F_BODY   = new Font("Segoe UI", 9.5f);
        static readonly Font   F_SMALL  = new Font("Segoe UI", 8.5f);
        static readonly Font   F_BOLD   = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        static readonly Font   F_LOG    = new Font("Consolas",  9.5f);
        static readonly Font   F_TITLE  = new Font("Segoe UI", 11f, FontStyle.Bold);

        static readonly Color C_BG       = Color.FromArgb(242, 245, 250);  // 页面底色
        static readonly Color C_CARD     = Color.White;                     // 卡片/面板
        static readonly Color C_BORDER   = Color.FromArgb(210, 218, 232);  // 边框
        static readonly Color C_HDR_BG   = Color.FromArgb(37,  99, 185);   // 深蓝标题条
        static readonly Color C_HDR_TEXT = Color.White;
        static readonly Color C_ACCENT   = Color.FromArgb(37,  99, 185);
        static readonly Color C_TEXT     = Color.FromArgb(30,  40,  60);
        static readonly Color C_SUB      = Color.FromArgb(100, 116, 139);

        // 日志颜色
        static readonly Color C_OK    = Color.FromArgb(5,  150, 105);
        static readonly Color C_WAIT  = Color.FromArgb(180, 120,  0);
        static readonly Color C_POPUP = Color.FromArgb(7,  140, 190);
        static readonly Color C_WARN  = Color.FromArgb(200,  80,  0);
        static readonly Color C_ERR   = Color.FromArgb(185,  30,  30);
        static readonly Color C_DBG   = Color.FromArgb(148, 163, 184);

        // ── 控件引用 ─────────────────────────────────────────
        TextBox     _tbServer  = null!;
        ComboBox    _cbFlow    = null!;
        Button      _btnTest   = null!;
        Label       _lblConn   = null!;

        Panel       _pnlSteps  = null!;
        RichTextBox _rtLog     = null!;

        Button  _btnStart  = null!;
        Button  _btnPause  = null!;
        Button  _btnSkip   = null!;
        Button  _btnStop   = null!;
        Label   _lblRound  = null!;
        Label   _lblStatus = null!;
        ProgressBar _pb    = null!;

        // ── 引擎状态 ─────────────────────────────────────────
        FlowEngine?     _engine;
        FlowDefinition? _flow;
        string          _flowsDir = "";
        string          _currentStateId = "";
        Dictionary<string, StepSt> _stepSt = new Dictionary<string, StepSt>();
        public enum StepSt { Pending, Running, Done, Error }  // public 供 StepRow 使用

        // =====================================================
        public MainForm()
        {
            _flowsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "flows");
            InitUI();
            LoadFlowsList();
        }

        // =====================================================
        //  界面构建
        // =====================================================
        private void InitUI()
        {
            Text            = "GnwayAgent · 自动化控制台";
            Size            = new Size(1000, 660);
            MinimumSize     = new Size(820, 520);
            StartPosition   = FormStartPosition.CenterScreen;
            BackColor       = C_BG;
            Font            = F_BODY;

            // ── 顶部标题栏 ───────────────────────────────────
            var header = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 52,
                BackColor = C_HDR_BG
            };
            Controls.Add(header);

            var lblTitle = new Label
            {
                Text      = "GnwayAgent 自动化控制台",
                Font      = F_TITLE,
                ForeColor = C_HDR_TEXT,
                AutoSize  = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Location  = new Point(16, 0),
                Size      = new Size(320, 52),
                BackColor = Color.Transparent
            };
            header.Controls.Add(lblTitle);

            // ── 工具栏（配置行）──────────────────────────────
            var toolbar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 52,
                BackColor = C_CARD,
                Padding   = new Padding(12, 0, 12, 0)
            };
            // 底部边框线
            toolbar.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(C_BORDER, 1), 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
            Controls.Add(toolbar);

            int x = 12;
            toolbar.Controls.Add(MkLabel("服务器 IP", toolbar, new Point(x, 10)));
            _tbServer = new TextBox
            {
                Text      = ".",
                Width     = 145,
                Location  = new Point(x + 5, 28),
                Font      = F_BODY,
                BorderStyle = BorderStyle.FixedSingle
            };
            toolbar.Controls.Add(_tbServer); x += 160;

            toolbar.Controls.Add(MkLabel("流程文件", toolbar, new Point(x, 10)));
            _cbFlow = new ComboBox
            {
                Width        = 210,
                Location     = new Point(x, 27),
                DropDownStyle= ComboBoxStyle.DropDownList,
                Font         = F_BODY,
                FlatStyle    = FlatStyle.System
            };
            toolbar.Controls.Add(_cbFlow); x += 222;

            _btnTest = MkBtn("连接测试", toolbar, new Point(x, 26), 90, C_ACCENT, Color.White);
            _btnTest.Click += OnTestConnect; x += 102;

            _lblConn = new Label
            {
                Location   = new Point(x, 31),
                AutoSize   = true,
                ForeColor  = C_SUB,
                Font       = F_SMALL,
                BackColor  = Color.Transparent
            };
            toolbar.Controls.Add(_lblConn);

            // ── 主体 — SplitContainer ─────────────────────────
            var splitter = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                SplitterWidth    = 6,
                SplitterDistance = 230,
                BackColor        = C_BG,
                Orientation      = Orientation.Vertical,
                Panel1MinSize    = 160,
                Panel2MinSize    = 300
            };
            Controls.Add(splitter);
            splitter.BringToFront();

            // 左：步骤面板
            BuildStepPanel(splitter.Panel1);

            // 右：日志面板
            BuildLogPanel(splitter.Panel2);

            // ── 底部状态栏 ───────────────────────────────────
            var statusBar = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 50,
                BackColor = C_CARD,
                Padding   = new Padding(10, 0, 10, 0)
            };
            statusBar.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(C_BORDER, 1), 0, 0, statusBar.Width, 0);
            Controls.Add(statusBar);
            statusBar.BringToFront();

            _btnStart = MkBtn("▶ 启动", statusBar, new Point(10, 10), 88, Color.FromArgb(22,101,52), Color.White);
            _btnStart.Click += OnStart;

            _btnPause = MkBtn("⏸ 暂停", statusBar, new Point(106, 10), 88, Color.FromArgb(120, 53, 15), Color.White);
            _btnPause.Enabled = false;
            _btnPause.Click  += OnPause;

            _btnSkip = MkBtn("⏭ 跳过", statusBar, new Point(202, 10), 88, Color.FromArgb(30, 64, 175), Color.White);
            _btnSkip.Enabled = false;
            _btnSkip.Click  += OnSkip;

            _btnStop = MkBtn("⏹ 停止", statusBar, new Point(298, 10), 88, Color.FromArgb(153, 27, 27), Color.White);
            _btnStop.Enabled = false;
            _btnStop.Click  += OnStop;

            var btnClear = MkBtn("清日志", statusBar, new Point(400, 10), 80, C_BG, C_TEXT);
            btnClear.Click += (_, __) => _rtLog.Clear();

            _pb = new ProgressBar
            {
                Width  = 0,
                Height = 6,
                Location = new Point(0, statusBar.Height - 6),
                Style  = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 30,
                Visible = false
            };
            _pb.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            statusBar.Controls.Add(_pb);

            // 状态信息
            _lblRound = new Label
            {
                Location  = new Point(498, 15),
                AutoSize  = true,
                Font      = F_BOLD,
                ForeColor = C_ACCENT,
                BackColor = Color.Transparent
            };
            statusBar.Controls.Add(_lblRound);

            _lblStatus = new Label
            {
                Location  = new Point(630, 15),
                AutoSize  = false,
                Width     = 300,
                Font      = F_BODY,
                ForeColor = C_SUB,
                BackColor = Color.Transparent,
                Text      = "就绪"
            };
            statusBar.Controls.Add(_lblStatus);
        }

        private void BuildStepPanel(SplitterPanel panel)
        {
            // 标题
            var hdr = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 34,
                BackColor = Color.FromArgb(37, 99, 185)
            };
            var hdrLbl = new Label
            {
                Text      = "执行步骤",
                Dock      = DockStyle.Fill,
                Font      = F_BOLD,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            hdr.Controls.Add(hdrLbl);
            panel.Controls.Add(hdr);

            // 步骤列表
            _pnlSteps = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = C_CARD,
                AutoScroll= true,
                Padding   = new Padding(0)
            };
            panel.Controls.Add(_pnlSteps);

            // 右边框
            panel.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(C_BORDER, 1), panel.Width - 1, 0, panel.Width - 1, panel.Height);
        }

        private void BuildLogPanel(SplitterPanel panel)
        {
            var hdr = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 34,
                BackColor = Color.FromArgb(37, 99, 185)
            };
            var hdrLbl = new Label
            {
                Text      = "实时日志",
                Dock      = DockStyle.Fill,
                Font      = F_BOLD,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            hdr.Controls.Add(hdrLbl);
            panel.Controls.Add(hdr);

            _rtLog = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = C_CARD,
                ForeColor   = C_TEXT,
                BorderStyle = BorderStyle.None,
                ReadOnly    = true,
                Font        = F_LOG,
                ScrollBars  = RichTextBoxScrollBars.Vertical
            };
            panel.Controls.Add(_rtLog);
        }

        // =====================================================
        //  重建步骤列表（每次加载流程时）
        // =====================================================
        private void RebuildStepList()
        {
            _pnlSteps.SuspendLayout();
            _pnlSteps.Controls.Clear();

            if (_flow == null) { _pnlSteps.ResumeLayout(); return; }

            for (int i = _flow.States.Count - 1; i >= 0; i--)
            {
                var st  = _flow.States[i];
                int idx = i;
                var row = new StepRow(idx + 1, st.Label, st.Id);
                row.Dock = DockStyle.Top;
                _pnlSteps.Controls.Add(row);
            }

            _pnlSteps.ResumeLayout();
        }

        private void RefreshStepList()
        {
            foreach (Control c in _pnlSteps.Controls)
            {
                if (c is StepRow row)
                    row.SetStatus(
                        row.StateId == _currentStateId ? StepSt.Running
                        : _stepSt.TryGetValue(row.StateId, out var s) ? s
                        : StepSt.Pending);
            }
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
                _stepSt.Clear();
                foreach (var st in _flow.States)
                    _stepSt[st.Id] = StepSt.Pending;

                RebuildStepList();
                AppendLog($"✓ 已加载流程：{_flow.Name}（{_flow.States.Count} 步）", LogLevel.Ok);
            }
            catch (Exception ex)
            {
                AppendLog($"✗ 流程加载失败：{ex.Message}", LogLevel.Error);
            }
        }

        // =====================================================
        //  事件：连接测试（异步，不阻塞 UI）
        // =====================================================
        private async void OnTestConnect(object? sender, EventArgs e)
        {
            _btnTest.Enabled   = false;
            _lblConn.ForeColor = C_SUB;
            _lblConn.Text      = "连接中…";

            string server = _tbServer.Text.Trim();
            var client = new AgentClient(server, timeoutMs: 8000);

            string r = await Task.Run(() => client.Send("snapshot"));

            if (r.StartsWith("OK"))
            {
                int wndCount = r.Length > 3
                    ? r.Substring(3).Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries).Length
                    : 0;
                _lblConn.ForeColor = Color.FromArgb(22, 101, 52);
                _lblConn.Text      = $"✓ 连接正常（{wndCount} 个窗口）";
                AppendLog($"✓ Agent 连接成功，检测到 {wndCount} 个窗口", LogLevel.Ok);
            }
            else
            {
                _lblConn.ForeColor = Color.FromArgb(153, 27, 27);
                _lblConn.Text      = $"✗ {r.Replace("ERR:", "")}";
                AppendLog($"✗ 连接失败：{r}", LogLevel.Error);
            }

            _btnTest.Enabled = true;
        }

        // =====================================================
        //  事件：启动 / 暂停 / 跳过 / 停止
        // =====================================================
        private void OnStart(object? sender, EventArgs e)
        {
            if (_flow == null)
            {
                MessageBox.Show("请先选择流程文件", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _rtLog.Clear();
            ResetAllSteps();
            AppendLog($"▶ 启动流程：{_flow.Name}", LogLevel.Ok);

            var client = new AgentClient(_tbServer.Text.Trim());
            _engine = new FlowEngine(client, _flow, OnEngineEvent);
            _engine.Start();

            SetRunning(true);
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
            SetRunning(false);
            SetStatus("已停止", C_WARN);
        }

        // =====================================================
        //  引擎事件（跨线程回调，自动 Invoke）
        // =====================================================
        private void OnEngineEvent(EngineEvent evt)
        {
            if (InvokeRequired) { Invoke((Action)(() => OnEngineEvent(evt))); return; }

            AppendLog($"[{DateTime.Now:HH:mm:ss}] {evt.Message}", evt.Level);

            switch (evt.Type)
            {
                case EngineEventType.StateChanged:
                    if (evt.StateId != null)
                    {
                        if (!string.IsNullOrEmpty(_currentStateId)
                            && _stepSt.ContainsKey(_currentStateId))
                            _stepSt[_currentStateId] = StepSt.Done;

                        _currentStateId = evt.StateId;
                        if (_stepSt.ContainsKey(_currentStateId))
                            _stepSt[_currentStateId] = StepSt.Running;
                    }
                    RefreshStepList();
                    break;

                case EngineEventType.RoundChanged:
                    _lblRound.Text = $"第 {evt.Round} / {_flow?.MaxRounds} 轮";
                    ResetAllSteps();
                    break;

                case EngineEventType.NeedIntervention:
                    if (_stepSt.ContainsKey(_currentStateId))
                        _stepSt[_currentStateId] = StepSt.Error;
                    RefreshStepList();
                    SetStatus("⛔ 需要人工干预", C_ERR);
                    _btnPause.Text = "▶ 继续";
                    MessageBox.Show(evt.Message + "\n\n请手动处理后点「确定」恢复自动化。",
                                    "需要人工干预",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    _engine?.Resume();
                    _btnPause.Text = "⏸ 暂停";
                    SetStatus("▶ 运行中", C_OK);
                    break;

                case EngineEventType.Completed:
                    SetRunning(false);
                    SetStatus(evt.Message, C_OK);
                    break;

                case EngineEventType.Error:
                    SetStatus($"✗ {evt.Message}", C_ERR);
                    break;

                case EngineEventType.Paused:
                    SetStatus("⏸ 已暂停", C_WARN);
                    break;

                case EngineEventType.Resumed:
                    SetStatus("▶ 运行中", C_OK);
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
                LogLevel.Debug => C_DBG,
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
        private void SetRunning(bool running)
        {
            _btnStart.Enabled = !running;
            _btnPause.Enabled = running;
            _btnSkip.Enabled  = running;
            _btnStop.Enabled  = running;
            _cbFlow.Enabled   = !running;
            _tbServer.Enabled = !running;
            _btnTest.Enabled  = !running;
            _pb.Visible = running;
            if (_pb.Visible)
            {
                _pb.Width = ((Panel)_pb.Parent!).Width;
                _pb.Left  = 0;
            }
            if (!running) _btnPause.Text = "⏸ 暂停";
            SetStatus(running ? "▶ 运行中" : "就绪", running ? C_OK : C_SUB);
        }

        private void SetStatus(string text, Color color)
        {
            _lblStatus.Text      = text;
            _lblStatus.ForeColor = color;
        }

        private void ResetAllSteps()
        {
            _currentStateId = _flow?.States.FirstOrDefault(s => s.StartState)?.Id ?? "";
            if (_flow != null)
                foreach (var st in _flow.States)
                    _stepSt[st.Id] = StepSt.Pending;
            RefreshStepList();
        }

        // ── 工厂方法 ─────────────────────────────────────────
        private static Label MkLabel(string text, Control parent, Point loc)
        {
            var lbl = new Label
            {
                Text      = text,
                Location  = loc,
                AutoSize  = true,
                Font      = F_SMALL,
                ForeColor = C_SUB,
                BackColor = Color.Transparent
            };
            parent.Controls.Add(lbl);
            return lbl;
        }

        private static Button MkBtn(string text, Control parent, Point loc,
                                    int width, Color backColor, Color foreColor)
        {
            var btn = new Button
            {
                Text      = text,
                Location  = loc,
                Width     = width,
                Height    = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = backColor,
                ForeColor = foreColor,
                Font      = F_SMALL
            };
            btn.FlatAppearance.BorderSize  = 0;
            btn.FlatAppearance.MouseOverBackColor  = ControlPaint.Light(backColor, 0.2f);
            btn.FlatAppearance.MouseDownBackColor  = ControlPaint.Dark(backColor, 0.1f);
            parent.Controls.Add(btn);
            return btn;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _engine?.Stop();
            base.OnFormClosing(e);
        }
    }

    // =====================================================
    //  StepRow — 单条步骤行控件
    // =====================================================
    public class StepRow : Panel
    {
        public string StateId { get; }
        private readonly int    _index;
        private readonly string _label;
        private MainForm.StepSt _status = MainForm.StepSt.Pending; // access via ref

        static readonly Color C_RUNNING = Color.FromArgb(219, 234, 254);
        static readonly Color C_DONE    = Color.FromArgb(220, 252, 231);
        static readonly Color C_ERROR   = Color.FromArgb(254, 226, 226);
        static readonly Color C_PENDING = Color.White;

        static readonly Color BAR_RUNNING = Color.FromArgb(37,  99, 185);
        static readonly Color BAR_DONE    = Color.FromArgb(22, 101,  52);
        static readonly Color BAR_ERROR   = Color.FromArgb(153,  27,  27);
        static readonly Color BAR_PENDING = Color.FromArgb(200, 210, 230);

        public StepRow(int index, string label, string stateId)
        {
            _index  = index;
            _label  = label;
            StateId = stateId;

            Height    = 44;
            BackColor = C_PENDING;
            Padding   = new Padding(0);
            DoubleBuffered = true;
        }

        public void SetStatus(MainForm.StepSt status)
        {
            _status = status;
            BackColor = status switch
            {
                MainForm.StepSt.Running => C_RUNNING,
                MainForm.StepSt.Done    => C_DONE,
                MainForm.StepSt.Error   => C_ERROR,
                _                       => C_PENDING
            };
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;

            // 左侧色条
            Color barColor = _status switch
            {
                MainForm.StepSt.Running => BAR_RUNNING,
                MainForm.StepSt.Done    => BAR_DONE,
                MainForm.StepSt.Error   => BAR_ERROR,
                _                       => BAR_PENDING
            };
            g.FillRectangle(new SolidBrush(barColor),
                new Rectangle(0, 4, 4, Height - 8));

            // 序号圆
            var numRect = new Rectangle(10, 10, 22, 22);
            g.FillEllipse(new SolidBrush(barColor), numRect);
            TextRenderer.DrawText(g, _index.ToString(),
                new Font("Segoe UI", 8f, FontStyle.Bold),
                numRect, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            // 状态图标
            string icon = _status switch
            {
                MainForm.StepSt.Running => "►",
                MainForm.StepSt.Done    => "✓",
                MainForm.StepSt.Error   => "✗",
                _                       => "○"
            };
            Color textColor = _status == MainForm.StepSt.Running
                ? Color.FromArgb(30, 64, 175)
                : Color.FromArgb(50, 60, 80);

            var textRect = new Rectangle(40, 0, Width - 44, Height);
            TextRenderer.DrawText(g, $"{icon}  {_label}",
                new Font("Segoe UI", 9.5f,
                    _status == MainForm.StepSt.Running ? FontStyle.Bold : FontStyle.Regular),
                textRect, textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

            // 底部分隔线
            g.DrawLine(new Pen(Color.FromArgb(230, 236, 245), 1),
                6, Height - 1, Width - 6, Height - 1);
        }
    }
}
