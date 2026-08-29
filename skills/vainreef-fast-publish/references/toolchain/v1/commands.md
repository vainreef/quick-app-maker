# Windows Toolchain v1 Commands

- Status: `windows-verified-nine-rounds`
- Release selector: `bootstrap/toolchain.json -> release`
- Target: `win-x64`

本页只收录实际要执行的命令和实测结果。版本数字读取 `bootstrap/toolchain.json`。

## 1. Inspect the environment

```powershell
Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsBuildNumber, OsArchitecture
git --version
dotnet --list-sdks
dotnet new list winui
Get-AppxPackage -Name winapp | Select-Object Name, Version, InstallLocation
winapp --version
winapp --help
# Developer Mode（打包运行 dotnet run 必需，见坑点 15）
Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
```

记录每条命令的退出码：

```powershell
$LASTEXITCODE
```

## 2. Create a disposable project

```powershell
$smokeRoot = Join-Path (Get-Location).Path 'smoke-app'
New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null
Set-Location $smokeRoot

$appName = 'VainreefSmokeApp'
dotnet new winui-navview -n $appName
Set-Location $appName
```

创建后先查看真实工程结构：

```powershell
Get-ChildItem -Force
Get-ChildItem -Recurse -Filter *.csproj
Get-ChildItem -Recurse -Filter Package.appxmanifest
```

## 3. Build and run

```powershell
dotnet build
dotnet build -c Release
```

运行验证的硬规则见下方「命令执行硬规则」——不要直接 `dotnet run` 挂在后台等待，它会阻塞整个会话。

验证判别律：**命令不返回 = 应用进程还活着**；**命令正常返回 ≠ 应用正常**（返回快可能是应用秒崩）。任何一次运行验证都必须 `Get-Process` 查存活 + `Get-WinEvent` 查崩溃记录双确认。工具被手动打断后，下一条命令先清残留进程（见硬规则 1）。

## 4. Publish

```powershell
dotnet publish -c Release -r win-x64 -o ./publish
```

发布后记录：

```powershell
Get-ChildItem ./publish -Recurse |
    Select-Object FullName, Length |
    Out-File ./publish-files.txt
```

## 5. Package for local testing

已实测（WinAppCLI 0.6.1）：

```powershell
# 首次打包：生成开发证书并安装到本机
winapp package ./publish --executable <AppName>.exe --generate-cert --install-cert --publisher "CN=AppPublisher" --output ./store-package/App_1.0.0.0_x64.msix
# 后续重打包：复用同一个 pfx（推荐，避免每次换证书导致信任失效）
winapp package ./publish --executable <AppName>.exe --cert .\<Identity>_cert.pfx --cert-password password --output ./store-package/App_1.0.0.0_x64.msix
```

要点：

- 命令是 `winapp package`。参数是 `--output <完整文件路径>`（坑 42），不支持短参数 `-o`。
- `--generate-cert` 每次都会生成新 pfx 并覆盖同名旧文件 → 旧的信任记录失效，安装会报 0x800B0109。**固定保留一个 pfx，之后一律 `--cert <pfx> --cert-password password` 复用。**
- `--install-cert` 在当前用户信任区（`Cert:\CurrentUser\TrustedPeople`）完成，严禁操作 `LocalMachine` 杜绝提权弹窗（坑 40）。

## 6. Install, launch, uninstall, reinstall

```powershell
# 证书导入必须显式密码（winapp 默认密码是 password）
$pwd = ConvertTo-SecureString -String "password" -Force -AsPlainText
Import-PfxCertificate -FilePath .\<Identity>_cert.pfx -CertStoreLocation Cert:\CurrentUser\TrustedPeople -Password $pwd

Add-AppxPackage -Path .\<Identity>_<version>_<arch>.msix
Get-AppxPackage -Name <Identity> | Select-Object Version, Status

# 启动已安装应用（不阻塞会话）
explorer.exe "shell:AppsFolder\<PFN>!App"
```

要点：

