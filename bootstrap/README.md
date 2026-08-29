# Bootstrap 实现手册（给维护者和排障用）

本文档说明一键初始化脚本的实现细节。Agent 入口和操作步骤见根目录 `README.md`；这里是 bootstrap 实现与故障处理手册。

## 一键脚本做什么

```text
检查工作区 MinGit 或全局 Git
→ 缺少时从 npmmirror 下载 MinGit 绿色免安装包并解压 (零 UAC 提权)
→ 立即 clone Gitee 仓库
→ 调用仓库内 bootstrap/install.ps1
→ 检查 .NET SDK、WinAppCLI、WinUI 模板
→ 只下载和安装缺少的部分
→ 输出 BOOTSTRAP_READY
→ 交接 vainreef-fast-publish Skill
```

## 第一阶段：Git 和仓库

`bootstrap/entry.ps1` 负责：

1. 查找工作区根目录 `git\cmd\git.exe`（与 `quick-app-maker` 同级）或已安装的 Git。
2. Git 缺失时从 npmmirror 下载固定版本绿色免安装包 `MinGit-2.47.1-64-bit.zip`。
3. 静默解压到工作区根目录 `git\`，无需管理员提权，彻底杜绝 UAC 弹窗。
4. 使用解压后的免安装版绝对路径进行 clone。
5. clone 的标准输出和错误输出分别写入日志，`Cloning into ...` 只作为进度信息。
6. 已有仓库时执行 `pull --ff-only`。
7. clone 完成后调用仓库里的工具链安装器。

## 第二阶段：缺什么安装什么

`bootstrap/install.ps1` 会先检测：

```text
.NET SDK 10.0.400 (工作区 dotnet\ 或全局)
WinAppCLI 0.6.1
Microsoft.WindowsAppSDK.WinUI.CSharp.Templates 0.0.6-alpha
```

已存在的组件直接跳过。缺失组件按下面方式处理：

```text
WinAppCLI：直接安装仓库内 toolchain/winapp-cli/0.6.1/winappcli_x64.msix
.NET SDK：从官方构建 CDN 下载 zip 绿色压缩包并解压至工作区 dotnet\ (零 UAC, 零写C盘)
WinUI 模板：从 NuGet 下载固定 nupkg
```

.NET SDK 和 WinUI 模板下载并行启动；仓库内 WinAppCLI 同时安装。.NET SDK 下载结束后立即启动解压；.NET 就绪后立即安装 WinUI 模板。

Bootstrap 的版本、文件名和下载地址统一读取 `bootstrap/toolchain.json`。这里是安装阶段唯一的版本来源，仓库不再维护重复的 `version-lock.md`。

## 固定版本和来源

| 组件 | 固定版本 | 来源 |
| --- | --- | --- |
| MinGit (免安装) | `2.47.1.windows.1` | `registry.npmmirror.com` |
| .NET SDK x64 (免安装) | `10.0.400` | `builds.dotnet.microsoft.com` |
| WinUI C# templates | `0.0.6-alpha` | `nuget.azure.cn` |
| WinAppCLI x64 | `0.6.1` | Gitee 仓库固定副本 |

初始化过程只使用：

```text
Gitee
npmmirror
Microsoft 官方构建 CDN
Azure China CDN (nuget.azure.cn)
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

缓存和日志严格位于工作根目录内的本地缓存文件夹：

```text
<WORKSPACE_ROOT>\.cache\
```

正常完成后不生成 `bootstrap-report.md`。出现问题时读取对应日志，修复后重新运行同一个入口；已安装组件会自动跳过。

## 已知坑和固定处理方式

### README 地址

- 默认分支是 `main`。
- raw 地址使用 `/raw/main/...`。
- `/raw/master/...` 返回 404 时直接改用 `main`。

### Git 和 PATH
- 优先查找当前工作区根目录 `git\cmd\git.exe`（与 `quick-app-maker` 同级）绿色免安装版。
- 缺少时直接下载 `MinGit-2.47.1-64-bit.zip` 并解压到工作区根目录 `git\`，不依赖全局 PATH，不触发 UAC 提权弹窗。
- 安装/解压后输出 `Git executable: ...` 并传给后续流程。
- 全程所有 git 命令统一使用同级 `.\git\cmd\git.exe` 免安装版本。

### clone 日志

- Git 会把 `Cloning into ...` 写入 stderr，这属于正常进度。
- clone 是否成功只看 Git 退出码和目标目录中的 `.git`。

### .NET 免安装解压与挂载
- 下载官方 Binaries 绿色包 `dotnet-sdk-10.0.400-win-x64.zip`。
- 直接解压至工作区根目录 `dotnet\`（`Project\dotnet`），通过 `$env:DOTNET_ROOT` 挂载使用，绝不运行任何 EXE 安装包，不写 `C:\Program Files`，零 UAC 提权。
- 解压后执行 `$dotnetExe --list-sdks` 验证版本。
- 发现 `10.0.400` 后才进入 WinUI 模板安装。

### WinAppCLI 安装

- 只使用仓库内 MSIX。
- `Add-AppxPackage` 完成后通过 `Get-AppxPackage -Name winapp` 确认版本。

### WinUI 模板安装

- 直接执行仓库 worker，不使用 PowerShell 5.1 的 `2>&1 + ErrorActionPreference=Stop` 组合。
- 安装和模板列表查询都通过独立 `Start-Process` 捕获 stdout、stderr 和 exit code。
- `dotnet new list winui` 出现 item template 需要项目上下文的提示属于正常信息。
- 只要列表中存在 `winui-navview`，模板即已就绪。

### PowerShell 5.1 原生命令

- Git clone 使用独立进程的 stdout/stderr 重定向；`Cloning into ...` 只作为进度。
- .NET、WinAppCLI、WinUI 每个安装动作都由仓库 worker 写入自己的日志和退出码。
- 控制器读取状态文件，不通过 `$LASTEXITCODE` 猜测 `Start-Process` 的结果。

### 下载和缓存

- 正常路径直接使用已有缓存，不在每次执行前计算 SHA-256。
- 下载写入 `.part`，下载进程成功后再移动到正式文件名。
- 安装或包读取失败时，脚本记录失败文件的大小和 SHA-256。
- 外部缓存文件在失败后删除、重新下载并自动重试一次。
- SHA-256 在这里用于失败诊断和对比，不是正常启动前的固定关卡。