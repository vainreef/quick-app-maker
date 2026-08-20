# Vainreef Quick App Maker

> 新 Windows 电脑读取这一页后，一键准备 Windows App 工具链，然后立即进入 App 需求访谈。

## Agent 从这里开始

用户说：

```text
用 https://gitee.com/freevian/quick-app-maker README 开始。
```

Agent 直接执行下面三行。使用 `main` 分支，不使用 `master`：

```powershell
$entry = Join-Path $env:TEMP 'vainreef-quick-app-maker-entry.ps1'
Invoke-WebRequest -UseBasicParsing -Uri 'https://gitee.com/freevian/quick-app-maker/raw/main/bootstrap/entry.ps1' -OutFile $entry
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry
```

这三行就是唯一入口。Agent 不现场编写下载器、launcher、安装器或 clone 脚本。

## 一键脚本会做什么

```text
检查 Git
→ 缺少 Git 时从 npmmirror 下载并静默安装
→ 立即 clone Gitee 仓库
→ 调用仓库内 bootstrap/install.ps1
→ 检查 .NET SDK、WinAppCLI、WinUI 模板
→ 只下载和安装缺少的部分
→ 输出 BOOTSTRAP_READY
→ 交接 vainreef-fast-publish Skill
```

### 第一阶段：Git 和仓库

`bootstrap/entry.ps1` 负责：

1. 查找已安装的 Git。
2. Git 缺失时下载固定版本 `2.47.1.windows.1`。
3. 静默安装 Git 并读取真实安装器退出码。
4. 使用 `C:\Program Files\Git\cmd\git.exe` 的绝对路径 clone。
5. clone 的标准输出和错误输出分别写入日志，`Cloning into ...` 只作为进度信息。
6. 已有仓库时执行 `pull --ff-only`。
7. clone 完成后调用仓库里的工具链安装器。

### 第二阶段：缺什么安装什么

`bootstrap/install.ps1` 会先检测：

```text
.NET SDK 10.0.400
WinAppCLI 0.6.1
Microsoft.WindowsAppSDK.WinUI.CSharp.Templates 0.0.6-alpha
```

已存在的组件直接跳过。缺失组件按下面方式处理：

```text
WinAppCLI：直接安装仓库内 toolchain/winapp-cli/0.6.1/winappcli_x64.msix
.NET SDK：从 Microsoft CDN 下载
WinUI 模板：从 NuGet 下载固定 nupkg
```

.NET SDK 和 WinUI 模板下载并行启动；仓库内 WinAppCLI 同时安装。.NET SDK 下载结束后立即启动安装；.NET 就绪后立即安装 WinUI 模板。

版本、文件名和下载地址统一读取 `bootstrap/toolchain.json`；Agent 不在运行时选择其他版本。

## 固定版本和来源

| 组件 | 固定版本 | 来源 |
| --- | --- | --- |
| Git for Windows | `2.47.1.windows.1` | `registry.npmmirror.com` |
| .NET SDK x64 | `10.0.400` | `download.microsoft.com` |
| WinUI C# templates | `0.0.6-alpha` | `api.nuget.org` |
| WinAppCLI x64 | `0.6.1` | Gitee 仓库固定副本 |

初始化过程只使用：

```text
Gitee
npmmirror
Microsoft CDN
NuGet
```

WinAppCLI 没有外部下载任务，也不经过其他代码托管站点或 winget。

## 仓库内安装脚本

```text
bootstrap/
├── entry.ps1                         # 安装 Git、clone、启动仓库安装器
├── install.ps1                       # 检查并安装缺少的组件
├── toolchain.json                    # 固定版本、文件名和来源
└── workers/
    ├── download-file.ps1             # 单文件下载 worker
    ├── install-dotnet.ps1            # .NET SDK 安装 worker
    ├── install-winappcli.ps1         # 仓库内 WinAppCLI 安装 worker
    └── install-winui-template.ps1    # WinUI 模板安装 worker
```

这些脚本是流程唯一实现。Agent 只执行脚本，不在临时目录重新生成同名逻辑。

## 进度和日志

一键安装会持续输出：

```text
[START] 正在下载/安装什么
[RUNNING] 当前任务和已下载大小
[OK] 已完成的组件
[FAIL] 错误和日志位置
```

缓存和日志位于：

```text
%LOCALAPPDATA%\Vainreef\QuickAppMaker\
```

正常完成后不生成 `bootstrap-report.md`。出现问题时读取对应日志，修复后重新运行同一个入口；已安装组件会自动跳过。

## 已知坑和固定处理方式

### README 地址

- 默认分支是 `main`。
- raw 地址使用 `/raw/main/...`。
- `/raw/master/...` 返回 404 时直接改用 `main`。

### Git 和 PATH

- Git 安装后，不依赖新终端刷新 PATH。
- clone 和后续 Git 命令始终使用 Git 的绝对路径。

### clone 日志

- Git 会把 `Cloning into ...` 写入 stderr，这属于正常进度。
- clone 是否成功只看 Git 退出码和目标目录中的 `.git`。

### .NET 安装

- 使用 `Start-Process -PassThru -Wait` 获取真实安装器退出码。
- 安装器进程结束后再执行 `dotnet --list-sdks`。
- 发现 `10.0.400` 后才进入 WinUI 模板安装。
- 安装过程中不重复启动第二个 .NET 安装器。

### WinAppCLI 安装

- 只使用仓库内 MSIX。
- `Add-AppxPackage` 完成后通过 `Get-AppxPackage -Name winapp` 确认版本。

### WinUI 模板安装

- 直接执行仓库 worker，不使用 PowerShell 5.1 的 `2>&1 + ErrorActionPreference=Stop` 组合。
- `dotnet new list winui` 出现 item template 需要项目上下文的提示属于正常信息。
- 只要列表中存在 `winui-navview`，模板即已就绪。

## Bootstrap 的停止线

工具链初始化阶段只准备环境。下面这些动作由真实 App 流程负责：

```text
创建 Hello World
创建临时测试项目
dotnet run
Developer Mode
dotnet publish
winapp pack
MSIX 安装测试
Microsoft Store 登录和提交
```

一键安装完成时只输出：

```text
BOOTSTRAP_READY
NEXT_ACTION: read skills/vainreef-fast-publish/SKILL.md and start discovery
```

## 接下来立即进入 Skill

Agent 读取：

```text
skills/vainreef-fast-publish/SKILL.md
skills/vainreef-fast-publish/references/discovery-interview.md
```

然后向用户提问：

```text
你想做一个什么样的 App？可以先随意描述你脑中的画面、玩法或感觉。
```

用户给出第一段实质想法后，Skill 才创建真实 App 目录和 living README。用户确认完整需求后，才进入工程生成、运行、打包和 Store 流程。

## 项目结构

```text
.
├── README.md
├── bootstrap/                      # 一键初始化实现
├── skills/vainreef-fast-publish/  # 需求发现、工程、打包和 Store Skill
├── toolchain/                      # 仓库内固定工具
├── templates/                      # Golden Template
├── apps/                           # 真实测试 Build 归档
└── docs/                           # 研究资料
```

Fast Publish 的技术契约、需求边界、依赖 Registry 和 Store 流程都在 `skills/vainreef-fast-publish/` 内维护；根 README 只承担新电脑初始化手册职责。
