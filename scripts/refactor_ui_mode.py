import codecs
import re

with codecs.open('Controller/MainForm.cs', 'r', 'utf-8') as f:
    code = f.read()

# 1. Add class variables
vars_target = r"        // ── 状态 ──────────────────────────────────────────────"
vars_new = """        // ── 双模状态 ────────────────────────────────────────
        bool _isStudioMode = false;
        Panel _pnlAssistantRoot = null!;
        Panel _pnlStudioRoot = null!;
        Panel _pnlAssistantBody = null!;
        Panel _flowContainer = null!;
        SplitContainer _innerSplit = null!;

        // ── 状态 ──────────────────────────────────────────────"""
code = code.replace(vars_target, vars_new)

# 2. Modify InitUI Layout
init_target = """            // ── 主体 SplitContainer ──────────────────────────
            var split = new SplitContainer {
                Dock = DockStyle.Fill,
                SplitterWidth = 5,
                BackColor = C_BG,
                Orientation = Orientation.Vertical,
                Panel1MinSize = 280
            };
            Controls.Add(split);
            split.BringToFront(); // [!!! FIX MANGLED FORM LAYOUT OVERLAP !!!]
            this.Load += (_, __) => split.SplitterDistance = (int)(ClientSize.Width * 0.55);

            BuildLeftPanel(split.Panel1);
            BuildRightPanel(split.Panel2);
        }"""
init_new = """            // ── 根布局双模容器 ────────────────────────────────────────
            _pnlAssistantRoot = new Panel { Dock = DockStyle.Fill, Visible = true, BackColor = C_BG };
            _pnlStudioRoot = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = C_BG };
            
            Controls.Add(_pnlAssistantRoot);
            Controls.Add(_pnlStudioRoot);

            // 【模式1】设计器模式内容 (挂载原逻辑)
            var split = new SplitContainer {
                Dock = DockStyle.Fill, SplitterWidth = 5, BackColor = C_BG, Orientation = Orientation.Vertical, Panel1MinSize = 280
            };
            
            // 为设计器模式添加一个顶部工具栏（返回小助手用）
            var studioNav = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = C_HDR_BG };
            var btnGoAsst = Btn("⬅ 退出设计器，返回执行助手", studioNav, new Point(12, 8), 240, Color.FromArgb(50, 255, 255, 255), Color.White);
            btnGoAsst.Click += (_, __) => SwitchMode(false);
            
            _pnlStudioRoot.Controls.Add(split);
            _pnlStudioRoot.Controls.Add(studioNav);
            
            this.Load += (_, __) => split.SplitterDistance = (int)(ClientSize.Width * 0.55);

            BuildLeftPanel(split.Panel1);
            BuildRightPanel(split.Panel2);

            // 【模式2】小助手模式内容
            var asstTop = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = C_HDR_BG };
            _pnlAssistantRoot.Controls.Add(asstTop);
            
            var asstTitle = new Label { Text = "GnwayAgent 执行器", ForeColor = Color.White, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Location = new Point(16, 20), AutoSize = true };
            asstTop.Controls.Add(asstTitle);

            var btnGoStudio = Btn("⚙️ 开发设计模式", asstTop, new Point(10, 16), 120, Color.FromArgb(50, 255, 255, 255), Color.White);
            asstTop.Resize += (s, e) => btnGoStudio.Left = asstTop.Width - 130;
            btnGoStudio.Click += (_, __) => SwitchMode(true);
            asstTop.Controls.Add(btnGoStudio);

            _pnlAssistantBody = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(12) };
            _pnlAssistantRoot.Controls.Add(_pnlAssistantBody);
            _pnlAssistantBody.BringToFront();

            // 软件启动时，进入小助手模式
            this.Load += (_, __) => SwitchMode(false);
        }

        private void SwitchMode(bool toStudio)
        {
            _isStudioMode = toStudio;
            if (toStudio)
            {
                _pnlAssistantRoot.Visible = false;
                _pnlStudioRoot.Visible = true;
                this.Size = new Size(1300, 780);
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.CenterToScreen();

                if (_flowContainer != null && _innerSplit != null)
                {
                    _flowContainer.Controls.Add(_innerSplit);
                }
            }
            else
            {
                _pnlStudioRoot.Visible = false;
                _pnlAssistantRoot.Visible = true;
                this.Size = new Size(420, 760);
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.MaximizeBox = false;
                this.CenterToScreen();

                if (_pnlAssistantBody != null && _innerSplit != null)
                {
                    _pnlAssistantBody.Controls.Add(_innerSplit);
                }
            }
        }
"""
code = code.replace(init_target, init_new)


# 3. Hook BuildRightPanel to _flowContainer
right_target = """            BuildEventsPanel(rightSplit.Panel1);
            BuildFlowPanel(rightSplit.Panel2);
        }"""
right_new = """            BuildEventsPanel(rightSplit.Panel1);
            _flowContainer = new Panel { Dock = DockStyle.Fill };
            rightSplit.Panel2.Controls.Add(_flowContainer);
            BuildFlowPanel(_flowContainer);
        }"""
code = code.replace(right_target, right_new)


# 4. Modify BuildFlowPanel to use _innerSplit
flow_target = """        private void BuildFlowPanel(SplitterPanel panel)
        {
            var innerSplit = new SplitContainer {"""
flow_new = """        private void BuildFlowPanel(Control panel)
        {
            _innerSplit = new SplitContainer {"""
code = code.replace(flow_target, flow_new)

flow_target2 = """            panel.Controls.Add(innerSplit);
            this.Load += (_, __) => {
                innerSplit.SplitterDistance = (int)(innerSplit.Height * 0.55);
            };"""
flow_new2 = """            panel.Controls.Add(_innerSplit);
            this.Load += (_, __) => {
                try { _innerSplit.SplitterDistance = (int)(_innerSplit.Height * 0.55); } catch { }
            };"""
code = code.replace(flow_target2, flow_new2)

# Replace 'innerSplit.' with '_innerSplit.'
code = code.replace("innerSplit.Panel1", "_innerSplit.Panel1")
code = code.replace("innerSplit.Panel2", "_innerSplit.Panel2")
code = code.replace("innerSplit.", "_innerSplit.")

# Modify toolbar logic to fix parentage issues. We actually want `toolbar` inside `splitLeft` or `pnlStudioRoot`.
# Wait, `toolbar` was added to `Controls.Add(toolbar)`. We should add it to `_pnlStudioRoot` instead.
btn_test_target = """            Controls.Add(toolbar);"""
btn_test_new = """            // 将配置类工具栏直接装入设计器面板中，小助手模式不显示
            _pnlStudioRoot.Controls.Add(toolbar);
            _pnlStudioRoot.Controls.SetChildIndex(toolbar, 0);"""

if btn_test_target in code and "_pnlStudioRoot.Controls.Add(toolbar)" not in code:
    code = code.replace(btn_test_target, btn_test_new)

with codecs.open('Controller/MainForm.cs', 'w', 'utf-8') as f:
    f.write(code)

print("MainForm.cs successfully dual-moded.")
