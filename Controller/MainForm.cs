// ============================================================
//  MainForm.cs — GnwayAgent 可视化录制执行平台
//
//  布局（单窗口）：
//    ┌─ 顶部工具栏 ─────────────────────────────────────────┐
//    │  服务器 IP │ 连接测试 │ 目标窗口 │ 刷新控件树         │
//    ├─ 左区(40%) ──────────┬─ 右区(60%) ──────────────────┤
//    │  控件树 DataGridView  │  上：已录制事件列表          │
//    │  （类型/名称/操作）   │  下：流程步骤 + 执行控制 + 日志│
//    │  测试结果区           │                              │
//    └──────────────────────┴──────────────────────────────┘
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
        static readonly Font F_BODY  = new Font("Segoe UI", 9.5f);
        static readonly Font F_SMALL = new Font("Segoe UI", 8.5f);
        static readonly Font F_BOLD  = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        static readonly Font F_LOG   = new Font("Consolas",  9f);
        static readonly Font F_TITLE = new Font("Segoe UI", 11f, FontStyle.Bold);
        static readonly Font F_MONO  = new Font("Consolas",  8.5f);

        static readonly Color C_BG      = Color.FromArgb(242, 245, 250);
        static readonly Color C_CARD    = Color.White;
        static readonly Color C_BORDER  = Color.FromArgb(210, 218, 232);
        static readonly Color C_HDR_BG  = Color.FromArgb(37,  99, 185);
        static readonly Color C_ACCENT  = Color.FromArgb(37,  99, 185);
        static readonly Color C_TEXT    = Color.FromArgb(30,  40,  60);
        static readonly Color C_SUB     = Color.FromArgb(100, 116, 139);
        static readonly Color C_OK      = Color.FromArgb(5,  150, 105);
        static readonly Color C_WARN    = Color.FromArgb(180, 120,  0);
        static readonly Color C_ERR     = Color.FromArgb(185,  30,  30);
        static readonly Color C_POPUP   = Color.FromArgb(7,  140, 190);
        static readonly Color C_WAIT    = Color.FromArgb(180, 120,  0);
        static readonly Color C_DBG     = Color.FromArgb(148, 163, 184);

        // ── 控件引用 ─────────────────────────────────────────
        TextBox        _tbServer   = null!;
        Button         _btnTest    = null!;
        Button         _btnRefreshWins = null!;
        Label          _lblConn    = null!;

        // 左区：主细表明细
        DataGridView   _dgvWindows = null!;
        DataGridView   _dgvTree    = null!;
        Label          _lblTreeSt  = null!;
        RichTextBox    _rtTestOut  = null!;

        // 右上：事件列表
        ListView       _lvEvents   = null!;
        Button         _btnEvtTest = null!;
        Button         _btnEvtSave = null!;
        Button         _btnEvtDel  = null!;
        Button         _btnEvtUp   = null!;
        Button         _btnEvtDown = null!;

        // 右下：流程步骤
        ListView       _lvSteps    = null!;
        Button         _btnStepUp  = null!;
        Button         _btnStepDn  = null!;
        Button         _btnStepAdd = null!;
        Button         _btnStepRm  = null!;
        NumericUpDown  _nudStart   = null!;
        NumericUpDown  _nudTimeout = null!;
        Button         _btnStart   = null!;
        Button         _btnPause   = null!;
        Button         _btnStop    = null!;
        RichTextBox    _rtLog      = null!;

        // ── 状态 ──────────────────────────────────────────────
        EventStore         _store     = null!;
        List<AutoEvent>    _allEvents = new();
        List<string>       _flowSteps = new();   // 有序步骤 ID
        FlowRunner?        _runner;

        // 当前控件树数据（type/name/enabled列表）
        List<(string Type, string Name, bool Enabled)> _treeData = new();

        // =====================================================
        public MainForm()
        {
            string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
            _store = new EventStore(baseDir);
            InitUI();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ReloadEvents();
        }

        // =====================================================
        //  界面构建
        // =====================================================
        private void InitUI()
        {
            Text          = "GnwayAgent · 自动化录制执行平台";
            Size          = new Size(1240, 740);
            MinimumSize   = new Size(960, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor     = C_BG;
            Font          = F_BODY;

            // (The custom header panel has been removed to avoid redundancy with the native window title bar)

            // ── 工具栏 ───────────────────────────────────────
            var toolbar = new Panel {
                Dock = DockStyle.Top, Height = 56,
                BackColor = C_CARD, Padding = new Padding(10, 0, 10, 0)
            };
            toolbar.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(C_BORDER), 0, toolbar.Height - 1, toolbar.Width, toolbar.Height - 1);
            Controls.Add(toolbar);

            int x = 10;
            toolbar.Controls.Add(Lbl("服务器 IP", toolbar, new Point(x, 8)));
            _tbServer = new TextBox {
                Text = ".", Width = 130, Location = new Point(x, 26),
                Font = F_BODY, BorderStyle = BorderStyle.FixedSingle
            };
            toolbar.Controls.Add(_tbServer); x += 142;

            _btnTest = Btn("连接测试", toolbar, new Point(x, 24), 80, C_ACCENT, Color.White);
            _btnTest.Click += OnTestConnect; x += 90;

            _lblConn = new Label {
                Location = new Point(x, 29), AutoSize = true,
                ForeColor = C_SUB, Font = F_SMALL, BackColor = Color.Transparent
            };
            toolbar.Controls.Add(_lblConn); x += 145;

            _btnRefreshWins = Btn("🔄 获取全部窗口", toolbar, new Point(x, 24), 110, C_BG, C_TEXT);
            _btnRefreshWins.Click += OnGetWindows;
            toolbar.Controls.Add(_btnRefreshWins); x += 120;

            // ── 主体 SplitContainer ──────────────────────────
            var split = new SplitContainer {
                Dock = DockStyle.Fill,
                SplitterWidth = 5,
                BackColor = C_BG,
                Orientation = Orientation.Vertical,
                Panel1MinSize = 280
            };
            Controls.Add(split);
            split.BringToFront(); // [!!! FIX MANGLED FORM LAYOUT OVERLAP !!!]
            this.Load += (_, __) => split.SplitterDistance = (int)(ClientSize.Width * 0.40);

            BuildLeftPanel(split.Panel1);
            BuildRightPanel(split.Panel2);
        }

        // =====================================================
        //  左区：Master-Detail 布局
        // =====================================================
        private void BuildLeftPanel(SplitterPanel panel)
        {
            var splitLeft = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                SplitterWidth = 5, Panel1MinSize = 100, BackColor = C_BG
            };
            panel.Controls.Add(splitLeft);
            splitLeft.BringToFront();
            this.Load += (_, __) => splitLeft.SplitterDistance = 180;

            // --- 上半部：主表（窗口列表） ---
            SectionHeader("在线窗口 (Master)", splitLeft.Panel1, DockStyle.Top);
            _dgvWindows = new DataGridView {
                Dock = DockStyle.Fill, RowHeadersVisible = false, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AllowUserToResizeRows = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = C_CARD,
                BorderStyle = BorderStyle.None, ReadOnly = true, Font = F_SMALL, RowTemplate = { Height = 28 }
            };
            _dgvWindows.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "窗口名称", Name = "colWinName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _dgvWindows.SelectionChanged += OnWindowSelectionChanged;
            splitLeft.Panel1.Controls.Add(_dgvWindows);
            _dgvWindows.BringToFront();

            // --- 下半部：明细表（控件与动作组合） ---
            SectionHeader("窗口控件与内联动作 (Detail)", splitLeft.Panel2, DockStyle.Top);
            
            _dgvTree = new DataGridView {
                Dock = DockStyle.Fill,
                RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, RowTemplate = { Height = 30 },
                BackgroundColor = C_CARD, BorderStyle = BorderStyle.None, GridColor = C_BORDER, Font = F_SMALL,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 28, EditMode = DataGridViewEditMode.EditOnEnter
            };
            _dgvTree.ColumnHeadersDefaultCellStyle.BackColor  = Color.FromArgb(240, 244, 252);
            _dgvTree.ColumnHeadersDefaultCellStyle.Font       = F_SMALL;
            _dgvTree.ColumnHeadersDefaultCellStyle.ForeColor  = C_TEXT;
            _dgvTree.EnableHeadersVisualStyles = false;
            _dgvTree.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            _dgvTree.DefaultCellStyle.SelectionForeColor = C_TEXT;

            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "类型", Name = "colType", Width = 90, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "控件名称", Name = "colName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", Name = "colEnabled", Width = 40, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            
            var colAct = new DataGridViewComboBoxColumn { HeaderText = "操作", Name = "colAction", Width = 70, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox, SortMode = DataGridViewColumnSortMode.NotSortable };
            colAct.Items.AddRange("click", "input", "select", "gridnext", "popupclick", "sleep");
            colAct.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            _dgvTree.Columns.Add(colAct);

            var colVal = new DataGridViewTextBoxColumn { HeaderText = "测试值", Name = "colValue", Width = 90, SortMode = DataGridViewColumnSortMode.NotSortable };
            colVal.DefaultCellStyle.BackColor = Color.LightYellow;
            _dgvTree.Columns.Add(colVal);

            _dgvTree.Columns.Add(new DataGridViewButtonColumn { HeaderText = "测试", Name = "colTest", Text = "▶测试", UseColumnTextForButtonValue = true, Width = 55, SortMode = DataGridViewColumnSortMode.NotSortable });
            _dgvTree.Columns.Add(new DataGridViewButtonColumn { HeaderText = "保存", Name = "colSave", Text = "💾保存", UseColumnTextForButtonValue = true, Width = 55, SortMode = DataGridViewColumnSortMode.NotSortable });

            _dgvTree.CellContentClick += OnTreeActionClick;
            _dgvTree.CellFormatting   += OnTreeCellFormat;
            splitLeft.Panel2.Controls.Add(_dgvTree);

            // 底部：状态 + 测试输出 (附着在 Panel2)
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 155, BackColor = C_CARD };
            bottom.Paint += (s, e) => e.Graphics.DrawLine(new Pen(C_BORDER), 0, 0, bottom.Width, 0);
            splitLeft.Panel2.Controls.Add(bottom);

            _lblTreeSt = new Label {
                Text = "控件树空——请先点击上方任一在线窗口",
                Location = new Point(8, 6), AutoSize = true, ForeColor = C_SUB, Font = F_SMALL, BackColor = Color.Transparent
            };
            bottom.Controls.Add(_lblTreeSt);

            _rtTestOut = new RichTextBox {
                Location = new Point(8, 24), Size = new Size(0, 90), BackColor = Color.FromArgb(20, 24, 36),
                ForeColor = Color.FromArgb(200, 215, 240), Font = F_MONO, BorderStyle = BorderStyle.None,
                ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, WordWrap = true
            };
            bottom.Resize += (_, __) => _rtTestOut.Width = bottom.Width - 16;
            bottom.Controls.Add(_rtTestOut);

            var btnClearTest = Btn("清空输出", bottom, new Point(8, 122), 70, C_BG, C_SUB);
            btnClearTest.Click += (_, __) => _rtTestOut.Clear();
            bottom.Controls.Add(btnClearTest);

            _dgvTree.BringToFront(); // 让 Tree 占用剩余空间
        }

        // =====================================================
        //  右区：事件列表 + 流程步骤 + 执行日志
        // =====================================================
        private void BuildRightPanel(SplitterPanel panel)
        {
            var rightSplit = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 5,
                BackColor = C_BG,
                Panel1MinSize = 140
            };
            panel.Controls.Add(rightSplit);
            this.Load += (_, __) => rightSplit.SplitterDistance = 200;

            BuildEventsPanel(rightSplit.Panel1);
            BuildFlowPanel(rightSplit.Panel2);
        }

        // ── 右上：已录制事件 ──────────────────────────────────
        private void BuildEventsPanel(SplitterPanel panel)
        {
            SectionHeader("已录制的事件", panel, DockStyle.Top);

            // ── 新增：快捷测试直连栏 (免保存测试) ──
            var quickBar = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.FromArgb(240, 246, 252) };
            quickBar.Paint += (s, e) => e.Graphics.DrawLine(new Pen(C_BORDER), 0, quickBar.Height - 1, quickBar.Width, quickBar.Height - 1);
            panel.Controls.Add(quickBar);

            int qx = 8;
            quickBar.Controls.Add(Lbl("直连闪击:", quickBar, new Point(qx, 12))); qx += 64;
            
            var tbCtrl = new TextBox { Width = 110, Location = new Point(qx, 10), Font = F_BODY };
            quickBar.Controls.Add(tbCtrl); qx += 116;
            
            var cbAct = new ComboBox { Width = 70, Location = new Point(qx, 9), DropDownStyle = ComboBoxStyle.DropDownList };
            cbAct.Items.AddRange(new[] { "click", "input", "select" }); cbAct.SelectedIndex = 0;
            quickBar.Controls.Add(cbAct); qx += 76;
            
            var tbVal = new TextBox { Width = 90, Location = new Point(qx, 10), Font = F_BODY };
            quickBar.Controls.Add(tbVal); qx += 96;
            
            var btnSend = Btn("🚀 发射", quickBar, new Point(qx, 8), 66, Color.FromArgb(7, 140, 190), Color.White);
            btnSend.Click += async (s, e) => {
                string win = _dgvWindows.SelectedRows.Count > 0 ? _dgvWindows.SelectedRows[0].Cells[0].Value?.ToString() ?? "" : "";
                string ctl = tbCtrl.Text.Trim();
                if (string.IsNullOrEmpty(win) || string.IsNullOrEmpty(ctl)) {
                    MessageBox.Show("左侧在线窗口、及这里的控件名均不能为空！\n提示：直接填入服务端打出的或选中窗口。", "校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
                }
                string act = cbAct.Text;
                string val = tbVal.Text;
                string cmd = (act == "input" || act == "select") ? $"{act}|{win}|{ctl}|{val}" : $"{act}|{win}|{ctl}";
                
                var client = new AgentClient(_tbServer.Text.Trim(), timeoutMs: 15000);
                AppendTest($"[独立闪击] 正在发送: {cmd}", C_ACCENT);
                btnSend.Enabled = false;
                try {
                    string r = await Task.Run(() => client.Send(cmd));
                    AppendTest(r, r.StartsWith("OK") ? C_OK : C_ERR);
                } catch (Exception ex) {
                    AppendTest($"网络或底层崩溃: {ex.Message}", C_ERR);
                }
                btnSend.Enabled = true;
            };
            quickBar.Controls.Add(btnSend);

            // 按钮条
            var btnBar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = C_CARD };
            panel.Controls.Add(btnBar);
            int bx = 4;
            _btnEvtTest = Btn("▶ 测试", btnBar, new Point(bx, 4), 70, C_ACCENT, Color.White); bx += 76;
            _btnEvtTest.Click += OnEvtTest;
            _btnEvtSave = Btn("💾 另存", btnBar, new Point(bx, 4), 62, C_OK, Color.White); bx += 68;
            _btnEvtSave.Click += OnEvtResave;
            _btnEvtUp   = Btn("↑", btnBar, new Point(bx, 4), 30, C_BG, C_TEXT); bx += 36;
            _btnEvtUp.Click += (_, __) => MoveEvt(-1);
            _btnEvtDown = Btn("↓", btnBar, new Point(bx, 4), 30, C_BG, C_TEXT); bx += 36;
            _btnEvtDown.Click += (_, __) => MoveEvt(+1);
            _btnEvtDel  = Btn("🗑 删除", btnBar, new Point(bx, 4), 64, Color.FromArgb(220, 53, 69), Color.White);
            _btnEvtDel.Click += OnEvtDelete; bx += 70;
            
            var btnEvtManual = Btn("✍ 手动添加(已知名称)", btnBar, new Point(bx, 4), 130, Color.FromArgb(100, 116, 139), Color.White);
            btnEvtManual.Click += OnEvtAddManual;

            // 事件列表
            _lvEvents = new ListView {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = false,
                BackColor = C_CARD, BorderStyle = BorderStyle.None,
                Font = F_SMALL, HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _lvEvents.Columns.Add("#",    28);
            _lvEvents.Columns.Add("名称", 140);
            _lvEvents.Columns.Add("窗口", 160);
            _lvEvents.Columns.Add("动作",  -2);
            panel.Controls.Add(_lvEvents);
            _lvEvents.BringToFront(); // [!!! FIX MANGLED RIGHT-TOP PANEL (EVENTS LIST) OVERLAP !!!]
        }

        // ── 右下：流程步骤 + 执行控制 + 日志 ─────────────────
        private void BuildFlowPanel(SplitterPanel panel)
        {
            var innerSplit = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 5,
                BackColor = C_BG,
                Panel1MinSize = 160
            };
            panel.Controls.Add(innerSplit);
            this.Load += (_, __) => innerSplit.SplitterDistance = 220;

            // ── 左半：流程步骤列表 ────────────────────────────
            SectionHeader("流程步骤", innerSplit.Panel1, DockStyle.Top);

            var stepBtnBar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = C_CARD };
            innerSplit.Panel1.Controls.Add(stepBtnBar);
            int sx = 4;
            _btnStepAdd = Btn("+ 添加选中", stepBtnBar, new Point(sx, 4), 90, C_ACCENT, Color.White); sx += 96;
            _btnStepAdd.Click += OnStepAdd;
            _btnStepRm  = Btn("− 移除", stepBtnBar, new Point(sx, 4), 60, C_BG, C_TEXT); sx += 66;
            _btnStepRm.Click  += OnStepRemove;
            _btnStepUp  = Btn("↑", stepBtnBar, new Point(sx, 4), 28, C_BG, C_TEXT); sx += 34;
            _btnStepUp.Click  += (_, __) => MoveStep(-1);
            _btnStepDn  = Btn("↓", stepBtnBar, new Point(sx, 4), 28, C_BG, C_TEXT);
            _btnStepDn.Click  += (_, __) => MoveStep(+1);

            _lvSteps = new ListView {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = false,
                BackColor = C_CARD, BorderStyle = BorderStyle.None,
                Font = F_SMALL, HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _lvSteps.Columns.Add("步",   28);
            _lvSteps.Columns.Add("事件名", 130);
            _lvSteps.Columns.Add("窗口",   -2);
            innerSplit.Panel1.Controls.Add(_lvSteps);
            _lvSteps.BringToFront(); // [!!! FIX MANGLED RIGHT-BOTTOM-LEFT PANEL (FLOW STEPS) OVERLAP !!!]

            // ── 右半：执行控制 + 日志 ─────────────────────────
            SectionHeader("执行控制 & 日志", innerSplit.Panel2, DockStyle.Top);

            var execBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = C_CARD };
            innerSplit.Panel2.Controls.Add(execBar);

            execBar.Controls.Add(Lbl("从第", execBar, new Point(6, 10)));
            _nudStart = new NumericUpDown {
                Minimum = 1, Maximum = 999, Value = 1,
                Location = new Point(36, 7), Width = 52, Font = F_BODY
            };
            execBar.Controls.Add(_nudStart);
            execBar.Controls.Add(Lbl("步", execBar, new Point(92, 10)));

            execBar.Controls.Add(Lbl("超时", execBar, new Point(108, 10)));
            _nudTimeout = new NumericUpDown {
                Minimum = 5, Maximum = 300, Value = 60,
                Location = new Point(138, 7), Width = 56, Font = F_BODY
            };
            execBar.Controls.Add(_nudTimeout);
            execBar.Controls.Add(Lbl("秒", execBar, new Point(198, 10)));

            int ex = 216;
            _btnStart = Btn("▶ 启动", execBar, new Point(ex, 6), 72, Color.FromArgb(22, 101, 52), Color.White); ex += 78;
            _btnStart.Click += OnStart;
            _btnPause = Btn("⏸ 暂停", execBar, new Point(ex, 6), 72, Color.FromArgb(120, 53, 15), Color.White); ex += 78;
            _btnPause.Enabled = false;
            _btnPause.Click   += OnPause;
            _btnStop  = Btn("⏹ 停止", execBar, new Point(ex, 6), 72, Color.FromArgb(153, 27, 27), Color.White);
            _btnStop.Enabled = false;
            _btnStop.Click   += OnStop;

            _rtLog = new RichTextBox {
                Dock = DockStyle.Fill,
                BackColor = C_CARD, ForeColor = C_TEXT,
                Font = F_LOG, BorderStyle = BorderStyle.None,
                ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical
            };
            innerSplit.Panel2.Controls.Add(_rtLog);
            _rtLog.BringToFront(); // [!!! FIX MANGLED RIGHT-BOTTOM-RIGHT PANEL (LOG TEXTBOX) OVERLAP !!!]
        }

        // =====================================================
        //  工具栏事件
        // =====================================================
        private async void OnTestConnect(object? s, EventArgs e)
        {
            _btnTest.Enabled = false;
            _lblConn.ForeColor = C_SUB;
            _lblConn.Text = "连接中…";

            string server = _tbServer.Text.Trim();
            var client = new AgentClient(server, timeoutMs: 8000);
            string r = await Task.Run(() => client.Send("snapshot"));

            if (r.StartsWith("OK"))
            {
                string[] wins = r.Length > 3
                    ? r.Substring(3).Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries)
                    : Array.Empty<string>();

                _lblConn.ForeColor = C_OK;
                _lblConn.Text = $"✓ 连接正常（{wins.Length} 窗口）";

                _dgvWindows.Rows.Clear();
                foreach (var w in wins) _dgvWindows.Rows.Add(w);
            }
            else
            {
                _lblConn.ForeColor = C_ERR;
                _lblConn.Text = "✗ 连接失败";
            }
            _btnTest.Enabled = true;
        }

        private async void OnGetWindows(object? s, EventArgs e)
        {
            var btn = s as Button;
            if (btn != null) btn.Enabled = false;

            string server = _tbServer.Text.Trim();
            var client = new AgentClient(server, timeoutMs: 8000);
            
            try 
            {
                string r = await Task.Run(() => client.Send("snapshot"));
                if (r.StartsWith("OK"))
                {
                    string[] wins = r.Length > 3
                        ? r.Substring(3).Split(new[] { "|||" }, StringSplitOptions.RemoveEmptyEntries)
                        : Array.Empty<string>();

                    _dgvWindows.Rows.Clear();
                    foreach (var w in wins) _dgvWindows.Rows.Add(w);
                }
                else
                {
                    MessageBox.Show("获取窗口列表失败: " + r, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("网络或引擎错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (btn != null) btn.Enabled = true;
        }

        private async void OnWindowSelectionChanged(object? s, EventArgs e)
        {
            if (_dgvWindows.SelectedRows.Count == 0) return;
            string win = _dgvWindows.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(win)) return;

            _lblTreeSt.Text = "正在读取控件树…";
            _dgvTree.Rows.Clear();
            _treeData.Clear();

            string server = _tbServer.Text.Trim();
            var client = new AgentClient(server, timeoutMs: 30000);
            string result = await Task.Run(() => client.Send($"listcontrols|{win}|10"));

            if (!result.StartsWith("OK:"))
            {
                _lblTreeSt.Text = $"✗ {result}";
                return;
            }

            var controls = EventStore.ParseControlList(result);
            foreach (var c in controls)
            {
                _treeData.Add((c.Type, c.Name, c.Enabled));
                string defAction = GetActionLabel(c.Type); // mapped to "click", "input", etc.
                var row = _dgvTree.Rows[_dgvTree.Rows.Add()];
                row.Cells["colType"].Value    = c.Type;
                row.Cells["colName"].Value    = c.Name;
                row.Cells["colEnabled"].Value = c.Enabled ? "✓" : "✗";
                row.Cells["colAction"].Value  = defAction;
                row.Cells["colValue"].Value   = "";
                if (!c.Enabled) row.DefaultCellStyle.ForeColor = C_SUB;
            }

            _lblTreeSt.Text = $"共 {controls.Count} 个控件   窗口：{win}";
        }

        // =====================================================
        //  控件树内联操作 (测试 / 保存)
        // =====================================================
        private async void OnTreeActionClick(object? s, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var col = _dgvTree.Columns[e.ColumnIndex].Name;
            if (col != "colTest" && col != "colSave") return;

            if (_dgvWindows.SelectedRows.Count == 0) return;
            string win = _dgvWindows.SelectedRows[0].Cells[0].Value?.ToString() ?? "";
            if (string.IsNullOrEmpty(win)) return;

            // 为了内联编辑实时生效，需结束编辑状态
            _dgvTree.EndEdit();

            var row     = _dgvTree.Rows[e.RowIndex];
            string name = row.Cells["colName"].Value?.ToString() ?? "";
            string act  = row.Cells["colAction"].Value?.ToString() ?? "click";
            string val  = row.Cells["colValue"].Value?.ToString() ?? "";
            bool enabled= row.Cells["colEnabled"].Value?.ToString() == "✓";

            if (!enabled)
            {
                AppendTest($"控件 [{name}] 当前不可用", Color.FromArgb(255, 160, 50));
                return;
            }

            var action = new EventAction {
                Type = act, ControlName = name, Value = val, MatchText = val
            };
            
            string cmd = act switch {
                "input" or "select" => $"{act}|{win}|{name}|{val}",
                "gridnext"          => $"gridrows|{win}|{name}", // 测试时只需查行即可检验可读性
                _                   => $"{act}|{win}|{name}"
            };

            var client = new AgentClient(_tbServer.Text.Trim(), timeoutMs: 15000);

            if (col == "colTest")
            {
                AppendTest($">>> {cmd}", Color.FromArgb(100, 200, 255));
                try 
                {
                    string r = await Task.Run(() => client.Send(cmd));
                    AppendTest(r, r.StartsWith("OK") ? Color.FromArgb(120, 230, 120) : Color.FromArgb(255, 120, 100));
                }
                catch (Exception ex) { AppendTest($"通信失败: {ex.Message}", C_ERR); }
            }
            else if (col == "colSave")
            {
                string? stepName = PromptInput("保存事件", @"请为该步骤取一个名称（如 ""选待处理行""）：", action.Describe());
                if (string.IsNullOrWhiteSpace(stepName)) return;

                var evt = new AutoEvent {
                    Id = EventStore.NewId(), Name = stepName.Trim(), WindowName = win,
                    Snapshot = new ControlSnapshot(), Action = action
                };
                _store.Save(evt);
                AppendTest($"✅ 步骤「{evt.Name}」已保存至流程录制库！", Color.FromArgb(50, 205, 100));
                ReloadEvents();
            }
        }

        // =====================================================
        //  保存与手动添加事件
        // =====================================================
        private void OnEvtAddManual(object? s, EventArgs e)
        {
            using var dlg = new Form {
                Text = "✍ 手动添加步骤 (已知名称)", Size = new Size(380, 260),
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent
            };
            var lblW = new Label { Text = "目标窗口:", Location = new Point(12, 12), AutoSize = true };
            string curWin = _dgvWindows.SelectedRows.Count > 0 ? _dgvWindows.SelectedRows[0].Cells[0].Value?.ToString() ?? "" : "";
            var tbW  = new TextBox { Text = curWin, Location = new Point(80, 10), Width = 260 };
            
            var lblC = new Label { Text = "控件名称:", Location = new Point(12, 42), AutoSize = true };
            var tbC  = new TextBox { Text = "生成按钮", Location = new Point(80, 40), Width = 260 };
            
            var lblA = new Label { Text = "动作类型:", Location = new Point(12, 72), AutoSize = true };
            var cbA  = new ComboBox { Location = new Point(80, 70), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            cbA.Items.AddRange(new[] { "click", "input", "select", "gridnext", "popupclick", "sleep" });
            cbA.SelectedIndex = 0;
            
            var lblV = new Label { Text = "输入值:", Location = new Point(12, 102), AutoSize = true };
            var tbV  = new TextBox { Text = "", Location = new Point(80, 100), Width = 260 };
            
            var lblN = new Label { Text = "步骤名:", Location = new Point(12, 132), AutoSize = true };
            var tbN  = new TextBox { Text = "手动点击", Location = new Point(80, 130), Width = 260 };

            var btnOk = new Button { Text = "保存", DialogResult = DialogResult.OK, Location = new Point(150, 175), Width = 90 };
            var btnCn = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(250, 175), Width = 90 };

            dlg.Controls.AddRange(new Control[] { lblW, tbW, lblC, tbC, lblA, cbA, lblV, tbV, lblN, tbN, btnOk, btnCn });
            dlg.AcceptButton = btnOk; dlg.CancelButton = btnCn;

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                var evt = new AutoEvent
                {
                    Id         = EventStore.NewId(),
                    Name       = tbN.Text.Trim(),
                    WindowName = tbW.Text.Trim(),
                    Snapshot   = new ControlSnapshot(), // 空快照，因为 FlowRunner 现在只检查 exists，不验证整个树了
                    Action     = new EventAction
                    {
                        Type        = cbA.Text,
                        ControlName = tbC.Text.Trim(),
                        Value       = tbV.Text.Trim(),
                        MatchText   = tbV.Text.Trim() // 如果是 gridnext，把输入值作为匹配文本
                    }
                };
                _store.Save(evt);
                ReloadEvents();
                AppendLog($"💾 手动事件「{evt.Name}」已保存。", C_OK);
            }
        }

        // =====================================================
        //  事件列表操作
        // =====================================================
        private void ReloadEvents()
        {
            _allEvents = _store.LoadAll();

            // 读取已保存的流程顺序
            var savedFlow = _store.LoadFlow();
            if (savedFlow != null)
                _flowSteps = savedFlow;
            else
                _flowSteps = _allEvents.Select(e => e.Id).ToList();

            RefreshEventsListView();
            RefreshFlowListView();
        }

        private void RefreshEventsListView()
        {
            _lvEvents.Items.Clear();
            for (int i = 0; i < _allEvents.Count; i++)
            {
                var ev = _allEvents[i];
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(ev.Name);
                item.SubItems.Add(ev.WindowName);
                item.SubItems.Add(ev.Action.Describe());
                item.Tag = ev.Id;
                _lvEvents.Items.Add(item);
            }
        }

        private void RefreshFlowListView()
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
        }

        private void MoveEvt(int dir)
        {
            if (_lvEvents.SelectedIndices.Count == 0) return;
            int idx  = _lvEvents.SelectedIndices[0];
            int nIdx = idx + dir;
            if (nIdx < 0 || nIdx >= _allEvents.Count) return;

            var tmp = _allEvents[idx]; _allEvents[idx] = _allEvents[nIdx]; _allEvents[nIdx] = tmp;
            RefreshEventsListView();
            _lvEvents.Items[nIdx].Selected = true;
        }

        private async void OnEvtTest(object? s, EventArgs e)
        {
            if (_lvEvents.SelectedItems.Count == 0) return;
            string id = _lvEvents.SelectedItems[0].Tag?.ToString() ?? "";
            var ev = _allEvents.FirstOrDefault(x => x.Id == id);
            if (ev == null) return;

            var client = new AgentClient(_tbServer.Text.Trim(), timeoutMs: 15000);
            string cmd = BuildCmd(ev);
            AppendLog($"[测试] {ev.Name}: {cmd}", C_ACCENT);
            string r = await Task.Run(() => client.Send(cmd));
            AppendLog(r, r.StartsWith("OK") ? C_OK : C_ERR);
        }

        private void OnEvtResave(object? s, EventArgs e)
        {
            // 仅重命名事件
            if (_lvEvents.SelectedItems.Count == 0) return;
            string id = _lvEvents.SelectedItems[0].Tag?.ToString() ?? "";
            var ev = _allEvents.FirstOrDefault(x => x.Id == id);
            if (ev == null) return;

            string? name = PromptInput("重命名事件", "新名称：", ev.Name);
            if (!string.IsNullOrWhiteSpace(name))
            {
                ev.Name = name.Trim();
                _store.Save(ev);
                ReloadEvents();
            }
        }

        private void OnEvtDelete(object? s, EventArgs e)
        {
            if (_lvEvents.SelectedItems.Count == 0) return;
            string id = _lvEvents.SelectedItems[0].Tag?.ToString() ?? "";
            var ev = _allEvents.FirstOrDefault(x => x.Id == id);
            if (ev == null) return;

            if (MessageBox.Show($"确定删除事件「{ev.Name}」？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            _store.Delete(id);
            _flowSteps.Remove(id);
            _store.SaveFlow(_flowSteps);
            ReloadEvents();
        }

        // =====================================================
        //  流程步骤操作
        // =====================================================
        private void OnStepAdd(object? s, EventArgs e)
        {
            if (_lvEvents.SelectedItems.Count == 0) return;
            string id = _lvEvents.SelectedItems[0].Tag?.ToString() ?? "";
            _flowSteps.Add(id);
            _store.SaveFlow(_flowSteps);
            RefreshFlowListView();
            AppendLog($"已将「{_allEvents.FirstOrDefault(x => x.Id == id)?.Name}」添加到流程末尾", C_OK);
        }

        private void OnStepRemove(object? s, EventArgs e)
        {
            if (_lvSteps.SelectedIndices.Count == 0) return;
            int idx = _lvSteps.SelectedIndices[0];
            _flowSteps.RemoveAt(idx);
            _store.SaveFlow(_flowSteps);
            RefreshFlowListView();
        }

        private void MoveStep(int dir)
        {
            if (_lvSteps.SelectedIndices.Count == 0) return;
            int idx  = _lvSteps.SelectedIndices[0];
            int nIdx = idx + dir;
            if (nIdx < 0 || nIdx >= _flowSteps.Count) return;

            var tmp = _flowSteps[idx]; _flowSteps[idx] = _flowSteps[nIdx]; _flowSteps[nIdx] = tmp;
            _store.SaveFlow(_flowSteps);
            RefreshFlowListView();
            _lvSteps.Items[nIdx].Selected = true;
        }

        // =====================================================
        //  流程执行
        // =====================================================
        private void OnStart(object? s, EventArgs e)
        {
            if (_flowSteps.Count == 0)
            {
                MessageBox.Show("流程中没有步骤，请先添加事件到流程。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int startStep   = (int)_nudStart.Value - 1;  // 转 0-based
            int timeoutSec  = (int)_nudTimeout.Value;

            _rtLog.Clear();
            AppendLog($"▶ 流程启动，从第 {startStep + 1} 步，超时 {timeoutSec}s", C_OK);

            var client = new AgentClient(_tbServer.Text.Trim());
            _runner = new FlowRunner(client, _allEvents, _flowSteps,
                                     startStep, timeoutSec, OnRunnerEvent);
            _runner.Start();
            SetRunning(true);
        }

        private void OnPause(object? s, EventArgs e)
        {
            if (_runner == null) return;
            if (_runner.IsPaused) { _runner.Resume(); _btnPause.Text = "⏸ 暂停"; }
            else                  { _runner.Pause();  _btnPause.Text = "▶ 继续"; }
        }

        private void OnStop(object? s, EventArgs e)
        {
            _runner?.Stop();
            SetRunning(false);
        }

        private void OnRunnerEvent(EngineEvent evt)
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
        }

        private void SetRunning(bool running)
        {
            _btnStart.Enabled = !running;
            _btnPause.Enabled = running;
            _btnStop.Enabled  = running;
            _btnRefreshWins.Enabled  = !running;
            _btnEvtDel.Enabled= !running;
            if (!running) _btnPause.Text = "⏸ 暂停";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _runner?.Stop();
            base.OnFormClosing(e);
        }

        // =====================================================
        //  日志 & 测试输出
        // =====================================================
        private void AppendLog(string text, LogLevel level = LogLevel.Info)
        {
            var color = level switch
            {
                LogLevel.Ok    => C_OK,
                LogLevel.Wait  => C_WAIT,
                LogLevel.Popup => C_POPUP,
                LogLevel.Warn  => C_WARN,
                LogLevel.Error => C_ERR,
                LogLevel.Debug => C_DBG,
                _              => C_TEXT
            };
            AppendLog(text, color);
        }

        private void AppendLog(string text, Color color)
        {
            if (_rtLog.InvokeRequired)
            { _rtLog.Invoke((Action)(() => AppendLog(text, color))); return; }
            _rtLog.SelectionStart  = _rtLog.TextLength;
            _rtLog.SelectionLength = 0;
            _rtLog.SelectionColor  = color;
            _rtLog.AppendText(text + "\n");
            _rtLog.SelectionColor  = C_TEXT;
            _rtLog.ScrollToCaret();
        }

        private void AppendTest(string text, Color color)
        {
            if (_rtTestOut.InvokeRequired)
            { _rtTestOut.Invoke((Action)(() => AppendTest(text, color))); return; }
            _rtTestOut.SelectionStart  = _rtTestOut.TextLength;
            _rtTestOut.SelectionLength = 0;
            _rtTestOut.SelectionColor  = color;
            _rtTestOut.AppendText(text + "\r\n");
            _rtTestOut.SelectionColor  = _rtTestOut.ForeColor;
            _rtTestOut.ScrollToCaret();
        }

        // =====================================================
        //  辅助
        // =====================================================
        private static string GetActionLabel(string type)
        {
            if (IsGridType(type))        return "选行▼";
            if (type == "Edit" ||
                type.Contains("Text") ||
                type == "Document")      return "输入";
            if (type == "ComboBox" ||
                type == "List")          return "选择▼";
            return "点击";
        }

        private static bool IsGridType(string type)
            => type == "DataGrid" || type == "List" || type == "Table"
            || type == "DataItem" || type == "Tree";

        private string BuildCmd(AutoEvent ev)
        {
            var a = ev.Action;
            return a.Type switch
            {
                "click"      => $"click|{ev.WindowName}|{a.ControlName}",
                "input"      => $"input|{ev.WindowName}|{a.ControlName}|{a.Value}",
                "select"     => $"select|{ev.WindowName}|{a.ControlName}|{a.Value}",
                "gridnext"   => $"gridrows|{ev.WindowName}|{a.ControlName}",
                "popupclick" => $"click|{ev.WindowName}|{a.ControlName}",
                _            => $"click|{ev.WindowName}|{a.ControlName}"
            };
        }

        private void OnTreeCellFormat(object? s, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _treeData.Count) return;
            bool enabled = _treeData[e.RowIndex].Enabled;
            if (!enabled)
                e.CellStyle.ForeColor = Color.FromArgb(160, 170, 185);
        }

        // ── 工厂方法 ─────────────────────────────────────────
        private static Label Lbl(string text, Control parent, Point loc)
        {
            var l = new Label {
                Text = text, Location = loc, AutoSize = true,
                Font = F_SMALL, ForeColor = C_SUB, BackColor = Color.Transparent
            };
            parent.Controls.Add(l);
            return l;
        }

        private static Button Btn(string text, Control parent, Point loc,
                                   int w, Color bg, Color fg)
        {
            var b = new Button {
                Text = text, Location = loc, Width = w, Height = 24,
                FlatStyle = FlatStyle.Flat, BackColor = bg, ForeColor = fg, Font = F_SMALL
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
            parent.Controls.Add(b);
            return b;
        }

        private static Panel SectionHeader(string title, Control parent, DockStyle dock)
        {
            var hdr = new Panel { Dock = dock, Height = 30, BackColor = Color.FromArgb(37, 99, 185) };
            var lbl = new Label {
                Text = title, Dock = DockStyle.Fill,
                Font = F_BOLD, ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent,
                Padding = new Padding(8, 0, 0, 0)
            };
            hdr.Controls.Add(lbl);
            parent.Controls.Add(hdr);
            return hdr;
        }

        private static string? PromptInput(string title, string prompt, string defaultVal)
        {
            using var dlg = new Form {
                Text = title, Size = new Size(380, 150),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false
            };
            var lbl = new Label { Text = prompt, Location = new Point(12, 12), AutoSize = true };
            var tb  = new TextBox { Text = defaultVal, Location = new Point(12, 32), Width = 338, Font = F_BODY };
            var ok  = new Button { Text = "确定", DialogResult = DialogResult.OK,
                                   Location = new Point(180, 68), Width = 80 };
            var cn  = new Button { Text = "取消", DialogResult = DialogResult.Cancel,
                                   Location = new Point(270, 68), Width = 80 };
            dlg.Controls.AddRange(new Control[] { lbl, tb, ok, cn });
            dlg.AcceptButton = ok; dlg.CancelButton = cn;
            return dlg.ShowDialog() == DialogResult.OK ? tb.Text : null;
        }
    }
}
