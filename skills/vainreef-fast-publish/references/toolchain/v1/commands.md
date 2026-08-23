# Windows Toolchain v1 Commands

- Status: `windows-verified-two-rounds`
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

已实测（两轮，WinAppCLI 0.6.1）：

```powershell
# 首次打包：生成开发证书并安装到本机
winapp package ./publish --generate-cert --install-cert --publisher "CN=AppPublisher"
# 后续重打包：复用同一个 pfx（推荐，避免每次换证书导致信任失效）
winapp package ./publish --cert .\<Identity>_cert.pfx --cert-password password
```

要点：

- 命令是 `winapp package`。第一轮记录 `pack` 是别名，第二轮实测认为不存在 `pack` 子命令——两轮结论冲突，以 `winapp package --help` 当前输出为准，不要赌别名。
- `--generate-cert` 每次都会生成新 pfx 并覆盖同名旧文件 → 旧的信任记录失效，安装会报 0x800B0109。**固定保留一个 pfx，之后一律 `--cert <pfx> --cert-password password` 复用。**
- `--install-cert` 在管理员会话下静默完成；普通用户可能需要 UAC 确认。
- 输出 MSIX 命名：`<PackageIdentity>_<version>_<arch>.msix`，位于当前目录。

## 6. Install, launch, uninstall, reinstall

已实测（两轮）：

```powershell
# 证书导入必须显式密码（winapp 默认密码是 password）
$pwd = ConvertTo-SecureString -String "password" -Force -AsPlainText
Import-PfxCertificate -FilePath .\<Identity>_cert.pfx -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $pwd

Add-AppxPackage -Path .\<Identity>_<version>_<arch>.msix
Get-AppxPackage -Name <Identity> | Select-Object Version, Status

# 启动已安装应用（不阻塞会话）
explorer.exe "shell:AppsFolder\<PFN>!App"
```

要点：

- **同版本号禁止覆盖安装（0x80073CFB）**：改代码后重装要么先卸载调试包/旧包，要么升 manifest 的 `Version`（推荐升版本，如 1.0.0.0 → 1.0.1.0）。
- 从安装目录直接启动 exe 也能验证（runFullTrust），但用 `shell:AppsFolder` 更接近用户真实启动路径。
- 卸载不会删除应用自己写在 `%LOCALAPPDATA%` 下独立目录的数据。

## 7. Store command

下面的命令等待 Partner Center 实测确认：

```powershell
winapp store publish ./*.msix --appId APP_ID
```

执行前保存：

```powershell
winapp store --help
winapp store publish --help
```

## 命令执行硬规则（两轮血泪教训，违反必卡死）

这些是 Agent 在 Windows 实机上执行命令时最容易踩的坑，每条都造成过真实损失（累计卡死约 40 分钟、用户手动打断 6 次）。

### 1. 禁止把 `dotnet run` 挂在后台等待（最严重，7 次卡死）

`dotnet run` 在 winapp 集成下会启动打包应用，然后 **dotnet 进程一直活着直到应用退出**。如果命令结束时应用还活着：

- 执行器的 stdout/stderr 管道被进程树握住，永不返回 → 工具调用挂起 10 分钟以上
- **timeout 参数在这种场景下不可靠，不会自动触发**（7 次全部靠用户手动打断，无一次自动超时）
- 只能靠用户手动打断

**实测反直觉事实（别被表象骗了）：**

- **`-PassThru` 没有保护作用**：7 次卡死中 3 次带 -PassThru 照样卡（L3812/L4376/L4503 带，L4627/L4761/L4941 不带，结果相同）
- **重定向到文件也防不住**：7 次全部 `-RedirectStandardOutput/Error 到 $env:TEMP\*.txt` 仍卡——因为 `dotnet run` → app 整棵进程树都继承工具捕获管道句柄，与子进程 stdout 去向无关
- **只在命令开头杀 app、不杀 dotnet 照样卡**：实测命令开头杀了 `<AppName>`，但 dotnet 存活 → 仍卡死
- **纯 build 也会被残留污染**：L4814 一次不含 dotnet run 的纯 `dotnet build` 也卡死——上一命令被打断后残留的孤儿进程树（+obj 锁争用）污染了下一条命令
- **对照组定律**：命令正常返回 ⇔ 应用进程已退出（崩溃/被杀）；命令不返回 ⇔ 应用还活着。同构命令在应用秒崩时全部正常返回（L2970/L3305/L3557/L4289），应用存活时全部卡死