- **同版本号禁止覆盖安装（0x80073CFB）**：改代码后重装要么先卸载旧包，要么递增 manifest 的 `Version`。
- 从安装目录直接启动 exe 也能验证（runFullTrust），但用 `shell:AppsFolder` 更接近用户真实启动路径。

## 7. Store Automation Pipeline (Store 0 ~ 10 多阶段离散执行与 DOM 自检)

使用仓库内置的声明式 Edge Store CLI，**严格按阶段独立推进，每步必须检查 DOM 自检验收证据**：

```powershell
# 阶段 0: 离线静态质检 (MSIX 清单/设备依赖/物料尺寸/字数限制)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action preflight -Manifest .\<app>\build\edge-store.json

# 阶段 1: 启动独立常驻 Edge 会话并在桌面确权
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action launch -Manifest .\<app>\build\edge-store.json -KeepOpen

# 阶段 2: 动态探测 active submissionId 与 6 大表单实时 href 并建立 DOM 基线
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action discover -Manifest .\<app>\build\edge-store.json

# 阶段 3: 定价与可用性离散步进 + DOM 探针自检
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase availability -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen

# 阶段 4: 应用属性离散步进 + DOM 探针自检
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase properties -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen

# 阶段 5: 年龄分级问卷离散步进 + DOM 探针自检
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase ageRatings -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen

# 阶段 6: 程序包上传与云端 Validated 轮询 + DOM 探针自检
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase packages -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen

# 阶段 7: 应用商店一览 (文本/特性/关键词/截图/Logo) + DOM 探针自检
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase listing -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen

# 阶段 8: 提交选项 (发布时机/runFullTrust) + DOM 探针自检
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action step -Phase options -Manifest .\<app>\build\edge-store.json -Apply -KeepOpen

# 阶段 9: 概览页 6 大模块冷加载全绿勾总检
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action verify -Manifest .\<app>\build\edge-store.json

# 阶段 10: 显式双确认提交审核
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action submit -ConfirmSubmit -Manifest .\<app>\build\edge-store.json
```

---

## Confirmed Windows Findings & Pitfalls (坑点 1 ~ 53)

#### 42. winapp package 没有 -o 参数，输出必须用 --output <完整文件路径>
- Environment: WinAppCLI 0.6.1
- Command: `winapp package ./publish --self-contained --executable App.exe -o ./store-package`
- Error text: `Input folder(s) not found: <cwd>/-o`
- Root cause: `-o` 不是合法短选项，被当成输入目录解析
- Fix: 必须使用 `--output ./store-package/<name>_<version>_<arch>.msix`

#### 43. 包内 DisplayName 必须与 Partner Center 预留的产品名称一致
- Environment: Partner Center 上传 MSIX
- 现象: 若包内 `<DisplayName>` 与预留名称不一致，提审认证会被阻断或警告
- Fix: 提交前确保 `Package.appxmanifest` 的 `Properties/DisplayName` 与 `VisualElements/DisplayName` 与控制台预留名完全一致

#### 44. 模板清单声明 Windows.Universal 导致设备家族矩阵全列 rank 1
- Environment: WinUI 模板生成清单
- 现象: 上传后设备系列出现 Mobile, Xbox, Team, Mixed Reality 全部 rank 1 报错
- Root cause: 模板 `<Dependencies>` 同时声明 `Windows.Universal` 与 `Windows.Desktop`
- Fix: 必须删除 `Windows.Universal` 依赖，仅保留 `<TargetDeviceFamily Name="Windows.Desktop" .../>`

#### 45. runFullTrust 权限说明区分：程序包警告 vs 提交选项必填
- Environment: Partner Center 提审
- 现象: 以为 runFullTrust 全程无需理会，结果在「提交选项」页面被阻断无法保存
- Root cause: 程序包页面的警告是桌面应用常态（可忽略），但「提交选项」页面出现的“为何需要使用 runFullTrust”文本框是必填项（最多 500 字）
- Fix: 在提交选项表单中自动填报 500 字以内合规用途说明

