// ============================================================
//  GridNextDialog.cs — 配置"选行"动作的对话框
//  显示当前表格内容，让用户指定按哪列/哪个文字选行
// ============================================================

using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GnwayController.Models;

namespace GnwayController
{
    public class GridNextDialog : Form
    {
        static readonly Font F_BODY  = new Font("Segoe UI",  9.5f);
        static readonly Font F_SMALL = new Font("Segoe UI",  8.5f);
        static readonly Font F_MONO  = new Font("Consolas",  8.5f);

        private readonly AgentClient _client;
        private readonly string      _windowName;
        private readonly string      _controlName;

        private TextBox      _tbMatch  = null!;
        private NumericUpDown _nudCol  = null!;
        private DataGridView  _dgvRows = null!;
        private Button        _btnTest = null!;
        private Label         _lblRes  = null!;

        public EventAction Result { get; private set; } = new EventAction { Type = "gridnext" };

        public GridNextDialog(AgentClient client, string windowName, string controlName)
        {
            _client      = client;
            _windowName  = windowName;
            _controlName = controlName;
            InitUI();
        }

        private void InitUI()
        {
            Text            = $"配置表格行选择 — {_controlName}";
            Size            = new Size(680, 440);
            MinimumSize     = new Size(520, 360);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox     = false;
            Font            = F_BODY;

            // ── 顶部说明 ──────────────────────────────────────
            var lblInfo = new Label {
                Text = $"表格控件：{_controlName}\n录制时选定「按哪列的文字」自动找待处理行。",
                Dock = DockStyle.Top, Height = 46,
                Padding = new Padding(10, 6, 0, 0),
                Font = F_SMALL, ForeColor = Color.FromArgb(60, 80, 120),
                BackColor = Color.FromArgb(240, 245, 255)
            };
            Controls.Add(lblInfo);

            // ── 设置区 ────────────────────────────────────────
            var setupPanel = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = Color.White };
            Controls.Add(setupPanel);

            setupPanel.Controls.Add(new Label {
                Text = "匹配文字：", Location = new Point(10, 12), AutoSize = true, Font = F_SMALL
            });
            _tbMatch = new TextBox {
                Text = "待处理", Location = new Point(76, 8), Width = 120, Font = F_BODY
            };
            setupPanel.Controls.Add(_tbMatch);

            setupPanel.Controls.Add(new Label {
                Text = "检查第几列（0起）：", Location = new Point(210, 12), AutoSize = true, Font = F_SMALL
            });
            _nudCol = new NumericUpDown {
                Minimum = 0, Maximum = 30, Value = 0,
                Location = new Point(330, 8), Width = 60, Font = F_BODY
            };
            setupPanel.Controls.Add(_nudCol);

            _btnTest = new Button {
                Text = "▶ 加载并测试", Location = new Point(408, 8), Width = 110, Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(37, 99, 185),
                ForeColor = Color.White, Font = F_SMALL
            };
            _btnTest.FlatAppearance.BorderSize = 0;
            _btnTest.Click += OnTest;
            setupPanel.Controls.Add(_btnTest);

            // ── 表格预览 ──────────────────────────────────────
            _dgvRows = new DataGridView {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                MultiSelect = false,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                GridColor = Color.FromArgb(220, 228, 240),
                Font = F_MONO,
                AllowUserToResizeRows = false,
                RowTemplate = { Height = 24 }
            };
            _dgvRows.ColumnHeadersDefaultCellStyle.Font = F_SMALL;
            Controls.Add(_dgvRows);

            // ── 底部 ──────────────────────────────────────────
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Color.White };
            Controls.Add(bottom);

            _lblRes = new Label {
                Location = new Point(10, 14), AutoSize = true,
                Font = F_SMALL, ForeColor = Color.FromArgb(37, 99, 185),
                BackColor = Color.Transparent
            };
            bottom.Controls.Add(_lblRes);

