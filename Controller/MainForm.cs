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
        // ── 设计令牌 (SaaS 级基准) ──────────────────────────
        static readonly Font F_BODY  = new Font("Segoe UI", 9.5f);
        static readonly Font F_SMALL = new Font("Segoe UI", 8.5f);
        static readonly Font F_BOLD  = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        static readonly Font F_LOG   = new Font("Consolas",  9f);
        static readonly Font F_TITLE = new Font("Segoe UI", 12f, FontStyle.Bold);
        static readonly Font F_MONO  = new Font("Consolas",  9f);

        static readonly Color C_BG      = Color.FromArgb(243, 244, 246);   // 纯净背景浅灰蓝 (Slate-100)
        static readonly Color C_CARD    = Color.White;                    // 卡片背景
        static readonly Color C_BORDER  = Color.FromArgb(226, 232, 240);   // 线条极亮灰 (Slate-200)
        static readonly Color C_SIDEBAR = Color.FromArgb(15, 23, 42);      // 侧边栏极限深邃 (Slate-900)
        static readonly Color C_HDR_BG  = Color.FromArgb(30, 41, 59);      // 顶栏深灰 (Slate-800)
        
        static readonly Color C_ACCENT  = Color.FromArgb(14, 165, 233);    // 醒目清透的主蓝 (Sky-500)
        static readonly Color C_TEXT    = Color.FromArgb(30, 41, 59);      // 正文灰黑 (Slate-800)
        static readonly Color C_SUB     = Color.FromArgb(100, 116, 139);   // 辅助文本 (Slate-500)
        static readonly Color C_OK      = Color.FromArgb(16, 185, 129);    // 成功绿 (Emerald-500)
        static readonly Color C_WARN    = Color.FromArgb(245, 158, 11);    // 警告黄 (Amber-500)
        static readonly Color C_ERR     = Color.FromArgb(239, 68, 68);     // 错误红 (Red-500)
        static readonly Color C_POPUP   = Color.FromArgb(139, 92, 246);    // 弹窗紫 (Violet-500)
        static readonly Color C_WAIT    = Color.FromArgb(245, 158, 11);    // 等待黄
        static readonly Color C_DBG     = Color.FromArgb(148, 163, 184);   // 调试浅灰
        ///

        ///

        ///

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
        TabControl     _tcTest     = null!;
        DataGridView   _dgvTestOut = null!;

        // 右上：事件列表
        ListView       _lvEvents   = null!;
        Button         _btnEvtTest = null!;
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
        List<ControlInfo> _treeData = new();

        // =====================================================
        // =====================================================
        //  UI 核心：无边框与悬浮阴影 (CS_DROPSHADOW)
        // =====================================================
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (FormBorderStyle == FormBorderStyle.None)
                {
                    cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                }
                return cp;
            }
        }

        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        // =====================================================
        //  界面构建
        // =====================================================
        public MainForm()
        {
            Text          = "GnwayAgent · 自动化车间";
            Size          = new Size(1360, 800);
            MinimumSize   = new Size(1024, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor     = C_BG;
            Font          = F_BODY;
            FormBorderStyle = FormBorderStyle.None; // 彻底无系统边框

            // 1. 顶部自定义高雅栏 ────────────
            var header = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = C_HDR_BG };
            header.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) {
                    ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                }
            };
            Controls.Add(header);

            // LOGO 区
            var lblLogo = new Label {
                Text = "⚡ GnwayAgent", ForeColor = Color.White, Font = F_TITLE, AutoSize = true, Location = new Point(16, 12)
            };
            lblLogo.MouseDown += (s, e) => {
                if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); }
            };
            header.Controls.Add(lblLogo);

            // 右侧系统按钮（关闭、最大化、最小化）
            int hx = this.Width - 45;
            var btnClose = new Label { Text = "✕", ForeColor = Color.FromArgb(200, 200, 200), Font = new Font("Segoe UI", 12), Anchor = AnchorStyles.Right | AnchorStyles.Top, AutoSize = true, Location = new Point(hx + 15, 12), Cursor = Cursors.Hand };
            btnClose.Click += (_, __) => Close();
            btnClose.MouseEnter += (_, __) => btnClose.ForeColor = C_ERR;
            btnClose.MouseLeave += (_, __) => btnClose.ForeColor = Color.FromArgb(200, 200, 200);
            header.Controls.Add(btnClose); hx -= 40;

            var btnMax = new Label { Text = "🗖", ForeColor = Color.FromArgb(200, 200, 200), Font = new Font("Segoe UI", 14), Anchor = AnchorStyles.Right | AnchorStyles.Top, AutoSize = true, Location = new Point(hx + 10, 10), Cursor = Cursors.Hand };
            btnMax.Click += (_, __) => WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            btnMax.MouseEnter += (_, __) => btnMax.ForeColor = Color.White;
            btnMax.MouseLeave += (_, __) => btnMax.ForeColor = Color.FromArgb(200, 200, 200);
            header.Controls.Add(btnMax); hx -= 40;

            var btnMin = new Label { Text = "─", ForeColor = Color.FromArgb(200, 200, 200), Font = new Font("Segoe UI", 12, FontStyle.Bold), Anchor = AnchorStyles.Right | AnchorStyles.Top, AutoSize = true, Location = new Point(hx + 10, 10), Cursor = Cursors.Hand };
            btnMin.Click += (_, __) => WindowState = FormWindowState.Minimized;
            btnMin.MouseEnter += (_, __) => btnMin.ForeColor = Color.White;
            btnMin.MouseLeave += (_, __) => btnMin.ForeColor = Color.FromArgb(200, 200, 200);
            header.Controls.Add(btnMin);

            // 2. 左侧深色侧边栏 SaaS Navigation ────────────
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 64, BackColor = C_SIDEBAR };
            Controls.Add(sidebar);
            sidebar.BringToFront(); // 压在 Header 之下
            
            var navItem = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.FromArgb(51, 65, 85) }; // 亮起的高亮色
            var navIcon = new Label { Text = "⛭", ForeColor = Color.White, Font = new Font("Segoe UI", 18), AutoSize = true, Location = new Point(18, 8) };
            var navText = new Label { Text = "主控台", ForeColor = Color.White, Font = new Font("Segoe UI", 8), AutoSize = true, Location = new Point(13, 38) };
            navItem.Controls.Add(navIcon); navItem.Controls.Add(navText);
            
            var navMark = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = C_ACCENT };
            navItem.Controls.Add(navMark);
            sidebar.Controls.Add(navItem);

            // 3. 通用内容底板 Main Content ────────────
            var mainContent = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            Controls.Add(mainContent);
            mainContent.BringToFront();

            // 工具栏 (Toolbar Card)
            var toolbarCard = CreateCard(mainContent, DockStyle.Top, new Padding(0, 0, 0, 12), 60);
            toolbarCard.Padding = new Padding(16, 0, 16, 0);
            int tx = 16;
            toolbarCard.Controls.Add(Lbl("探针 IP 地址", toolbarCard, new Point(tx, 8), C_SUB, F_SMALL));
            _tbServer = new TextBox {
                Text = ".", Width = 150, Location = new Point(tx, 26),
                Font = F_BODY, BorderStyle = BorderStyle.FixedSingle
            };
            toolbarCard.Controls.Add(_tbServer); tx += 162;

            _btnTest = Btn("连接并探测", toolbarCard, new Point(tx, 24), 90, C_ACCENT, Color.White);
            _btnTest.Click += OnTestConnect; tx += 100;

            _lblConn = new Label {
                Location = new Point(tx, 29), AutoSize = true,
                ForeColor = C_SUB, Font = F_SMALL, BackColor = Color.Transparent
            };
            toolbarCard.Controls.Add(_lblConn); tx += 170;

            _btnRefreshWins = Btn("🔄 重新抓取全局窗口", toolbarCard, new Point(tx, 24), 160, Color.FromArgb(241, 245, 249), C_TEXT);
            _btnRefreshWins.Click += OnGetWindows;

            // 4. 数据区 SplitContainer ────────────
            var mainSplit = new SplitContainer {
                Dock = DockStyle.Fill, SplitterWidth = 10, BackColor = C_BG, Orientation = Orientation.Vertical, Panel1MinSize = 300
            };
            mainContent.Controls.Add(mainSplit);
            mainSplit.BringToFront();
            this.Load += (_, __) => {
                mainSplit.SplitterDistance = (int)(mainContent.Width * 0.45);
                ReloadEvents();
            };

            BuildLeftPanel(mainSplit.Panel1);
            BuildRightPanel(mainSplit.Panel2);
            
            _store = new EventStore(AppDomain.CurrentDomain.BaseDirectory);
        }

        // =====================================================
        //  左侧区面板逻辑 (Master-Detail Cards)
        // =====================================================
        private void BuildLeftPanel(SplitterPanel panel)
        {
            var leftSplit = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 10, BackColor = C_BG, Panel1MinSize = 200
            };
            panel.Controls.Add(leftSplit);
            this.Load += (_, __) => leftSplit.SplitterDistance = 220;

            // --- 在线窗口卡片 ---
            var winCard = CreateCard(leftSplit.Panel1, DockStyle.Fill, new Padding(0));
            SectionHeader("在线窗口容器 (Master)", winCard, DockStyle.Top);
            _dgvWindows = CustomDgv();
            _dgvWindows.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "实时目标窗口名称", Name = "colWinName", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            _dgvWindows.SelectionChanged += OnWindowSelectionChanged;
            winCard.Controls.Add(_dgvWindows);
            _dgvWindows.BringToFront();

            // --- 控件树视窗卡片 ---
            var treeCard = CreateCard(leftSplit.Panel2, DockStyle.Fill, new Padding(0));
            SectionHeader("深度控件树与单步测试 (Detail)", treeCard, DockStyle.Top);
            
            _dgvTree = CustomDgv();
            _dgvTree.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _dgvTree.EditMode = DataGridViewEditMode.EditOnEnter;
            _dgvTree.RowTemplate.Height = 36;
            
            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "控件分类", Name = "colType", Width = 110, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "系统 MagicId 标识", Name = "colMagicId", Width = 180, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "显示文本 (支持缩进)", Name = "colText", Width = 180, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "矩形包围盒", Name = "colRect", Width = 140, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "启用", Name = "colEnabled", Width = 50, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            
            var colAct = new DataGridViewComboBoxColumn { HeaderText = "派发动作", Name = "colAction", Width = 100, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox, SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Flat };
            colAct.Items.AddRange("click", "input", "sendkeys", "select", "gridnext", "popupclick", "sleep");
            colAct.DefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            _dgvTree.Columns.Add(colAct);

            var colVal = new DataGridViewTextBoxColumn { HeaderText = "动作传值参数", Name = "colValue", Width = 140, SortMode = DataGridViewColumnSortMode.NotSortable };
            colVal.DefaultCellStyle.BackColor = Color.FromArgb(254, 252, 232); // 非常淡的黄色提示输入
            _dgvTree.Columns.Add(colVal);

            _dgvTree.Columns.Add(new DataGridViewButtonColumn { HeaderText = "即时测试", Name = "colTest", Text = "▶ 发起", UseColumnTextForButtonValue = true, Width = 65, SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Flat });
            _dgvTree.Columns.Add(new DataGridViewButtonColumn { HeaderText = "持久化", Name = "colSave", Text = "💾 录制", UseColumnTextForButtonValue = true, Width = 65, SortMode = DataGridViewColumnSortMode.NotSortable, FlatStyle = FlatStyle.Flat });

            _dgvTree.CellContentClick += OnTreeActionClick;
            _dgvTree.CellFormatting += OnTreeCellFormat;
            _dgvTree.CellPainting += OnTreeCellPainting; // 美化按钮
            treeCard.Controls.Add(_dgvTree);
            _dgvTree.BringToFront();

            // 底部：响应输出与预览
            var bottomCard = new Panel { Dock = DockStyle.Bottom, Height = 140, BackColor = C_CARD };
            var bottomBorder = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = C_BORDER };
            bottomCard.Controls.Add(bottomBorder);
            treeCard.Controls.Add(bottomCard);

            _lblTreeSt = new Label { Text = "控件树空——请先点击上方在线窗口", Location = new Point(8, 8), AutoSize = true, ForeColor = C_SUB, Font = F_SMALL };
            bottomCard.Controls.Add(_lblTreeSt);

            _tcTest = new TabControl { Location = new Point(8, 28), Size = new Size(0, 100) };
            bottomCard.Resize += (_, __) => _tcTest.Width = bottomCard.Width - 100;
            bottomCard.Controls.Add(_tcTest);

            var tpTxt = new TabPage("通道返回日志") { BackColor = C_CARD }; _tcTest.TabPages.Add(tpTxt);
            var tpGrid = new TabPage("结构化网格数据") { BackColor = C_CARD }; _tcTest.TabPages.Add(tpGrid);

            _rtTestOut = new RichTextBox {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(56, 189, 248), Font = F_MONO, BorderStyle = BorderStyle.None,
                ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical
            };
            tpTxt.Controls.Add(_rtTestOut);

            _dgvTestOut = CustomDgv();
            _dgvTestOut.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells;
            tpGrid.Controls.Add(_dgvTestOut);

            var btnClearTest = Btn("🗑 清空", bottomCard, new Point(0, 30), 74, Color.White, C_SUB);
            btnClearTest.FlatAppearance.BorderColor = C_BORDER; btnClearTest.FlatAppearance.BorderSize = 1;
            bottomCard.Resize += (_, __) => btnClearTest.Left = bottomCard.Width - 84;
            btnClearTest.Click += (_, __) => { _rtTestOut.Clear(); _dgvTestOut.Columns.Clear(); };
            bottomCard.Controls.Add(btnClearTest);
        }

        // =====================================================
        //  右侧区面板逻辑 (Script & Runner Cards)
        // =====================================================
        private void BuildRightPanel(SplitterPanel panel)
        {
            var rightSplit = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 10, BackColor = C_BG, Panel1MinSize = 200
            };
            panel.Controls.Add(rightSplit);
            this.Load += (_, __) => rightSplit.SplitterDistance = 280;

            BuildEventsPanel(rightSplit.Panel1);
            BuildFlowPanel(rightSplit.Panel2);
        }

        private void BuildEventsPanel(SplitterPanel panel)
        {
            var evtCard = CreateCard(panel, DockStyle.Fill, new Padding(0));
            SectionHeader("剧本行为大纲 (Recorded Events)", evtCard, DockStyle.Top);

            var quickBar = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(248, 250, 252) };
            var qbB = new Panel { Dock=DockStyle.Bottom, Height=1, BackColor=C_BORDER }; quickBar.Controls.Add(qbB);
            evtCard.Controls.Add(quickBar);

            int qx = 12;
            quickBar.Controls.Add(Lbl("指令终端:", quickBar, new Point(qx, 15), C_TEXT, F_BOLD)); qx += 70;
            var tbCtrl = new TextBox { Width = 110, Location = new Point(qx, 12), Font = F_BODY, BorderStyle = BorderStyle.FixedSingle }; quickBar.Controls.Add(tbCtrl); qx += 116;
            var cbAct = new ComboBox { Width = 80, Location = new Point(qx, 11), DropDownStyle = ComboBoxStyle.DropDownList };
            cbAct.Items.AddRange(new[] { "click", "input", "sendkeys", "select" }); cbAct.SelectedIndex = 0; quickBar.Controls.Add(cbAct); qx += 86;
            var tbVal = new TextBox { Width = 90, Location = new Point(qx, 12), Font = F_BODY, BorderStyle = BorderStyle.FixedSingle }; quickBar.Controls.Add(tbVal); qx += 96;
            
            var btnSend = Btn("🚀 直连闪击", quickBar, new Point(qx, 10), 90, C_POPUP, Color.White);
            btnSend.Click += async (s, e) => {
                string win = _dgvWindows.SelectedRows.Count > 0 ? _dgvWindows.SelectedRows[0].Cells[0].Value?.ToString() ?? "" : "";
                string ctl = tbCtrl.Text.Trim();
                if (string.IsNullOrEmpty(win) || string.IsNullOrEmpty(ctl)) { MessageBox.Show("目标窗口及控件均不能为空", "提示"); return; }
                string act = cbAct.Text; string val = tbVal.Text;
                string cmd = (act == "input" || act == "select" || act == "click") ? (string.IsNullOrWhiteSpace(val) ? $"{act}|{win}|{ctl}" : $"{act}|{win}|{ctl}|{val}") : $"{act}|{win}|{ctl}";
                var client = new AgentClient(_tbServer.Text.Trim(), timeoutMs: 15000);
                AppendTest($"[直连闪击] 发送: {cmd}", C_ACCENT);
                btnSend.Enabled = false;
                try { string r = await Task.Run(() => client.Send(cmd)); AppendTest(r, r.StartsWith("OK") ? C_OK : C_ERR); }
                catch (Exception ex) { AppendTest($"闪击失败: {ex.Message}", C_ERR); }
                btnSend.Enabled = true;
            };
            quickBar.Controls.Add(btnSend);

            var btnBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = C_CARD };
            evtCard.Controls.Add(btnBar);
            int bx = 12;
            _btnEvtTest = Btn("▶ 灰度试演", btnBar, new Point(bx, 6), 90, C_ACCENT, Color.White); bx += 96;
            _btnEvtTest.Click += OnEvtTest;
            var btnEvtEdit = Btn("✎ 编辑微调", btnBar, new Point(bx, 6), 90, Color.FromArgb(71, 85, 105), Color.White); bx += 96;
            btnEvtEdit.Click += OnEvtEdit;
            
            _btnEvtUp = Btn("↑", btnBar, new Point(bx, 6), 34, Color.White, C_TEXT); bx += 38;
            _btnEvtUp.FlatAppearance.BorderColor = C_BORDER; _btnEvtUp.FlatAppearance.BorderSize = 1; _btnEvtUp.Click += (_, __) => MoveEvt(-1);
            
            _btnEvtDown = Btn("↓", btnBar, new Point(bx, 6), 34, Color.White, C_TEXT); bx += 48;
            _btnEvtDown.FlatAppearance.BorderColor = C_BORDER; _btnEvtDown.FlatAppearance.BorderSize = 1; _btnEvtDown.Click += (_, __) => MoveEvt(+1);
            
            _btnEvtDel = Btn("🗑 移除节点", btnBar, new Point(bx, 6), 90, C_ERR, Color.White); bx += 96;
            _btnEvtDel.Click += OnEvtDelete;
            
            var btnEvtManual = Btn("+ 手动构造指令", btnBar, new Point(bx, 6), 116, Color.White, C_TEXT);
            btnEvtManual.FlatAppearance.BorderColor = C_BORDER; btnEvtManual.FlatAppearance.BorderSize = 1; btnEvtManual.Click += OnEvtAddManual;

            _lvEvents = CustomLv();
            _lvEvents.Columns.Add("#",    30);
            _lvEvents.Columns.Add("语义名称", 150);
            _lvEvents.Columns.Add("目标窗口作用域", 220);
            _lvEvents.Columns.Add("核心执行动作", 260);
            _lvEvents.KeyDown += OnListViewCopy;
            evtCard.Controls.Add(_lvEvents);
            _lvEvents.BringToFront();
        }

        private void BuildFlowPanel(SplitterPanel panel)
        {
            var innerSplit = new SplitContainer {
                Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 10, BackColor = C_BG, Panel1MinSize = 250
            };
            panel.Controls.Add(innerSplit);
            this.Load += (_, __) => innerSplit.SplitterDistance = (int)(innerSplit.Width * 0.55);

            // --- 左边：流程清单 ---
            var stepCard = CreateCard(innerSplit.Panel1, DockStyle.Fill, new Padding(0));
            SectionHeader("连续自动化管线 (Continuous Flow)", stepCard, DockStyle.Top);

            var stepBtnBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = C_CARD };
            stepCard.Controls.Add(stepBtnBar);
            int sx = 8;
            _btnStepAdd = Btn("+ 装配此节点", stepBtnBar, new Point(sx, 6), 94, C_ACCENT, Color.White); sx += 100;
            _btnStepAdd.Click += OnStepAdd;
            _btnStepRm = Btn("− 卸载", stepBtnBar, new Point(sx, 6), 66, Color.White, C_TEXT);
            _btnStepRm.FlatAppearance.BorderColor = C_BORDER; _btnStepRm.FlatAppearance.BorderSize = 1; sx += 72;
            _btnStepRm.Click += OnStepRemove;
            
            _btnStepUp = Btn("↑", stepBtnBar, new Point(sx, 6), 34, Color.White, C_TEXT); _btnStepUp.FlatAppearance.BorderColor = C_BORDER; _btnStepUp.FlatAppearance.BorderSize = 1; sx += 38; _btnStepUp.Click += (_, __) => MoveStep(-1);
            _btnStepDn = Btn("↓", stepBtnBar, new Point(sx, 6), 34, Color.White, C_TEXT); _btnStepDn.FlatAppearance.BorderColor = C_BORDER; _btnStepDn.FlatAppearance.BorderSize = 1; sx += 44; _btnStepDn.Click += (_, __) => MoveStep(+1);
            
            var btnImport = Btn("📥 导入", stepBtnBar, new Point(sx, 6), 66, Color.White, C_TEXT); btnImport.FlatAppearance.BorderColor = C_BORDER; btnImport.FlatAppearance.BorderSize = 1; sx += 72; btnImport.Click += OnFlowImport;
            var btnExport = Btn("📤 导出", stepBtnBar, new Point(sx, 6), 66, Color.White, C_TEXT); btnExport.FlatAppearance.BorderColor = C_BORDER; btnExport.FlatAppearance.BorderSize = 1; btnExport.Click += OnFlowExport;

            _lvSteps = CustomLv();
            _lvSteps.Columns.Add("序",   30);
            _lvSteps.Columns.Add("执行动作语义", 150);
            _lvSteps.Columns.Add("目标窗口作用域", 220);
            _lvSteps.KeyDown += OnListViewCopy;
            stepCard.Controls.Add(_lvSteps);
            _lvSteps.BringToFront();

            // --- 右边：监视器 ---
            var execCard = CreateCard(innerSplit.Panel2, DockStyle.Fill, new Padding(0));
            SectionHeader("引擎监控控制台 (Engine Terminal)", execCard, DockStyle.Top);

            var execBar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.FromArgb(248, 250, 252) };
            var ebB = new Panel { Dock=DockStyle.Bottom, Height=1, BackColor=C_BORDER }; execBar.Controls.Add(ebB);
            execCard.Controls.Add(execBar);

            execBar.Controls.Add(Lbl("初站:", execBar, new Point(6, 15), C_TEXT, F_BOLD));
            _nudStart = new NumericUpDown { Minimum = 1, Maximum = 999, Value = 1, Location = new Point(46, 12), Width = 56, Font = F_TITLE, BackColor = C_CARD, BorderStyle = BorderStyle.FixedSingle };
            execBar.Controls.Add(_nudStart);
            execBar.Controls.Add(Lbl("超时上限:", execBar, new Point(106, 15), C_SUB, F_SMALL));
            _nudTimeout = new NumericUpDown { Minimum = 5, Maximum = 300, Value = 60, Location = new Point(166, 13), Width = 56, Font = F_BODY, BackColor = C_CARD, BorderStyle = BorderStyle.FixedSingle };
            execBar.Controls.Add(_nudTimeout);
            execBar.Controls.Add(Lbl("s", execBar, new Point(226, 15), C_SUB, F_SMALL));

            int ex = 246;
            _btnStart = Btn("▶ 启动运转", execBar, new Point(ex, 8), 90, C_OK, Color.White); ex += 96;
            _btnStart.Click += OnStart;
            _btnPause = Btn("⏸ 静置", execBar, new Point(ex, 8), 70, C_WAIT, Color.White); ex += 76;
            _btnPause.Enabled = false; _btnPause.Click += OnPause;
            _btnStop = Btn("⏹ 制动", execBar, new Point(ex, 8), 70, C_ERR, Color.White);
            _btnStop.Enabled = false; _btnStop.Click += OnStop;

            _rtLog = new RichTextBox {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.FromArgb(241, 245, 249),
                Font = F_LOG, BorderStyle = BorderStyle.None, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, WordWrap = true
            };
            execCard.Controls.Add(_rtLog);
            _rtLog.BringToFront();
        }

        // =====================================================
        //  UI 扩展小部件工厂
        // =====================================================
        private Panel CreateCard(Control parent, DockStyle dock, Padding margin, int height = 0)
        {
            var pOuter = new Panel { Dock = dock, Padding = margin, BackColor = C_BG };
            if (height > 0) pOuter.Height = height + margin.Top + margin.Bottom;
            var pBorder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = C_BORDER };
            var pInner = new Panel { Dock = DockStyle.Fill, BackColor = C_CARD };
            pBorder.Controls.Add(pInner);
            pOuter.Controls.Add(pBorder);
            parent.Controls.Add(pOuter);
            return pInner;
        }

        private DataGridView CustomDgv()
        {
            var dgv = new DataGridView {
                Dock = DockStyle.Fill, RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = C_CARD, BorderStyle = BorderStyle.None, Font = F_BODY,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, GridColor = C_BORDER,
                EnableHeadersVisualStyles = false, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 36, RowTemplate = { Height = 36 }, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText
            };
            dgv.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = C_TEXT;
            dgv.ColumnHeadersDefaultCellStyle.Font = F_BOLD;
            dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(248, 250, 252);
            dgv.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            dgv.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 244, 255);
            dgv.DefaultCellStyle.SelectionForeColor = C_TEXT;
            dgv.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            return dgv;
        }

        private ListView CustomLv()
        {
            return new ListView {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false,
                BackColor = C_CARD, BorderStyle = BorderStyle.None, Font = F_BODY, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            };
        }

        private void OnTreeCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            // 对于 Button 列（即"测试"和"保存"），重绘更现代的外观
            if (e.RowIndex >= 0 && (e.ColumnIndex == _dgvTree.Columns["colTest"].Index || e.ColumnIndex == _dgvTree.Columns["colSave"].Index))
            {
                e.PaintBackground(e.CellBounds, true);
                var rect = e.CellBounds;
                rect.Inflate(-4, -6);
                var btnColor = e.ColumnIndex == _dgvTree.Columns["colTest"].Index ? C_ACCENT : C_OK;
                using (var brush = new SolidBrush(btnColor)) {
                    e.Graphics.FillRectangle(brush, rect);
                }
                TextRenderer.DrawText(e.Graphics, (string)e.FormattedValue, F_BOLD, rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                e.Handled = true;
            }
        }
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
                _treeData.Add(c);
                string defAction = GetActionLabel(c.Type); // mapped to "click", "input", etc.
                var row = _dgvTree.Rows[_dgvTree.Rows.Add()];
                
                string indent = c.Depth > 0 ? new string(' ', c.Depth * 3) + "└ " : "";
                
                row.Cells["colType"].Value    = c.Type;
                row.Cells["colMagicId"].Value = c.MagicId;
                row.Cells["colText"].Value    = indent + c.Text;
                row.Cells["colRect"].Value    = c.Rect;
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
            string name = row.Cells["colMagicId"].Value?.ToString() ?? "";
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
                "input" or "select" or "sendkeys" => $"{act}|{win}|{name}|{val}",
                "click" or "popupclick" => string.IsNullOrWhiteSpace(val) ? $"click|{win}|{name}" : $"click|{win}|{name}|{val}",
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
                    if (r.StartsWith("OK") && r.Contains("\n") && r.Contains("\t"))
                    {
                        RenderTestGrid(r);
                        _tcTest.SelectedTab = _tcTest.TabPages[1]; // Switch to Grid
                    }
                    else
                    {
                        AppendTest(r, r.StartsWith("OK") ? Color.FromArgb(120, 230, 120) : Color.FromArgb(255, 120, 100));
                        _tcTest.SelectedTab = _tcTest.TabPages[0]; // Switch to Text
                    }
                }
                catch (Exception ex) { AppendTest($"通信失败: {ex.Message}", C_ERR); }
            }
            else if (col == "colSave")
            {
                string? stepName = PromptInput("保存事件", @"请为该步骤取一个名称（如 ""选待处理行""）：", action.Describe());
                if (string.IsNullOrWhiteSpace(stepName)) return;

                var evt = new AutoEvent {
                    Id = EventStore.NewId(), Name = stepName!.Trim(), WindowName = win,
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
            cbA.Items.AddRange(new object[] { "click", "popupclick", "input", "sendkeys", "gettext", "select", "gridnext", "sleep" });
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

        private void OnEvtEdit(object? s, EventArgs e)
        {
            if (_lvEvents.SelectedItems.Count == 0) return;
            string id = _lvEvents.SelectedItems[0].Tag?.ToString() ?? "";
            var ev = _allEvents.FirstOrDefault(x => x.Id == id);
            if (ev == null) return;

            using var dlg = new Form {
                Text = "✎ 编辑事件匹配规则", Size = new Size(380, 260),
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent
            };

            var lblW = new Label { Text = "目标窗口:", Location = new Point(12, 12), AutoSize = true };
            var tbW  = new TextBox { Text = ev.WindowName, Location = new Point(80, 10), Width = 260 };
            
            var lblC = new Label { Text = "控件名称:", Location = new Point(12, 42), AutoSize = true };
            var tbC  = new TextBox { Text = ev.Action.ControlName, Location = new Point(80, 40), Width = 260 };
            
            var lblA = new Label { Text = "动作类型:", Location = new Point(12, 72), AutoSize = true };
            var cbA  = new ComboBox { Location = new Point(80, 70), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            cbA.Items.AddRange(new object[] { "click", "popupclick", "input", "sendkeys", "gettext", "select", "gridnext", "sleep" });
            cbA.Text = ev.Action.Type;
            
            var lblV = new Label { Text = "输入测试值:", Location = new Point(12, 102), AutoSize = true };
            var tbV  = new TextBox { Text = ev.Action.Value, Location = new Point(80, 100), Width = 260 };
            
            var lblN = new Label { Text = "步骤名:", Location = new Point(12, 132), AutoSize = true };
            var tbN  = new TextBox { Text = ev.Name, Location = new Point(80, 130), Width = 260 };

            var btnOk = new Button { Text = "保存更新", DialogResult = DialogResult.OK, Location = new Point(150, 175), Width = 90 };
            var btnCn = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(250, 175), Width = 90 };

            dlg.Controls.AddRange(new Control[] { lblW, tbW, lblC, tbC, lblA, cbA, lblV, tbV, lblN, tbN, btnOk, btnCn });
            dlg.AcceptButton = btnOk; dlg.CancelButton = btnCn;

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                ev.Name = tbN.Text.Trim();
                ev.WindowName = tbW.Text.Trim(); // 用户可在此截断后半截带有随机单号的名字
                ev.Action.Type = cbA.Text;
                ev.Action.ControlName = tbC.Text.Trim();
                ev.Action.Value = tbV.Text.Trim();
                ev.Action.MatchText = tbV.Text.Trim();
                _store.Save(ev);
                ReloadEvents();
            }
        }

        private void OnListViewCopy(object? sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                var lv = sender as ListView;
                if (lv != null && lv.SelectedItems.Count > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (ListViewItem item in lv.SelectedItems)
                    {
                        var row = string.Join("\t", item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(x => x.Text));
                        sb.AppendLine(row);
                    }
                    Clipboard.SetText(sb.ToString().TrimEnd());
                }
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
        //  流程导出与导入
        // =====================================================
        private void OnFlowExport(object? s, EventArgs e)
        {
            if (_flowSteps.Count == 0) { MessageBox.Show("当前流程为空，无法导出！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            using var sfd = new SaveFileDialog { Filter = "流程文件 (*.flow)|*.flow", Title = "导出当前流程", FileName = "MyAutoFlow.flow" };
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try {
                    var export = new Dictionary<string, object> {
                        ["FlowSteps"] = _flowSteps,
                        ["Events"] = _flowSteps.Select(id => _allEvents.FirstOrDefault(x => x.Id == id)).Where(ev => ev != null).ToList()
                    };
                    var json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(export);
                    File.WriteAllText(sfd.FileName, json, System.Text.Encoding.UTF8);
                    AppendLog($"💾 已成功导出流程及相关事件配置：{Path.GetFileName(sfd.FileName)}", C_OK);
                } catch (Exception ex) {
                    MessageBox.Show("导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnFlowImport(object? s, EventArgs e)
        {
            using var ofd = new OpenFileDialog { Filter = "流程文件 (*.flow)|*.flow", Title = "导入流程" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                try {
                    string json = File.ReadAllText(ofd.FileName, System.Text.Encoding.UTF8);
                    var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var exportDict = js.Deserialize<Dictionary<string, object>>(json);
                    
                    var newFlowSteps = new List<string>();
                    if (exportDict.ContainsKey("FlowSteps"))
                    {
                        var arr = exportDict["FlowSteps"] as System.Collections.ArrayList;
                        if (arr != null) foreach (var a in arr) newFlowSteps.Add(a?.ToString() ?? "");
                    }
                    
                    if (exportDict.ContainsKey("Events"))
                    {
                        var eventsObj = exportDict["Events"];
                        string evJson = js.Serialize(eventsObj);
                        var importedEvents = js.Deserialize<List<AutoEvent>>(evJson);
                        if (importedEvents != null)
                        {
                            foreach(var ev in importedEvents) {
                                _store.Save(ev);
                            }
                        }
                    }

                    _flowSteps = newFlowSteps;
                    _store.SaveFlow(_flowSteps);
                    ReloadEvents();
                    AppendLog($"📂 已成功从 {Path.GetFileName(ofd.FileName)} 导入并加载了 {newFlowSteps.Count} 个步骤。", C_OK);
                } catch (Exception ex) {
                    MessageBox.Show("导入失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
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

        private void RenderTestGrid(string rawOk)
        {
            if (_dgvTestOut.InvokeRequired)
            { _dgvTestOut.Invoke((Action)(() => RenderTestGrid(rawOk))); return; }
            
            _dgvTestOut.Columns.Clear();
            _dgvTestOut.Rows.Clear();
            
            string[] lines = rawOk.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 1) return; // Only OK: or empty
            
            // Assume the first data line determines column count
            string[] firstRowSplit = lines[1].Split('\t');
            for (int i = 0; i < firstRowSplit.Length; i++)
            {
                _dgvTestOut.Columns.Add($"col{i}", $"列 {i+1}");
            }
            
            for (int i = 1; i < lines.Length; i++)
            {
                var rowData = lines[i].Split('\t');
                _dgvTestOut.Rows.Add(rowData);
            }
        }

        // =====================================================
        //  辅助
        // =====================================================
        private static string GetActionLabel(string type)
        {
            if (IsGridType(type))        return "选行▼";
            if (type == "Edit" ||
                type.Contains("Text") ||
                type == "Document")      return "input";
            if (type == "ComboBox" ||
                type == "List")          return "选择▼";
            return "click";
        }

        private static bool IsGridType(string type)
            => type.Contains("Grid") || type.Contains("List") || type.Contains("Table") || type.Contains("Tree");

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
        private static Label Lbl(string text, Control parent, Point loc, Color? foreColor = null, Font? font = null)
        {
            var l = new Label {
                Text = text, Location = loc, AutoSize = true,
                Font = font ?? F_SMALL, ForeColor = foreColor ?? C_SUB, BackColor = Color.Transparent
            };
            parent.Controls.Add(l);
            return l;
        }

        private static Button Btn(string text, Control parent, Point loc, int width, Color back, Color fore)
        {
            var b = new Button {
                Text = text, Location = loc, Width = width, Height = 32,
                BackColor = back, ForeColor = fore, Font = F_BODY, Cursor = Cursors.Hand,
                FlatStyle = FlatStyle.Flat
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back);
            parent.Controls.Add(b); return b;
        }

        private static void SectionHeader(string text, Control parent, DockStyle dock = DockStyle.Top)
        {
            var p = new Panel { Dock = dock, Height = 45, BackColor = C_CARD };
            var l = new Label { Text = text, Font = F_TITLE, ForeColor = C_TEXT, Location = new Point(16, 12), AutoSize = true };
            var bottomBorder = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = C_BORDER };
            p.Controls.Add(l); p.Controls.Add(bottomBorder);
            parent.Controls.Add(p);
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