#### 46. Partner Center 概览页必须先「开始提交」才显示 6 大表单
- Environment: Partner Center 控制台
- 现象: 进入产品页找不到 6 大表单
- Fix: 在概览页点击「开始提交 (Start Submission)」，6 大模块才会出现，且 DOM 包含动态真实 href

#### 47. UTF-8 无 BOM 的 .ps1 在中文 Windows (GBK) 环境下破坏语法
- Environment: PowerShell 5.1，代码页 936
- 现象: 中文字符的 UTF-8 字节被 GBK 误读，吞掉后续 ASCII 字符导致语法报错
- Fix: 脚本文件保存为 UTF-8 带 BOM，且 JS 内部中文统一使用 `\uXXXX` 转义

#### 48. $pid 变量名与 PowerShell 只读自动变量 $PID 冲突
- Environment: PowerShell 5.1
- 现象: `Cannot overwrite variable PID because it is read-only or constant`，导致进程无法复用
- Fix: 脚本中严禁使用 `$pid`，改用 `$savedPid`

#### 49. .GetAwaiter().GetResult() 的 VoidTaskResult 混入函数输出流
- Environment: PowerShell 5.1 异步调用
- 现象: 函数返回数组 `[{}, {"id": 3}]`，导致后续属性解构崩溃
- Fix: 裸调用末尾强制加 `| Out-Null` 或 `[void]` 包裹

#### 50. CDP WebSocket 单帧可能粘包多条 JSON
- Environment: Edge DevTools Protocol
- 现象: 一次 ReceiveAsync 收到事件和响应两条 JSON，直接 ConvertFrom-Json 抛错
- Fix: 采用深度花括号解析器 `Split-CdpMessages` 逐条拆分，跳过无 id 事件直到匹配目标 id

#### 51. Angular 自定义组件（he-select）必须采用 CDP 原生鼠标点击
- Environment: Partner Center 定价与表单页
- 现象: 用 DOM `setAttribute` 改值后页面显示 0 但保存按钮始终 disabled
- Root cause: Angular 不响应纯 DOM 属性篡改
- Fix: 通过 `getBoundingClientRect()` 动态获取几何中心，派发 `Input.dispatchMouseEvent` 原生物理点击

#### 52. JS 表达式内中文字符经 PowerShell 传递会被破坏
- Environment: PowerShell 5.1 + CDP Runtime.evaluate
- 现象: 含中文的表达式报 SyntaxError
- Fix: 传给 CDP 的 JavaScript 中文一律 `\uXXXX` 转义，单引号包裹

#### 53. 提交草稿重建后 submissionId 改变
- Environment: Partner Center 提审
- 现象: 硬编码 submissionId 导致 URL 404
- Fix: 每次自动化运行均从产品概览页 DOM 动态探测最新有效 submissionId 与表单 href

#### 54. dotnet publish 默认不拷贝 Assets 导致 MSIX 缺图验证失败
- Environment: WinUI 3 打包与 Partner Center 上传
- 现象: 商店接受验证报错：无法找到 appxManifest.xml 中指定的图像 Assets\StoreLogo.png
- Root cause: Content 项在发布时默认不进入 publish 根目录
- Fix: 打包前强制执行 `Copy-Item -Recurse -Force ./Assets ./publish/`

#### 55. 概览页已通过模块无文字徽标，未完成模块显示「未启动」
- Environment: 新版 Partner Center 应用程序概述
- 现象: OverviewAdapter 一直寻找 Complete/完成 关键字，最终报 Unknown 死锁
- Root cause: 新版已通过模块 `<app-module-status>` 主机为空；未完成模块显式包含 `<he-badge>未启动</he-badge>`
- Fix: 无徽标即判定为 Complete；含「未启动」判定为 Incomplete

#### 56. 属性页 Privacy = "No" 绝无隐私文本输入框
- Environment: Partner Center 属性表单
- 现象: 选“否”后尝试填写 `#privacyPolicyText` 抛异常
- Root cause: 选“否”时页面根本不渲染文本框，只有选“是”并点击「提供隐私策略文本」单选框后才动态插入 textarea
- Fix: Privacy = "No" 时严禁计划或填写任何文本框