**正确姿势（三选一）：**

```powershell
# 姿势 A：命令结尾必须杀干净进程再返回（先 app 后 dotnet，杀完验证）
$p = Start-Process dotnet -ArgumentList "run" -PassThru -RedirectStandardOutput f1 -RedirectStandardError f2
Start-Sleep -Seconds 15
Get-Process -Name <AppName> -ErrorAction SilentlyContinue | Select-Object Id, Responding
Get-Process -Name <AppName> -ErrorAction SilentlyContinue | Stop-Process -Force          # 杀 app
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue                                # 杀 dotnet
Get-Process -Name <AppName>,dotnet -ErrorAction SilentlyContinue   # 必须无输出，否则工具不返回
```

```powershell
# 姿势 B：不经过 dotnet run，直接启动已注册的打包应用（推荐，实测切换后零复发）
explorer.exe "shell:AppsFolder\<PFN>!App"
```

```powershell
# 姿势 C：直接运行构建产物 exe（只验证进程存活用）
$exe = Join-Path (Get-AppxPackage -Name <Identity>).InstallLocation '<AppName>.exe'
Start-Process $exe; Start-Sleep -Seconds 8
Get-Process -Name <AppName> | Stop-Process -Force
```

**中断残留清理（工具被手动打断后必做）：**

```powershell
Get-Process -Name <AppName>,dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
```

**验证**：应用启动即崩溃时命令会正常返回——"命令正常返回"不等于"应用正常"，要用 `Get-Process` 查存活 + `Get-WinEvent` 查崩溃记录来区分。

### 2. 每个调用 dotnet 的命令必须显式传 workdir

工具调用 JSON 里没有 `workdir` 时，`dotnet build` 会跑在会话默认目录（无 csproj）→ MSB1003。第一轮曾误判为"管道导致"（实际是 workdir 缺失，两轮 10+ 样本相关性 100%）。`2>&1 | Select-String` 管道过滤本身完全正常。

注意：**workdir 参数不展开环境变量**，写 `"$env:TEMP\smoke"` 会直接报 NotFound；必须写绝对路径字面量 `C:\Users\<user>\AppData\Local\Temp\smoke`。

### 3. 大段脚本和长文本不要内联进命令/工具参数

多行 here-string（`@'...'@`）内联在命令里会被截断错乱；长 JSON 工具参数会被截断（`Unterminated string`）。两轮都栽过。

