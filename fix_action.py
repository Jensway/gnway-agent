import os

# 读取 MainForm.cs
path = "Controller/MainForm.cs"
with open(path, "r", encoding="utf-8") as f:
    content = f.read()

# 替换所有中文动作为英文字段
content = content.replace("return \"选行\";", "return \"gridnext\";")
content = content.replace("return \"输入\";", "return \"input\";")
content = content.replace("return \"选择\";", "return \"select\";")
content = content.replace("return \"点击\";", "return \"click\";")

# 增加对常见类的支持
if "private static bool IsGridType(string type)" in content:
    content = content.replace(
        "private static bool IsGridType(string type)\n            => type == \"DataGrid\" || type == \"List\" || type == \"Table\"\n            || type == \"DataItem\" || type == \"Tree\";",
        "private static bool IsGridType(string type)\n            => type.Contains(\"Grid\") || type.Contains(\"List\") || type.Contains(\"Table\") || type.Contains(\"Tree\");"
    )

with open(path, "w", encoding="utf-8") as f:
    f.write(content)

print("MainForm.cs successfully updated action labels to match combobox.")