#### 57. 复选框禁止使用模糊文本包含匹配
- Environment: Partner Center 属性与表单
- 现象: `windows` 匹配到 6 个复选框导致歧义报错
- Root cause: 页面存在多个包含 "windows" 文本的复选框
- Fix: 优先采用 `name` 属性（如 `'windows-checkbox'`、`'storage-checkbox'`）精准定位

#### 58. 文件上传 input 隐藏于 Shadow DOM 内
- Environment: Partner Center 程序包与资产上传
- 现象: 常规 querySelector 找不到 `input[type=file]`
- Root cause: 文件输入控件被 Web Components 的 shadowRoot 深度封装且不可见
- Fix: 使用 `Runtime.evaluate` 获取 `RemoteObjectId`，通过 `DOM.describeNode` + `DOM.setFileInputFiles` 绑定

#### 59. 程序包多行冲突导致保存按钮被禁用
- Environment: Partner Center 程序包表单
- 现象: 上传后 Save 按钮长期处于 disabled=true，或处于 Analyzing 已暂停状态
- Root cause: 存在历史残留的 Analyzing 或 Error 包行
- Fix: 清理所有错误/重复行，仅保留唯一 Validated 行，并冷加载刷新页面使 Save 按钮激活

#### 60. F5 刷新 (Page.reload) 导致 SPA 丢失路由变成空白壳
- Environment: Partner Center 二次持久化验证
- 现象: F5 后页面变成只有搜索框的空壳或跳转到登录骨架屏
- Fix: 使用显式 URL 导航冷加载（`Page.navigate -> location.href`）

#### 61. 声明式自动化变更需先更新 Desired State JSON
- Environment: Agent 交互与 CLI 执行
- 现象: 用户要求修改线上配置，但 Agent 直接运行 CLI 导致 0 差异空转退出
- Root cause: CLI 依据本地 JSON 与页面比对，未修改 JSON 时差异为 0
- Fix: 用户提出新需求时，必须首先修改本地 `edge-store.json` 文件再运行收敛

#### 62. AI 执行器私有 Win32 Desktop (`WinSta0\exebox-*`) 隔离导致直接 UAC 提权失败 (`ERROR_NOT_SUPPORTED 0x80070032`)
- Environment: AI Agent 运行环境 / Windows 10/11 Win32 USER32 Desktop 对象模型
- 现象: Agent 在终端执行 `Start-Process -Verb RunAs` 请求管理员提权时，立即抛出异常 `0x80070032 (ERROR_NOT_SUPPORTED)` 或无任何反应，无法唤起 UAC 确认框。
- Root cause:
  1. AI Runner 与用户的 Explorer 处于同一个物理登录会话（`Session 1`）和同一个交互式窗口站（`WinSta0`），但 AI 执行线程被放进了独立的私有 Win32 Desktop 对象（`WinSta0\exebox-XXXX`），而用户操作的桌面是 `WinSta0\Default`，UAC 安全桌面是 `WinSta0\Winlogon`。
  2. Windows USER32 安全机制规定不同 Desktop 间 GUI 与 Shell 上下文严格隔离。从非活动私有 Desktop 直接发起 `ShellExecuteEx("runas")` 时，Windows Shell / AppInfo 服务会出于防欺骗安全机制直接拒绝并返回 `ERROR_NOT_SUPPORTED`。
  3. 普通文件读写、编译、网络、管道等因为完全共享内核/Token，100% 正常；仅跨 Desktop 的交互式提权路径受阻。
