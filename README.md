# GnwayAgent — 云联自动化工具

通过 **C# + UIAutomation** 实现对 GNWAY 云联服务端程序的精确控制。

## 项目结构

```
gnway-agent/
├── .github/workflows/build.yml   # GitHub Actions 自动编译
├── Agent/                        # 服务端（部署到云联服务器）
│   ├── Agent.csproj
│   └── Agent.cs
├── Controller/                   # 客户端（运行在本地电脑）
│   ├── Controller.csproj
│   └── Controller.cs
└── scripts/                      # 自动化脚本示例
    └── erp_submit.txt
```

## 快速开始

### 1. 编译（无需本地安装编译器）

Push 到 GitHub 后，Actions 会自动编译。  
在仓库的 **Actions → Build GnwayAgent → Artifacts** 下载 `GnwayAgent-Release.zip`

### 2. 部署服务端

将 `agent.exe` 复制到云联服务器任意目录，双击运行即可：

```
agent.exe
```

### 3. 运行客户端

```
controller.exe 服务器IP
```

### 4. 常用命令

```
# 查看当前所有窗口（先用这个确认程序名称）
[服务器] > windows

# 查看控件树（确认控件名称）
[服务器] > tree|ERP系统|5

# 点击按钮
[服务器] > click|ERP系统|保存

# 在工具栏内点击（解决同名问题）
[服务器] > click|ERP系统|保存|工具栏

# 输入文字
[服务器] > input|ERP系统|用户名|admin

# 等待弹窗并自动确认
[服务器] > wait|ERP系统|保存成功|confirm

# 滚动到底部
[服务器] > scroll|ERP系统|数据列表|bottom

# 执行脚本
[服务器] > run scripts/erp_submit.txt
```

## 资源占用

| 组件 | 内存 | CPU(待机) | 文件大小 |
|------|------|----------|---------|
| agent.exe（服务端） | ~2MB | 0% | ~100KB |
| controller.exe（本地） | ~1MB | 0% | ~50KB |

服务器依赖：**Windows 自带 .NET Framework 4.8，零额外安装**
