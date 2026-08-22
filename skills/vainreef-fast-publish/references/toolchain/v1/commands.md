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

### 1. 禁止把 `dotnet run` 挂在后台等待（最严重，6 次卡死）

`dotnet run` 在 winapp 集成下会启动打包应用，然后 **dotnet 进程一直活着直到应用退出**。如果命令结束时应用还活着：

- 执行器的 stdout/stderr 管道被进程树握住，永不返回 → 工具调用挂起 10 分钟以上
- **timeout 参数在这种场景下不可靠，不会自动触发**
- 只能靠用户手动打断

**正确姿势（三选一）：**

```powershell
# 姿势 A：命令结尾必须杀干净进程再返回（两条都杀）
$p = Start-Process dotnet -ArgumentList "run" -PassThru -RedirectStandardOutput f1 -RedirectStandardError f2
Start-Sleep -Seconds 15
Get-Process -Name <AppName> -ErrorAction SilentlyContinue | Select-Object Id, Responding
Get-Process -Name <AppName> | Stop-Process -Force          # 杀 app
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue # 杀 dotnet
```

```powershell
# 姿势 B：不经过 dotnet run，直接启动已注册的打包应用（推荐，最快）
explorer.exe "shell:AppsFolder\<PFN>!App"
```

```powershell
# 姿势 C：直接运行构建产物 exe（只验证进程存活用）
$exe = Join-Path (Get-AppxPackage -Name <Identity>).InstallLocation '<AppName>.exe'
Start-Process $exe; Start-Sleep -Seconds 8
Get-Process -Name <AppName> | Stop-Process -Force
```

**验证**：如果应用启动即崩溃（进程树自己退出），命令会正常返回——所以"命令正常返回"不等于"应用正常"；要用 `Get-Process` 查存活 + `Get-WinEvent` 查崩溃记录来区分。

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
winapp ui search "应用" -a <AppName>            # 按文本找元素
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

### 第一轮坑点（应用 App，13 条）

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

### 第二轮坑点（应用 App，新增）

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
| 1 ApplicationData | **否，两轮矛盾** | 真因未明；不要传播为通用规则 |
| 13 数据路径/LocalState 清空 | 是（工具链行为） | 本机验证 |
| 10 运行验证姿势 | 是 | 工具层 |

## 会话级流程坑（每轮必做，违反即失败）

- **实机上修改的任何文档（commands.md、windows-smoke-test.md、SKILL.md、run-report）必须在本轮结束前 git add/commit/push 回 Gitee。** 三轮实测：第一轮经验没推回、第二轮基于旧基线又重踩 0x80073CFB、两轮之间零提交——知识闭环连续三次断裂。推送是硬性收尾步骤
- 会话全程日志保存到 `apps/<slug>/session-<id>.md`
- 新发现的 Windows 行为经过复现后，写回 `commands.md`（用下面的格式）

```markdown
#### 短标题
- Environment:
- Command:
- Error text:
- Root cause:
- Fix:
- Retest result:
```