- Fix（遇到此问题怎么办）:
  1. **首选方案（治本 - 零提权免安装架构）**：
     - Git 和 .NET 10 SDK 全程采用绿色免安装版，解压至工作区根目录（`Project\git\` 与 `Project\dotnet\`）；
     - 证书只导入 `Cert:\CurrentUser\TrustedPeople`，包管理使用用户级 `Add-AppxPackage`，全流程 100% 在 Medium Integrity 下运行，根本不需要提权。
  2. **兜底方案（若必须提权 - 跨 Desktop 桥接）**：
     - **严禁要求用户关闭 UAC、关闭 Defender 或修改 `EnableLUA=0`！**
     - 使用底层 Win32 API `CreateProcess` 显式指定 `STARTUPINFO.lpDesktop = @"WinSta0\Default"` 启动非提权中间进程至用户桌面，再由该进程调用 `ShellExecuteEx("runas")`，系统即可正常切入 `WinSta0\Winlogon` 安全桌面弹出原生 UAC 确认框。

#### 63. PowerShell `Start-Process -Wait` 调用 `dotnet build` 因 MSBuild 节点重用句柄继承导致进程假死
- Environment: PowerShell 5.1 / Windows 10/11 / .NET SDK 10 MSBuild
- 现象: 执行脚本调用 `Start-Process -FilePath dotnet -ArgumentList "build ... -c Release" -NoNewWindow -PassThru -Wait` 时，控制台显示 `Build succeeded. Time Elapsed 00:00:44.13`，编译早已 100% 成功，但脚本却永久卡住不向下继续执行。
- Root cause:
  1. .NET MSBuild 默认开启了编译服务器节点重用机制（Node Reuse / `VBCSCompiler.exe` 后台守护进程）；
  2. 当通过 `Start-Process -NoNewWindow -Wait` 启动时，子进程的 stdout/stderr 标准管道句柄被驻留后台的 MSBuild worker 进程继承并保持打开状态；
  3. PowerShell 的 `-Wait` 阻塞机制等待所有管道句柄彻底关闭，因此即使主 `dotnet` 进程早已退出，整个会话依然被永久卡死。
- Fix:
  1. 避免使用 `Start-Process ... -NoNewWindow -Wait` 来调用编译器；改为在 PowerShell 中直接调用操作符 `& $dotnet.Source build $projectPath -c Release --nologo -v q /p:UseSharedCompilation=false`；
  2. 添加 `/p:UseSharedCompilation=false` 参数彻底禁止 MSBuild 派生驻留后台的共享编译服务器进程，消除句柄继承隐患。

#### 65. Windows 自动化浏览器进程与终端生命周期强绑定导致关终端即闪退（Job Object 诛连清理与 Task Scheduler 系统外壳脱钩机制）
- Environment: Windows 10/11 / PowerShell / Edge CDP 自动化提审
- 现象: 脚本启动 Edge 浏览器后，只要用户手动关闭终端、取消任务或脚本正常退出，屏幕上的 Edge 浏览器窗口就会瞬间连带关闭闪退，无法保留窗口查看或登录。
- Root cause:
  1. Windows 终端与任务运行器默认将派生子进程纳入同一个 Job Object，并配置了 `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`；
  2. 终端或命令退出时，Windows 内核发送 `TerminateJobObject` 强行株连杀死整个进程树；
  3. 受限 Job Object 下调用 `CREATE_BREAKAWAY_FROM_JOB` 会被系统拒绝返回 `0x5 (ERROR_ACCESS_DENIED)`。
- Fix:
  1. 使用 Windows 任务计划程序（`schtasks.exe /create ...` + `schtasks.exe /run ...`）由系统服务 `svchost.exe` 脱钩启动 Edge；
  2. 结合 `STARTUPINFO.lpDesktop = @"WinSta0\Default"` + `SW_SHOWMAXIMIZED` 确保窗口在前台主屏幕最大化呈现；
  3. 自动化驱动仅通过 CDP 端口（`http://127.0.0.1:58567`）热插拔连接，实现关终端、关 IDE 浏览器永远常驻不闪退。

#### 68. .NET 10 System.Text.Json 默认非 ASCII 字符 Unicode 转义导致输出 `\uXXXX` 乱码感
- Environment: .NET 10 CLI 控制台输出 / 中文 Windows
- 现象: 所有控制台输出和快照中的中文按钮和标题被转义为 `\u5220\u9664`、`\u63D0\u4EA4` 等不可读字符串。
- Root cause: `System.Text.Json` 默认启用了严格的 `JavaScriptEncoder.Default`（仅允许基本 ASCII 字符）。
- Fix: 在 `JsonSerializerOptions` 中显式指定宽松非转义编码器：`Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping`。