- 脚本先写入 `.ps1` 文件再 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File` 执行
- 长文件内容（XAML/C# 源码）拆分写入或分段 edit

### 4. 模型可能看不了图片——不要依赖截图验证

DeepSeek V4 Flash 等模型无视觉能力：read 工具读图返回 "Image read successfully" 但模型无法解读内容。截图验证是死路。**改用 `winapp ui` 做 UI 自动化验证**（已实测可用）：

```powershell
winapp ui search "<文本>" -a <AppName>            # 按文本找元素
winapp ui inspect "<selector>" -a <AppName>      # 读元素树
winapp ui invoke "<selector>" -a <AppName>       # 点按钮
winapp ui set-value "<selector>" "文本" -a <AppName>  # 填输入框
```

UI 自动化交互（点添加 → 填名字 → 点保存）已验证可完整驱动 WinUI 3 应用，是"无法点击 UI"问题的标准解。

### 5. PowerShell 5.1 控制台中文乱码是显示问题，不是文件问题

`Set-Content -Encoding UTF8`（带 BOM）和 `[IO.File]::WriteAllText` + UTF8 无 BOM 写出的文件都是正确的 UTF-8，System.Text.Json 都能读；控制台乱码只是代码页显示。不要为了"消除乱码"反复改写入方式（第二轮浪费三轮）。验证文件内容用 `Get-Content -Encoding UTF8` 或让应用自己读回来打印。

### 6. 避免长时间 Start-Sleep 串联

单条命令里 sleep 13-25 秒 × 连续多轮，会给用户"卡死"的观感。把"启动 → 等 → 读日志"合并成一次命令，并且**每一条都必须符合规则 1 的收尾清理**。

## Confirmed Windows findings

### 实测环境（两轮共同）

Windows 10 Enterprise 2009 build 26100 x64（26100 实为 Win11 24H2 版本号，产品名疑似镜像定制），.NET SDK 10.0.400，WinUI 模板 0.0.6-alpha（winui-navview），Microsoft.WindowsAppSDK 2.4.0，WinAppCLI 0.6.1，Administrator（提升）会话，winapp 调试身份。两轮整条链路均验证通过：创建 → Debug 运行 → Release → publish → MSIX → 安装 → 启动 → 重打包升版本。

### 第一轮坑点（轮次 1，13 条）

#### 1. WinRT ApplicationData 启动早期崩溃（两轮结论矛盾，真因未明）

- 第一轮：`ApplicationData.Current` 访问触发 stowed exception 0xc000027b（Microsoft.UI.Xaml.dll 固定偏移），try/catch 与 UnhandledException 均拦不住；换纯 .NET `%LOCALAPPDATA%` 路径后稳定 → 当时归因为"该环境原生崩溃"
- 第二轮：同一机器同样提升会话 + 调试身份，DayStore 首选 `ApplicationData.Current.LocalFolder` 运行完全正常（日志打出 store path = Packages\<PFN>\LocalState），**未复现崩溃**
- 结论：**第一轮归因很可能是又一次并发变量误判**（这台机器已出现三起同类误判：MSB1003、TitleBar、ApplicationData）。真因未明，可能与其他构建状态（WMC9999 产物）或调用时机有关。不要当作通用规则传播，也不要因为"怕崩"回避 ApplicationData
- 安全做法（无害兜底）：存储层 try/catch + 可降级到 `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` 自建目录；用 `%TEMP%` 分步日志定位崩溃点。崩溃日志本身写 `%TEMP%` 而非 ApplicationData

#### 2. 通知激活注册必须放在 Application 节点内

- Error: `0xC00CE014 根据父元素 Extensions 的内容模型，元素 desktop:Extension 为意外元素`
- Fix: `windows.toastNotificationActivation` 的 `desktop:Extension` 与 `com:Extension`（COM server）必须放在 `<Applications>/<Application>/<Extensions>` 内，不能放 Package 级；`com:ExeServer Executable="AppName.exe"` 只写文件名，`Arguments="----AppNotificationActivated:"`；ToastActivatorCLSID 与 com:Class Id 用同一个 GUID

#### 3. 管理员身份运行时通知被禁用

- WinAppSDK 源码 `IsSupported() = !IsElevated()`：提升进程不支持 App Notifications，toast 不会弹出（系统设计，跨机器成立）
- Fix: 应用内检测 `IsSupported()`，false 时显示日常语言提示。**本开发机是 Administrator 会话，永远测不出真实 toast 弹出**——只能验证"调度逻辑不报错"，真实弹窗要在非提升环境验证。不要花时间在这台机器上追求"看到 toast"

#### 4. 模板 PublishTrimmed 的两副面孔（两轮结论合并）

- `dotnet build -c Release`（无 RuntimeIdentifier，框架依赖）：报 `NETSDK1102 所选发布配置不支持优化程序集的大小` → 改 csproj `<PublishTrimmed>False</PublishTrimmed>`，或忽略（反正 publish 才是发布路径）
- `dotnet publish -c Release -r win-x64`（隐式自包含）：裁剪生效，反射式 System.Text.Json 报 `IL2026` 警告 → 用 `[JsonSerializable]` + `JsonSerializerContext` 源生成序列化消除
- 两轮分别遇到两种形态，都是模板 0.0.6-alpha 自带的 `PublishTrimmed=True` 引起

#### 5. 调试身份包与正式 MSIX 同身份冲突

- Error: `0x80073CFB 当前用户已安装该应用的未签名版本`（调试包身份与正式包相同）
- Fix: 装正式包前 `Get-AppxPackage -Name <identity> | Remove-AppxPackage`；或直接升版本号重打包（第二轮做法，更规范）

#### 6. XamlCompiler 级联内部错误 = 先修 C# 编译错误

- Error: `WMC1509: No LocalAssembly parameter given during MarkupCompilePass2` + `error WMC9999: 未将对象引用设置到对象的实例`
- Fix: 修 C# 错误（两轮实例：删掉 `using Microsoft.UI.Xaml.Controls;` 导致 TitleBar 找不到；Color 缺 `using Windows.UI;`）。看到 WMC9999 先找 CS 编译错误，不要对着内部错误排查

#### 7. WMC1506 警告是良性的，不要追

- `WMC1506: OneWay bindings require at least one of their steps to support raising notifications` = 模型无 INPC，x:Bind 退化为一次性绑定。列表每次整体重建时界面自然更新，不需要修

#### 8. workdir 缺失导致 MSB1003（真因，管道是巧合）

- 三轮结论合并：MSB1003 ⇔ 工具调用 JSON 无 workdir（10+ 样本 100% 相关）。管道 `2>&1 | Select-String` 无害。见「命令执行硬规则 2」

#### 9. 长文本/脚本截断

- 见「命令执行硬规则 3」。教训加一条：Agent 思考里声称"传了 workdir/写了内容"，以工具调用记录为准，不要凭记忆核对参数

#### 10. 运行验证与崩溃定位的标准姿势

- 见「命令执行硬规则 1」和「崩溃定位」小节（下方）

#### 11. TitleBar：危险在孤儿 code-behind 调用（两轮合并修正）

- 第二轮实证：删掉模板 TitleBar 的 XAML，但 code-behind 里残留 `AppWindow.TitleBar.PreferredHeightOption = Tall` → 启动即崩 COMException **0x8007139F**（"组或资源的状态不是执行请求操作的正确状态"），因为该属性只在 `ExtendsContentIntoTitleBar=true` 时可用
- **规则：改动模板结构时，全局搜索 code-behind 里对已删控件的孤儿调用**
- `TitleBar` 类型来自 `Microsoft.UI.Xaml.Controls`，code-behind 引用需 `using Microsoft.UI.Xaml.Controls;`
- 第一轮"TitleBar XAML 改了会崩"是错误归因（真因是 ApplicationData 疑案）；第二轮证明真正的坑是孤儿代码行

#### 12. C# 字符串里写中文引号

- `$"..."` 里直接写英文双引号会截断字符串编译失败 → 中文文案一律用「」或转义

#### 13. 测试数据注入

- 打包应用的数据目录是 `%LOCALAPPDATA%\Packages\<PFN>\LocalState\`，不是 `%LOCALAPPDATA%\<AppName>\`
- **`dotnet run` 每次重注册调试身份会清空该包 LocalState**——调试期写入的数据下次 run 就没了；持久化/提醒验证要在真实安装（Add-AppxPackage）后做，或让应用自己报告存储路径（StartupProbe 打点）
- 中文测试数据用 JSON Unicode 转义（`\uXXXX`）写入，彻底避开编码问题
- 验证方式：`winapp ui` 驱动 UI 添加数据 → 检查 days.json；或预置数据 → 启动 → 检查 LastReminded 等字段是否被写回

### 第二轮坑点（轮次 2，新增）

#### 14. 启动崩溃的定位标准手段：ModuleInitializer 启动探针

- 症状：应用启动即崩，crash.log 没写入（原生 stowed exception 0xc000027b 或 CLR 0xe0434352 都发生在托管 handler 之前）
- 手段：新建 `StartupProbe.cs`，用 `[ModuleInitializer]` 从 .NET 最早期入口开始逐阶段打点（module init → App ctor → InitializeComponent → OnLaunched → MainWindow created → Activated），一次运行拿到精确堆栈
- 本轮用此法 2 分钟定位到第 11 条的 0x8007139F 孤儿调用（此前盲猜 MicaBackdrop/toast/XAML 资源 30 分钟无果）
- 这是崩溃定位的第一选择，比加 UnhandledException 快得多

#### 15. Developer Mode 需要注册表开启

- `dotnet run` 打包运行要求 Developer Mode；实测注册表直接开启即可，无需重启：
  `HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock` 设 `AllowDevelopmentWithoutDevLicense=1`
- 注意：这是机器级系统变更，改前应告知用户

#### 16. 同版本重装 0x80073CFB 的正解是升版本

- 第二轮改代码后同版本（1.0.0.0）装不上 → 升 manifest `Version` 到 1.0.1.0 重新 publish+package+install，一次通过
- 版本号是迭代的正确手段，不要每次都卸载

#### 17. 证书信任链（0x800B0109）

- `--generate-cert` 每次生成新证书；不带 `--install-cert` 时新证书未入信任区，安装报 `0x800B0109 根证书不受信`
- 正解：固定一个 pfx，后续 `--cert <pfx> --cert-password password` 复用（见第 5 节）

#### 18. Import-PfxCertificate 需要 -Password

- 不带 `-Password` 报"需要密码"；密码是 `password`（winapp 默认）。见第 6 节

#### 19. 嵌套 ContentDialog 是地雷

- 编辑对话框打开时点击"删除"会再弹确认框 → WinUI 禁止同时显示两个 ContentDialog，必抛异常
- 设计交互时：先关闭当前对话框，再开下一个；或用单一对话框内切换内容

#### 20. 静默 catch 会吞掉通知故障

- 两轮的通知链路都用了 `catch {}` 静默吞异常，导致"没提醒"时无任何日志可查（第二轮 days.json 的 LastReminded 始终不写，排查半天才发现是测试数据路径错误 + 调度静默失败）
- 规则：catch 里至少写一条 StartupProbe/日志；开发期不要用空 catch

#### 21. winapp ui 是可用的 UI 自动化工具

- `winapp ui search/inspect/invoke/set-value/get-value` 可驱动 WinUI 3 应用（已实测完成添加流程）。见「命令执行硬规则 4」

### 第三轮坑点（轮次 3，新增）

#### 22. 非自包含 MSIX 安装报 0x80073CF3（依赖 WindowsAppRuntime 框架包缺失）

- Environment: Windows build 26100，WinAppSDK 2.4.0，winapp 0.6.1
- Command: `Add-AppxPackage -Path <pkg>.msix`（首次 `winapp package ./publish --generate-cert --install-cert` 产物）
- Error text: `0x80073CF3 无法注册包，因为以下依赖项缺失: Microsoft.WindowsAppRuntime.2 ... 2.4.0.0`
- Root cause: 普通打包产物带框架包依赖，目标机器未装 WindowsAppRuntime 框架包（前两轮同机开发已装过故未暴露）
- Fix: `winapp package ... --self-contained`（把 WinAppSDK 运行时打进包）
- Retest result: 安装成功

#### 23. 自包含包启动即崩 0x80070490（DeploymentManager AutoInitialize 找不到框架包）

- Environment: 同上，csproj 未设 WindowsAppSDKSelfContained
- Command: 启动自包含 MSIX 安装后的应用
- Error text: `.NET Runtime 事件: COMException 0x80070490 找不到元素，at ABI...DeploymentManagerCS.AutoInitialize.AccessWindowsAppSDK()`，模块 WinRT.Runtime
- Root cause: `winapp --self-contained` 只负责打包时捆绑运行时；编译期 auto-initializer 仍会去找已注册的框架包，自包含下不存在 → 崩溃在 Application.Start 阶段（ModuleInitializer 探针可定位：module-init 之后、App ctor 之前）
- Fix: csproj 加 `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`，编译期排除 auto-initializer
- Retest result: 启动正常

#### 24. 自包含 publish 输出含多个 exe，winapp package 报歧义

- Environment: 同上
- Command: `winapp package ./publish --self-contained --cert ...`
- Error text: `Failed to create MSIX package: The manifest contains a placeholder for the executable but multiple .exe files were found in the input folder`
- Root cause: 自包含构建后 publish 目录含运行时 bootstrap exe 和主程序 exe
- Fix: `winapp package ... --executable <AppName>.exe`
- Retest result: 打包成功

#### 25. CalendarDatePicker 无法用 winapp ui set-value 赋值

- Environment: WinUI 3
- Command: `winapp ui set-value "btn-xxx" "2026/8/30" -a <App>`
- Error text: `Element (Button) could not be set via ValuePattern...`
- Root cause: 日期选择器按钮不支持 ValuePattern 程序化赋值
- Fix: 用默认日期（今天+N 天）走通流程，或 `winapp ui send-keys --verbatim "<date>" --target <sel> --via send-input`（需前台）
- Retest result: 默认日期方案流程全通

### 第四轮坑点（轮次 4，新增）

#### 26. WinUI 3 的 Window 没有 Resources 属性

- Error text: `XamlCompiler error WMC0011: Unknown member 'Resources' on element 'Window'`
- Root cause: WinUI 3 的 `Window` 不是 FrameworkElement，不支持 `Window.Resources`（WPF 才有）
- Fix: 全局资源（如渐变画刷）放 `App.xaml` 的 `Application.Resources`
- Retest result: 构建通过

#### 27. RectangleGeometry 没有 RadiusX/RadiusY

- Error text: `XamlCompiler error WMC0011: Unknown member 'RadiusX' on element 'RectangleGeometry'`
- Root cause: WinUI 3 的 `RectangleGeometry` 只有 `Rect`（圆角是 WPF 专属）
- Fix: 圆角图片用 `ImageBrush` 作为 `Border.Background`（背景按 Border 圆角裁剪，天然圆角）；图片缺失时背景用渐变占位
- Retest result: 图片正常显示且无崩溃

#### 28. Window 没有 Loaded 事件

- Error text: `CS0103: 当前上下文中不存在名称 "Loaded"`
- Root cause: WinUI 3 的 `Window` 只有 `Activated`
- Fix: `Activated += ...` + 一次性初始化标志位
- Retest result: 构建通过，首次激活时初始化数据

#### 29. AppNotificationManager.IsSupported() 是静态方法

- Error text: `CS0176: 无法使用实例引用来访问成员 "AppNotificationManager.IsSupported()"`
- Fix: 直接 `AppNotificationManager.IsSupported()`，不要用 `Default.IsSupported()`
- Retest result: 编译通过，运行时在提升会话返回 false（与坑 3 一致）

#### 30. Manifest 的 ToastActivatorCLSID / com:Class Id 不能带花括号

- Command: `winapp package ./publish --self-contained ...`
- Error text: `error C00CE169: App manifest validation error ... '{GUID}' 不符合 '[0-9a-fA-F]{8}-...' 模式`
- Root cause: AppX manifest schema 要求 GUID 不带 `{}` 花括号
- Fix: 去掉花括号
- Retest result: 打包成功

#### 31. PowerShell 5.1 执行含中文的 .ps1 必须 UTF-8 BOM

- Command: `powershell.exe -File seed.ps1`（文件含中文字符串）
- Error text: `字符串缺少终止符` / ParserError
- Root cause: PS 5.1 无 BOM 时按 ANSI 代码页读取 .ps1，中文多字节把引号吃掉了
- Fix: 用 `[IO.File]::WriteAllText(path, content, [Text.UTF8Encoding]::new($true))` 加 BOM 重写
- Retest result: 脚本正常执行
- 注意: 这是"脚本文件编码"坑，与坑 13 的"写入数据编码"不同

#### 32. FontIcon 内容的按钮没有自动化名称

- Command: `winapp ui search "编辑"`（按钮 Content 是 FontIcon glyph）
- Error text: Found 0 matches
- Root cause: 图标按钮的自动化名称为空，ToolTipService.ToolTip 不成为可搜索名
- Fix: XAML 加 `AutomationProperties.Name="编辑"`（同时利于无障碍）
- Retest result: `winapp ui search/invoke` 可定位并点击

### 第五轮坑点（轮次 5，新增）

#### 33. WinAppSDK 2.4.0 的 AppNotifications 投影没有计划通知 API

- Environment: WinAppSDK 2.4.0，.NET 10，WinUI 模板 0.0.6-alpha，Windows build 26100
- Command: `dotnet build`（引用 `AppNotificationScheduledNotification` / `AddToSchedule`）
- Error text: `CS0246 未找到类型 AppNotificationScheduledNotification`；`CS1061 AppNotificationManager 未包含 AddToSchedule`
- Root cause: 该版本 `Microsoft.WindowsAppSDK.AppNotifications.Projection.dll` 中不存在计划通知类型（Unicode 字符串扫描 0 匹配确认）
- Fix: 改用 Windows Runtime 原生底层 `Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier()` + `ScheduledToastNotification`（打包应用带包身份，100% 可用；构造 XML 用 `XmlDocument`，文本特殊字符需转义）
- Retest result: 调度成功（日志确认 `scheduled 1 reminders`），无异常

#### 34. ItemsControl 未挂 ItemTemplate 时渲染裸类型名

- Environment: WinUI 3
- Command: `winapp ui inspect`
- Error text: 卡片区域文本直接显示 `RememberWhat.Models.DayItemViewModel`
- Root cause: 忘写 `ItemTemplate="{StaticResource ...}"`，ItemsControl 默认直接调用数据项的 `ToString()`
- Fix: 显式挂载 `ItemTemplate`
- Retest result: 卡片视图正常显示

#### 35. 打包应用 LocalApplicationData 被系统重定向

- Environment: MSIX 安装的 WinUI 3 应用
- Command: 应用内写调试日志到 `%LOCALAPPDATA%\<AppName>\log.txt`
- Error text: 文件未在预期绝对路径生成
- Root cause: 带包身份的应用 `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` 被系统虚拟化重定向到 `Packages\<PFN>\LocalCache\Local`
- Fix: 状态日志写到工作根目录 `<app-slug>/logs/`（符合 SKILL 工作目录规则 4）
- Retest result: 日志正常落盘且易于测试验证

#### 36. winapp ui 列表操作中重复文本匹配歧义

- Environment: winapp ui 自动化测试
- Command: `winapp ui invoke "删除"`
- Error text: `winapp.exe : Selector matched 6 elements`
- Root cause: 列表多张卡片均有"删除"按钮，按纯文本匹配存在多元素歧义
- Fix: 先用 `winapp ui search` 或 `inspect` 定位具体卡片，获取唯一 UID（如 `btn-684d`）进行精准触发
- Retest result: 自动化点击精准执行

### 已验证可复用的组合（轮次 4 & 5）

- 数据预置法：应用关闭时直接写 `LocalState\days.json`（中文用 JSON Unicode 转义）+ 复制图片到 `LocalState\images\`，重启应用即可验证图片显示、提醒调度、过期清理，全程无需碰文件选择器
- 升级重装（manifest Version 递增）后 LocalState 数据保留，可在真实安装环境下做持久化验证
- ContentDialog 子类（XAML 根元素为 ContentDialog）是添加/编辑表单的干净形态，`winapp ui` 可完整驱动：set-value 填文本框 → invoke PrimaryButton 保存
- 删除确认对话框与编辑对话框错开使用，规避嵌套 ContentDialog 崩溃（坑 19）
- 计划通知调度：采用 `ToastNotificationManager.CreateToastNotifier().AddToSchedule(new ScheduledToastNotification(...))`，在管理员提权会话下检测无崩溃、日志正常打点，非提权环境正常弹出通知
- 单页 Window + Grid.Resources DataTemplate + x:Bind：模板内 Button Click 事件绑定窗口 code-behind 正常

### 崩溃定位流程（推荐顺序）

```text
1. StartupProbe [ModuleInitializer] 逐阶段打点（首选，一次定位）
2. Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Application Error'; StartTime=(Get-Date).AddMinutes(-10)} 看 faulting module 与异常码
3. WER 目录 C:\ProgramData\Microsoft\Windows\WER\ReportArchive 同名目录的 Report.wer
4. 二分定位：最小 TestPage 隔离"页面内容"与"代码逻辑"
5. 原生 stowed exception（0xc000027b）不会被 UnhandledException 捕获，不要依赖 crash.log 方案
```

## 适用范围（对"大多数 Win11 电脑"的成立性）

| 条目 | 跨机器硬规则 | 备注 |
| --- | --- | --- |
| 2 通知激活注册位置 | 是 | MSIX schema 校验 |
| 3 管理员通知禁用 | 是 | 系统设计；仅提升进程触发 |
| 4 PublishTrimmed | 是 | 模板属性 + .NET 行为 |
| 5 调试身份包冲突 | 是 | 该工具链行为 |
| 6 WMC9999 级联 | 是 | XamlCompiler 行为 |
| 7 WMC1506 良性 | 是 | 同上 |
| 8 workdir 硬规则 | 是 | 工具层面，与 Windows 无关 |
| 9 长文本截断 | 是 | 工具层面 |
| 11 TitleBar 孤儿调用 | 是 | WinUI 3 框架行为 |
| 12 中文引号 | 是 | C# 编译规则 |
| 14 StartupProbe | 是 | 方法学 |
| 15 Developer Mode | 是 | Windows 打包运行要求 |
| 16 同版本重装 | 是 | AppX 部署规则 |
| 17/18 证书 | 是 | winapp 工具行为 |
| 19 嵌套 ContentDialog | 是 | WinUI 框架行为 |
| 20 静默 catch | 是 | 工程实践 |
| 21 winapp ui | 是 | 工具能力 |
| 22 框架包依赖 0x80073CF3 | 是 | MSIX 部署规则；换新机器装包必遇 |
| 23 自包含 AutoInit 0x80070490 | 是 | WinAppSDK 编译期行为 |
| 24 多 exe 歧义 | 是 | winapp 工具行为 |
| 25 CalendarDatePicker set-value | 是 | WinUI 控件 UIA 行为 |
| 26-28 Window 无 Resources/Loaded、RectGeometry 无圆角 | 是 | WinUI 3 框架 API 差异 |
| 29 IsSupported 静态方法 | 是 | WinAppSDK API 签名 |
| 30 GUID 不带花括号 | 是 | AppX manifest schema |
| 31 .ps1 中文需 BOM | 是 | PowerShell 5.1 行为 |
| 32 AutomationProperties.Name | 是 | UIA 规范 |
| 33 WinAppSDK 计划通知缺失 | 是 | 2.4.0 投影行为，回退 ScheduledToastNotification |
| 34 ItemsControl 裸类型名 | 是 | XAML 框架默认行为 |
| 35 LocalAppData 虚拟化重定向 | 是 | MSIX 安全沙箱行为 |
| 36 winapp ui 列表多元素歧义 | 是 | UIA 选择器行为，需用唯一 UID |
| 1 ApplicationData | **否，两轮矛盾** | 真因未明；不要传播为通用规则 |
| 13 数据路径/LocalState 清空 | 是（工具链行为） | 本机验证 |
| 10 运行验证姿势 | 是 | 工具层 |

## 会话级流程规则

- **Agent 不做 git 写操作**：不 add、不 commit、不 push（无凭据会弹窗打断用户，仓库是只读 skill）。每轮结束后把通用技术经验写到工作根目录 `round-notes/round-N.md`，由外部主控合并进仓库文档。
- 工作目录规则、仓库行尾符保护、LocalState 读取替代方案：见 SKILL.md「工作目录规则」（权威版，此处不重复）。
- 交付与共创规则：交付第一版是邀请用户试用与收集反馈的起点，严禁在首版主动推销上架；内部测试环境特例（提权通知限制）不得转嫁给用户，详见 SKILL.md「对用户的语言与心智规则」。
- 新发现的 Windows 行为经过复现后，先记入 `round-notes/`，由主控按下面格式并入本文件：

```markdown
#### 短标题
- Environment:
- Command:
- Error text:
- Root cause:
- Fix:
- Retest result:
```