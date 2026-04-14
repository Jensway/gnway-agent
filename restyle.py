import codecs

with codecs.open('Controller/MainForm.cs', 'r', 'utf-8') as f:
    text = f.read()

# Make the theme lighter and VS Code/VS 2022 like native theme
text = text.replace('Color.FromArgb(243, 244, 246)', 'Color.FromArgb(250, 250, 250)') # C_BG
text = text.replace('Color.FromArgb(226, 232, 240)', 'Color.FromArgb(204, 204, 204)') # C_BORDER
text = text.replace('Color.FromArgb(14, 165, 233)', 'Color.FromArgb(0, 120, 215)')   # C_ACCENT Main Blue
text = text.replace('Color.FromArgb(219, 234, 254)', 'Color.FromArgb(229, 243, 255)') # Selection light blue
text = text.replace('Color.White', 'Color.White') # Keep C_CARD white

# DGV Gridlines and Headers (remove visual noise)
text = text.replace('GridColor = C_BORDER', 'GridColor = Color.FromArgb(240, 240, 240)')
text = text.replace('BorderStyle = BorderStyle.None', 'BorderStyle = BorderStyle.FixedSingle')

# Make fonts standard
text = text.replace('new Font("Segoe UI", 9.5f)', 'new Font("Segoe UI", 9f)')
text = text.replace('new Font("Segoe UI", 8.5f)', 'new Font("Segoe UI", 9f)')
text = text.replace('new Font("Segoe UI", 12f, FontStyle.Bold)', 'new Font("Segoe UI", 10f, FontStyle.Bold)')

# DataGridView styling defaults for commercial application looks
text = text.replace('CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal', 'CellBorderStyle = DataGridViewCellBorderStyle.Single')

with codecs.open('Controller/MainForm.cs', 'w', 'utf-8') as f:
    f.write(text)