#### 69. 概览页模块状态与子页面中间态混淆假收敛（包提交阶段“云端 Validated”不等于“模块完成”，必须退回概览页 DOM 呈现“完成”为唯一真值）
- Environment: Microsoft Partner Center 商店提审 / 程序包与 6 大模块表单
- 现象: 自动化脚本检测到 MSIX 在子页面表格显示 `Validated` 便误判为该阶段完成；但实际由于重复行冲突或未点击保存草稿，概览页上该模块依然是「未完成」甚至「未启动」。
- Root cause: 混淆了“云端二进制包体静态分析通过（Package Validated）”与“该表单在概览草稿中成功持久化（Module Complete）”。
- Fix:
  1. 阶段前置探测：进入任何阶段前，必须先在概览页 DOM 读取真实基线；
  2. 阶段后置闭环验收：离开任何阶段后，必须冷加载导航回概览页，DOM 探针切实检测到该模块显示为「完成」（且无「未完成」/「未启动」等异常徽标），才允许放行并进入下一阶段！

---

## 适用范围（对"大多数 Win11 电脑"的成立性）

| 条目 | 跨机器硬规则 | 备注 |
| --- | --- | --- |
| 42 winapp package --output 语法 | 是 | WinAppCLI 参数规范 |
| 43 DisplayName 预留名对齐 | 是 | Store 准入规则 |
| 44 Windows.Desktop 独占依赖 | 是 | MSIX 设备家族规则 |
| 45 runFullTrust 提交选项必填 | 是 | Store 提审表单规则 |
| 46 概览页动态 href 探测 | 是 | Partner Center 前端架构 |
| 47 UTF-8 带 BOM 编码 | 是 | PowerShell 5.1 GBK 行为 |
| 48 $savedPid 命名安全 | 是 | PowerShell 变量作用域规则 |
| 49 VoidTaskResult 截断 | 是 | PowerShell 管道流机制 |
| 50 CDP 粘包分帧解析 | 是 | DevTools Protocol 传输层规则 |
| 51 he-select 原生物理点击 | 是 | Angular 响应式表单机制 |
| 52 JS 中文 Unicode 转义 | 是 | CDP 参数传递编码规则 |
| 53 动态 Submission ID 发现 | 是 | Partner Center 草稿生命周期 |
| 54 Assets 图标必须显式拷贝 | 是 | MSIX 静态资源规则 |
| 55 概览页无徽标即完成 | 是 | Partner Center SPA 最新架构 |
| 56 隐私 No/Yes 动态表单联动 | 是 | Partner Center 属性表单逻辑 |
| 57 复选框 name 属性精准定位 | 是 | LitElement 控件规则 |
| 58 Shadow DOM ObjectId 上传 | 是 | CDP Web Components 上传规范 |
| 59 冲突包清理与冷刷新激活 | 是 | Partner Center 上传提审规则 |
| 60 显式导航替代 F5 冷加载 | 是 | SPA 路由生命周期规则 |
| 61 变更配置前置更新 JSON | 是 | 声明式状态收敛铁律 |
| 62 Win32 Desktop 隔离与提权桥接 | 是 | Windows USER32 桌面安全模型 |
| 63 MSBuild 节点重用句柄阻塞假死 | 是 | .NET CLI / PowerShell 管道与进程模型 |
| 65 Task Scheduler 浏览器脱钩常驻 | 是 | Windows 任务计划与 Job Object 进程生命周期解耦 |
| 68 JSON 中文非转义宽松编码 | 是 | .NET 10 System.Text.Json 跨平台输出规范 |
| 69 概览页 DOM 绿勾唯一真值闭环 | 是 | Partner Center 阶段验收与状态收敛铁律 |

