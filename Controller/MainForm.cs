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
        static readonly Font F_BODY  = new Font("Segoe UI", 9f);
        static readonly Font F_SMALL = new Font("Segoe UI", 8.5f);
        static readonly Font F_BOLD  = new Font("Segoe UI", 9f, FontStyle.Bold);
        static readonly Font F_LOG   = new Font("Consolas",  9f);
        static readonly Font F_TITLE = new Font("Segoe UI", 10f, FontStyle.Bold);
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

        // ── 双模状态 ────────────────────────────────────────

        SplitContainer _innerSplit = null!;

        // ── 双模状态 ────────────────────────────────────────
        bool _isStudioMode = false;
        Panel _pnlAssistantRoot = null!;
        Panel _pnlStudioRoot = null!;
        Panel _pnlAssistantBody = null!;
        Panel _flowContainer = null!;

        // ── 状态 ──────────────────────────────────────────────
        EventStore         _store     = null!;
        List<AutoEvent>    _allEvents = new();
        List<string>       _flowSteps = new();   // 有序步骤 ID
        FlowRunner?        _runner;

        // 当前控件树数据（type/name/enabled列表）
        List<ControlInfo> _treeData = new();

        // 用于流程步骤UI状态显示
        private int _currentStepIndex = -1;
        private System.Windows.Forms.Timer _blinkTimer = new System.Windows.Forms.Timer { Interval = 400 };
        private bool _blinkState = false;

        // =====================================================
        public MainForm()
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
            // 待会在下方装配到 _pnlStudioRoot

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

            // ── 根布局双模容器 (TabControl) ──────────────────────────
            var mainTabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f) };
            Controls.Add(mainTabs);

            var tabAssistant = new TabPage("执行助手");
            var tabStudio = new TabPage("开发设计工作台");

            mainTabs.TabPages.Add(tabAssistant);
            mainTabs.TabPages.Add(tabStudio);

            _pnlAssistantRoot = new Panel { Dock = DockStyle.Fill, Visible = true, BackColor = Color.White };
            _pnlStudioRoot = new Panel { Dock = DockStyle.Fill, Visible = true, BackColor = C_BG };
            
            tabAssistant.Controls.Add(_pnlAssistantRoot);
            tabStudio.Controls.Add(_pnlStudioRoot);
            
            _pnlStudioRoot.Controls.Add(toolbar);
            _pnlStudioRoot.Controls.SetChildIndex(toolbar, 0);

            // 【模式】设计器模式内容
            var split = new SplitContainer {
                Dock = DockStyle.Fill, SplitterWidth = 5, BackColor = C_BG, Orientation = Orientation.Vertical, Panel1MinSize = 280
            };
            
            _pnlStudioRoot.Controls.Add(split);
            split.BringToFront(); // 修复叠加问题
            
            this.Load += (_, __) => { try { split.SplitterDistance = (int)(ClientSize.Width * 0.55); } catch { } };

            BuildLeftPanel(split.Panel1);
            BuildRightPanel(split.Panel2);

            // 【模式】小助手模式内容
            _pnlAssistantBody = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12) };
            _pnlAssistantRoot.Controls.Add(_pnlAssistantBody);
            _pnlAssistantBody.BringToFront();

            // 监听标签页切换，动态调整窗口和流程模块位置
            mainTabs.SelectedIndexChanged += (s, e) => {
                bool toStudio = (mainTabs.SelectedIndex == 1);
                _isStudioMode = toStudio;
                if (toStudio)
                {
                    this.Size = new Size(1300, 780);
                    this.FormBorderStyle = FormBorderStyle.Sizable;
                    this.MaximizeBox = true;
                    // 必须重置窗口位置居中
                    this.CenterToScreen();

                    // 根据所处的 Tab 将执行列表放回设计器或者小助手
                    if (_flowContainer != null && _innerSplit != null)
                    {
                        _flowContainer.Controls.Add(_innerSplit);
                    }
                }
                else
                {
                    this.Size = new Size(420, 760);
                    this.FormBorderStyle = FormBorderStyle.FixedDialog;
                    this.MaximizeBox = false;
                    this.CenterToScreen();
                    
                    if (_pnlAssistantBody != null && _innerSplit != null)
                    {
                        _pnlAssistantBody.Controls.Add(_innerSplit);
                    }
                }
            };

            // 软件启动时，进入小助手模式
            this.Load += (_, __) => {
                mainTabs.SelectedIndex = 0;
                this.Size = new Size(420, 760);
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.MaximizeBox = false;
                this.CenterToScreen();
                
                if (_pnlAssistantBody != null && _innerSplit != null)
                {
                    _pnlAssistantBody.Controls.Add(_innerSplit);
                }
            };
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
            this.Load += (_, __) => { try { splitLeft.SplitterDistance = 240; } catch { } };

            // --- 上半部：主表（窗口列表） ---
            SectionHeader("在线窗口 (Master)", splitLeft.Panel1, DockStyle.Top);
            _dgvWindows = new DataGridView {
                Dock = DockStyle.Fill, RowHeadersVisible = false, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AllowUserToResizeRows = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = C_CARD,
                BorderStyle = BorderStyle.FixedSingle, ReadOnly = true, Font = F_SMALL, RowTemplate = { Height = 28 }
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
                AllowUserToResizeRows = false, AllowUserToResizeColumns = true,
                MultiSelect = false, SelectionMode = DataGridViewSelectionMode.CellSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, RowTemplate = { Height = 25 },
                BackgroundColor = C_CARD, BorderStyle = BorderStyle.FixedSingle, GridColor = Color.FromArgb(240, 240, 240), Font = F_SMALL,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 25, EditMode = DataGridViewEditMode.EditOnEnter,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ScrollBars = ScrollBars.Both,
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText
            };
            _dgvTree.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 248, 252);
            _dgvTree.ColumnHeadersDefaultCellStyle.BackColor  = Color.FromArgb(240, 244, 252);
            _dgvTree.ColumnHeadersDefaultCellStyle.Font       = F_SMALL;
            _dgvTree.ColumnHeadersDefaultCellStyle.ForeColor  = C_TEXT;
            _dgvTree.EnableHeadersVisualStyles = false;
            _dgvTree.DefaultCellStyle.SelectionBackColor = Color.FromArgb(229, 243, 255);
            _dgvTree.DefaultCellStyle.SelectionForeColor = C_TEXT;

            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "类型", Name = "colType", Width = 140, MinimumWidth = 40, Resizable = DataGridViewTriState.True, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, Frozen = true });
            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "标识码", Name = "colMagicId", Width = 200, MinimumWidth = 60, Resizable = DataGridViewTriState.True, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable, Frozen = true });
            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "标题文字", Name = "colText", Width = 200, MinimumWidth = 60, Resizable = DataGridViewTriState.True, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "坐标矩形", Name = "colRect", Width = 160, MinimumWidth = 40, Resizable = DataGridViewTriState.True, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _dgvTree.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "状态", Name = "colEnabled", Width = 40, MinimumWidth = 30, Resizable = DataGridViewTriState.True, AutoSizeMode = DataGridViewAutoSizeColumnMode.None, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            
            var colAct = new DataGridViewComboBoxColumn { HeaderText = "操作", Name = "colAction", Width = 85, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox, SortMode = DataGridViewColumnSortMode.NotSortable };
            colAct.Items.AddRange("click", "input", "sendkeys", "select", "gridnext", "popupclick", "sleep");
            colAct.DefaultCellStyle.BackColor = Color.FromArgb(245, 245, 245);
            _dgvTree.Columns.Add(colAct);

            var colVal = new DataGridViewTextBoxColumn { HeaderText = "测试值", Name = "colValue", Width = 140, SortMode = DataGridViewColumnSortMode.NotSortable };
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

            _tcTest = new TabControl {
                Location = new Point(8, 24), Size = new Size(0, 95)
            };
            bottom.Resize += (_, __) => _tcTest.Width = bottom.Width - 16;
            bottom.Controls.Add(_tcTest);

            var tpTxt = new TabPage("日志输出") { BackColor = C_CARD };
            _tcTest.TabPages.Add(tpTxt);
            var tpGrid = new TabPage("数据网格") { BackColor = C_CARD };
            _tcTest.TabPages.Add(tpGrid);

            _rtTestOut = new RichTextBox {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 24, 36),
                ForeColor = Color.FromArgb(200, 215, 240), Font = F_MONO, BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical, WordWrap = true
            };
            tpTxt.Controls.Add(_rtTestOut);

            _dgvTestOut = new DataGridView {
                Dock = DockStyle.Fill, RowHeadersVisible = false, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, ReadOnly = true, BackgroundColor = C_CARD,
                BorderStyle = BorderStyle.FixedSingle, Font = F_SMALL,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
            };
            tpGrid.Controls.Add(_dgvTestOut);

            var btnClearTest = Btn("清空输出", bottom, new Point(8, 122), 70, C_BG, C_SUB);
            btnClearTest.Click += (_, __) => { _rtTestOut.Clear(); _dgvTestOut.Columns.Clear(); };
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
            this.Load += (_, __) => { try { rightSplit.SplitterDistance = 240; } catch { } };

            BuildEventsPanel(rightSplit.Panel1);
            _flowContainer = new Panel { Dock = DockStyle.Fill };
            rightSplit.Panel2.Controls.Add(_flowContainer);
            BuildFlowPanel(_flowContainer);
        }

        // ── 右上：已录制事件 ──────────────────────────────────
        private void BuildEventsPanel(SplitterPanel panel)
        {
            SectionHeader("已录制的事件", panel, DockStyle.Top);

            // 按钮条
            var btnBar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = C_CARD };
            panel.Controls.Add(btnBar);
            int bx = 4;
            _btnEvtTest = Btn("▶ 测试", btnBar, new Point(bx, 4), 70, C_ACCENT, Color.White); bx += 76;
            _btnEvtTest.Click += OnEvtTest;
            var btnEvtEdit = Btn("✎ 编辑", btnBar, new Point(bx, 4), 62, C_OK, Color.White); bx += 68;
            btnEvtEdit.Click += OnEvtEdit;
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
                BackColor = C_CARD, BorderStyle = BorderStyle.FixedSingle,
                Font = F_SMALL, HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _lvEvents.Columns.Add("#",    28);
            _lvEvents.Columns.Add("名称", 140);
            _lvEvents.Columns.Add("窗口", 200);
            _lvEvents.Columns.Add("动作", 250);
            _lvEvents.KeyDown += OnListViewCopy;
            panel.Controls.Add(_lvEvents);
            _lvEvents.BringToFront(); // [!!! FIX MANGLED RIGHT-TOP PANEL (EVENTS LIST) OVERLAP !!!]
        }

        // ── 右下：流程步骤 + 执行控制 + 日志 ─────────────────
        private void BuildFlowPanel(Control panel)
        {
            _innerSplit = new SplitContainer {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 5,
                BackColor = C_BG,
                Panel1MinSize = 160
            };
            panel.Controls.Add(_innerSplit);
            this.Load += (_, __) => {
                try { _innerSplit.SplitterDistance = (int)(_innerSplit.Height * 0.55); } catch { }
            };

            // ── 上半：流程步骤列表 ────────────────────────────
            SectionHeader("流程步骤", _innerSplit.Panel1, DockStyle.Top);

            var stepBtnBar = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = C_CARD };
            _innerSplit.Panel1.Controls.Add(stepBtnBar);
            int sx = 4;
            _btnStepAdd = Btn("+ 添加选中", stepBtnBar, new Point(sx, 4), 90, C_ACCENT, Color.White); sx += 96;
            _btnStepAdd.Click += OnStepAdd;
            _btnStepRm  = Btn("− 移除", stepBtnBar, new Point(sx, 4), 60, C_BG, C_TEXT); sx += 66;
            _btnStepRm.Click  += OnStepRemove;
            _btnStepUp  = Btn("↑", stepBtnBar, new Point(sx, 4), 28, C_BG, C_TEXT); sx += 34;
            _btnStepUp.Click  += (_, __) => MoveStep(-1);
            _btnStepDn  = Btn("↓", stepBtnBar, new Point(sx, 4), 28, C_BG, C_TEXT); sx += 34;
            _btnStepDn.Click  += (_, __) => MoveStep(+1);
            
            var btnImport = Btn("📂 导入", stepBtnBar, new Point(sx, 4), 60, C_BG, C_TEXT); sx += 66;
            btnImport.Click += OnFlowImport;
            var btnExport = Btn("💾 导出", stepBtnBar, new Point(sx, 4), 60, C_BG, C_TEXT);
            btnExport.Click += OnFlowExport;

            _lvSteps = new ListView {
                Dock = DockStyle.Fill, View = View.Details,
                FullRowSelect = true, GridLines = false,
                BackColor = C_CARD, BorderStyle = BorderStyle.FixedSingle,
                Font = F_SMALL, HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _lvSteps.Columns.Add("步",   28);
            _lvSteps.Columns.Add("事件名", 140);
            _lvSteps.Columns.Add("窗口",   200);
            _lvSteps.KeyDown += OnListViewCopy;
            _innerSplit.Panel1.Controls.Add(_lvSteps);
            _lvSteps.BringToFront(); // [!!! FIX MANGLED RIGHT-BOTTOM-LEFT PANEL (FLOW STEPS) OVERLAP !!!]

            // ── 下半：执行控制 + 日志 ─────────────────────────
            SectionHeader("执行控制 & 日志", _innerSplit.Panel2, DockStyle.Top);

            var execBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = C_CARD };
            _innerSplit.Panel2.Controls.Add(execBar);

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
                Font = F_LOG, BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Vertical
            };
            _innerSplit.Panel2.Controls.Add(_rtLog);
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
                        MatchText   = tbV.Text.Trim(),
                        SleepMs     = cbA.Text == "sleep" && int.TryParse(tbV.Text.Trim(), out int ms) ? ms : 0
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
                var item = new ListViewItem("   " + (i + 1).ToString());
                item.SubItems.Add(ev?.Name ?? stepId);
                item.SubItems.Add(ev?.WindowName ?? "");
                item.Tag = stepId;
                item.ForeColor = Color.Gray; // 默认为灰色（待执行状态）
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
                if (ev.Action.Type == "sleep" && int.TryParse(ev.Action.Value, out int ms)) {
                    ev.Action.SleepMs = ms;
                }
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
            // 每次启动时重置所有步骤状态
            RefreshFlowListView();
            _currentStepIndex = -1;
            _blinkTimer.Start();
            AppendLog($"▶ 流程启动，从第 {startStep + 1} 步，超时 {timeoutSec}s(等待中)", C_OK);

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
        }

        private void SetRunning(bool running)
        {
            if (!running && _blinkTimer.Enabled) _blinkTimer.Stop();
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
