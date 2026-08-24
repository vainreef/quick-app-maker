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
$smokeRoot = Join-Path $env:LOCALAPPDATA 'Vainreef\QuickAppMaker\smoke'
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
winapp package ./publish --generate-cert --install-cert --publisher "CN=AppPublisher" --output ./store-package/App_1.0.0.0_x64.msix
# 后续重打包：复用同一个 pfx（推荐，避免每次换证书导致信任失效）
winapp package ./publish --cert .\<Identity>_cert.pfx --cert-password password --output ./store-package/App_1.0.0.0_x64.msix
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

## 7. Store Automation Pipeline (Store 0 ~ 7)

使用仓库内置的声明式 Edge Store CLI：

```powershell
# 1. 语法与配置校验
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Validate-EdgeStoreCli.ps1 -Strict

# 2. Store 0 离线静态质检 (MSIX 清单/设备依赖/物料尺寸)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action preflight -Manifest .\<app>\build\edge-store.json

# 3. 启动隔离 Edge 会话
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action launch -Manifest .\<app>\build\edge-store.json -KeepOpen

# 4. 单表填报或全流程收敛 (含 F5 刷新二次验证)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase all -Manifest .\<app>\build\edge-store.json -Apply -ReloadVerify -KeepOpen

# 5. 显式双确认提交审核
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 -Action run -Phase all -Manifest .\<app>\build\edge-store.json -Apply -Submit -ConfirmSubmit
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
