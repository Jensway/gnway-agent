import codecs

# Fix MainForm.cs
with codecs.open('Controller/MainForm.cs', 'r', 'utf-8') as f:
    text = f.read()

# 1. Font fixes (make it 9f instead of 10f)
text = text.replace('new Font("Segoe UI", 10f, FontStyle.Bold)', 'new Font("Segoe UI", 9f, FontStyle.Bold)')

# 2. Align the middle section headers perfectly (180 -> 240)
text = text.replace('splitLeft.SplitterDistance = 180;', 'splitLeft.SplitterDistance = 240;')

with codecs.open('Controller/MainForm.cs', 'w', 'utf-8') as f:
    f.write(text)

# Fix FlowRunner.cs
with codecs.open('Controller/Engine/FlowRunner.cs', 'r', 'utf-8') as f:
    text = f.read()

# 3. Add explicit grab windows command before waiting
target = '''                string checkCmd;
                if (!string.IsNullOrEmpty(evt.Action.ControlName))'''
replace = '''                string checkCmd;
                try { _client.Send("windows"); } catch { } // Force engine to grab new windows
                if (!string.IsNullOrEmpty(evt.Action.ControlName))'''
text = text.replace(target, replace)

with codecs.open('Controller/Engine/FlowRunner.cs', 'w', 'utf-8') as f:
    f.write(text)