            var btnOk = new Button {
                Text = "✓ 确认录制", DialogResult = DialogResult.OK,
                Location = new Point(460, 10), Width = 100, Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(22, 101, 52), ForeColor = Color.White, Font = F_SMALL
            };
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Click += OnConfirm;
            bottom.Controls.Add(btnOk);

            var btnCn = new Button {
                Text = "取消", DialogResult = DialogResult.Cancel,
                Location = new Point(568, 10), Width = 72, Height = 26,
                FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(240, 240, 240),
                Font = F_SMALL
            };
            btnCn.FlatAppearance.BorderSize = 0;
            bottom.Controls.Add(btnCn);

            AcceptButton = btnOk;
            CancelButton = btnCn;

            // 加载时自动刷新
            Load += async (_, __) => await LoadRows();
        }

        private async Task LoadRows()
        {
            _btnTest.Enabled = false;
            _lblRes.Text = "正在读取表格...";

            string raw = await Task.Run(
                () => _client.Send($"gridrows|{_windowName}|{_controlName}|200"));

            _btnTest.Enabled = true;
            _dgvRows.Columns.Clear();
            _dgvRows.Rows.Clear();

            if (!raw.StartsWith("OK:"))
            {
                _lblRes.Text = $"读取失败：{raw}";
                return;
            }

            var lines = raw.Substring(3)
                           .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) { _lblRes.Text = "表格为空"; return; }

            int colCount = lines.Max(l => l.Split('\t').Length);
            for (int c = 0; c < colCount; c++)
                _dgvRows.Columns.Add($"c{c}", $"列{c}");

            foreach (var line in lines)
            {
                var cells = line.Split('\t');
                var row   = _dgvRows.Rows[_dgvRows.Rows.Add()];
                for (int c = 0; c < cells.Length && c < colCount; c++)
                    row.Cells[c].Value = cells[c];
            }

            _lblRes.Text = $"共 {lines.Length} 行";
            HighlightMatches();
        }

        private async void OnTest(object? s, EventArgs e)
        {
            await LoadRows();
            HighlightMatches();

            // 实际执行一次选行
            string matchText = _tbMatch.Text.Trim();
            int    colIndex  = (int)_nudCol.Value;

            // 找到匹配行索引
            int foundIdx = -1;
            foreach (DataGridViewRow r in _dgvRows.Rows)
            {
                if (colIndex < r.Cells.Count)
                {
                    string? cellVal = r.Cells[colIndex].Value?.ToString();
                    if (cellVal != null && cellVal.Contains(matchText))
                    { foundIdx = r.Index; break; }
                }
            }

            if (foundIdx < 0)
            {
                _lblRes.Text = $"⚠ 未找到含「{matchText}」的行（列{colIndex}）";
                return;
            }

            string selRes = await Task.Run(
                () => _client.Send($"gridselect|{_windowName}|{_controlName}|{foundIdx}"));
            _lblRes.Text = $"[列{colIndex}] 含「{matchText}」→ 第{foundIdx}行 {selRes}";
        }

        private void HighlightMatches()
        {
            string matchText = _tbMatch.Text.Trim();
            int    colIndex  = (int)_nudCol.Value;
            if (string.IsNullOrEmpty(matchText)) return;

            foreach (DataGridViewRow r in _dgvRows.Rows)
            {
                bool  hit = false;
                if (colIndex < r.Cells.Count)
                {
                    string? v = r.Cells[colIndex].Value?.ToString();
                    hit = v != null && v.Contains(matchText);
                }
                r.DefaultCellStyle.BackColor = hit
                    ? Color.FromArgb(220, 252, 231)
                    : Color.White;
                r.DefaultCellStyle.ForeColor = hit
                    ? Color.FromArgb(22, 101, 52)
                    : Color.FromArgb(30, 40, 60);
            }
        }

        private void OnConfirm(object? s, EventArgs e)
        {
            Result = new EventAction {
                Type        = "gridnext",
                ControlName = _controlName,
                MatchText   = _tbMatch.Text.Trim(),
                ColIndex    = (int)_nudCol.Value
            };
        }
    }
}
