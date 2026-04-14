import codecs

with codecs.open('Controller/MainForm.cs', 'r', 'utf-8') as f:
    lines = f.readlines()

def replace_block(start_marker, end_marker, new_content):
    global lines
    start_idx = -1
    end_idx = -1
    for i, line in enumerate(lines):
        if start_marker in line and start_idx == -1:
            start_idx = i
        if end_marker in line and start_idx != -1 and end_idx == -1:
            end_idx = i
            break
            
    if start_idx != -1 and end_idx != -1:
        lines = lines[:start_idx] + [new_content] + lines[end_idx:]
        print(f"Replaced from {start_marker} to {end_marker}")
    else:
        print(f"Could not find {start_marker} and {end_marker}")

new_tokens = """        // ── 设计令牌 (SaaS 级基准) ──────────────────────────
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

"""

replace_block('// ── 设计令牌', 'static readonly Color C_DBG', new_tokens)

ui_methods = """        // =====================================================
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
"""

replace_block('public MainForm()', 'private async void OnTestConnect', ui_methods)

with codecs.open('Controller/MainForm.cs', 'w', 'utf-8') as f:
    f.writelines(lines)

def replace_btn_factory():
    global lines
    start_idx = -1
    end_idx = -1
    for i, line in enumerate(lines):
        if "private static Button Btn(" in line and start_idx == -1:
            start_idx = i
        if "private static Label Lbl(" in line and start_idx != -1 and end_idx == -1:
            end_idx = i - 1
            break

    if start_idx != -1 and end_idx != -1:
        new_cnt = """        private static Button Btn(string text, Control parent, Point loc, int width, Color back, Color fore)
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
"""
        lines = lines[:start_idx] + [new_cnt] + lines[end_idx+1:]

replace_btn_factory()

def remove_old_section_header():
    global lines
    start_idx = -1
    end_idx = -1
    for i, line in enumerate(lines):
        if "private void SectionHeader(" in line and i > len(lines)-100:
            start_idx = i
            end_idx = i + 7 
            break
            
    if start_idx != -1:
        lines = lines[:start_idx] + lines[end_idx+1:]
        
remove_old_section_header()

with codecs.open('Controller/MainForm.cs', 'w', 'utf-8') as f:
    f.writelines(lines)
