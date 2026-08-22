# Windows Smoke Test

这份手册用于真实 Windows 机器测试整条链路。每次测试都以实际输出为准；发现稳定坑点后，把结论同步到 `skills/vainreef-fast-publish/references/toolchain/v1/commands.md`。

## Test record

第一轮实测记录（2026-08-22，轮次 1 App）：

| Field | Value |
| --- | --- |
| Date | 2026-08-22 |
| Tester | Agent (Build · DeepSeek V4 Flash) |
| Repository commit | 60194b8 (注意：实机经验未推回，见 commands.md 会话级流程坑) |
| Windows edition | Windows 10 Enterprise 2009 |
| Windows build | 26100 |
| Architecture | x64 |
| Fresh machine / existing tools | 全新 Windows 实机，Bootstrap 从零安装 |
| Result | 链路全通（创建→Debug 运行→Release→publish→MSIX→安装→启动→卸载→重装），遇到 13 个坑已全部解决并写入 commands.md |

- 测试会话：`apps/important-reminders/session-1.md`
- 应用运行报告：`apps/important-reminders/build/run-report.md`
- 结论：模板 0.0.6-alpha + WinAppSDK 2.4.0 链路可用；ApplicationData 原生崩溃、调试身份冲突、PublishTrimmed、通知注册位置等坑见 commands.md「Confirmed Windows findings」
- 未执行：第 8 步重复 Bootstrap（本次未验证二次执行耗时）

第二轮实测记录（2026-08-22，轮次 2 App）：

| Field | Value |
| --- | --- |
| Date | 2026-08-22 |
| Tester | Agent (DeepSeek V4 Flash → Ox Alpha) |
| Repository commit | 60194b8（仍未推回；第二轮基于旧基线开发，重踩了 0x80073CFB） |
| Windows edition | Windows 10 Enterprise 2009 |
| Windows build | 26100 |
| Architecture | x64 |
| Fresh machine / existing tools | 已有工具链，直接从工程阶段开始 |
| Result | 链路全通，最终交付 v1.0.1.0 MSIX。新增 10 个坑点。但出现 6 次命令卡死（dotnet run 挂在后台导致），需用户手动打断；新增坑与卡死机制已全部写入 commands.md |

- 测试会话：`apps/<repo>/session-2.md`
- 应用运行报告：`apps/<repo>/quick-app-maker/<app-slug>/build/run-report.md`
- 第二轮关键新发现：`dotnet run` 后台挂起机制（命令执行硬规则 1，7 次卡死：-PassThru 无效、重定向无效、开头杀 app 不够、中断残留污染下一条命令、判别律"命令返回⇔进程退出"）、StartupProbe ModuleInitializer 定位手段、`winapp ui` UI 自动化、`dotnet run` 清空 LocalState、Developer Mode 注册表、0x8007139F 孤儿 titlebar 调用
- 第二轮过程审计（2026-08-22 补充）：同一套设计被完整重推 4 次（L11-2526 占日志 39%），决策反复 6+ 次，API 行为纯猜不实测（ScheduledToast 时限 6 轮、MicaBackdrop 11 轮、崩溃原因 12 回合）；通知三层状态机与双路径存储 fallback 属过度工程；设计纪律与 MVP-first 规则已写入 SKILL.md 第 5 节
- 未执行：第 8 步重复 Bootstrap

## 1. Run the public entry

在 Windows PowerShell 中执行：

```powershell
$entry = Join-Path $env:TEMP 'vainreef-quick-app-maker-entry.ps1'
Invoke-WebRequest -UseBasicParsing -Uri 'https://gitee.com/freevian/quick-app-maker/raw/main/bootstrap/entry.ps1' -OutFile $entry
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry
```

记录：

- 最终是否出现 `BOOTSTRAP_READY`。
- 输出的 `Git executable` 实际路径。
- 总耗时。
- 下载和安装日志目录。
- 是否出现管理员确认。
- 发生错误时的原文和日志路径。

## 2. Inspect installed tools

```powershell
Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsBuildNumber, OsArchitecture
Get-Command git.exe | Select-Object Source
git --version
dotnet --list-sdks
dotnet new list winui
Get-AppxPackage -Name winapp | Select-Object Name, Version, InstallLocation
winapp --version
winapp --help
```

记录输出：

```text
Git path:
Git version:
.NET SDK:
WinUI template:
WinAppCLI package version:
winapp --version:
```

## 3. Create a disposable WinUI project

```powershell
$smokeRoot = Join-Path $env:LOCALAPPDATA 'Vainreef\QuickAppMaker\smoke'
New-Item -ItemType Directory -Force -Path $smokeRoot | Out-Null
Set-Location $smokeRoot

$appName = "VainreefSmokeApp$(Get-Date -Format 'yyyyMMddHHmmss')"
dotnet new winui-navview -n $appName
Set-Location $appName
```

记录：

- 创建命令退出码。
- 实际生成的 `.csproj` 路径。
- `Package.appxmanifest` 路径。
- 模板输出中的 warning / error。
- 工程目录结构。

## 4. Build and run

```powershell
dotnet build
$debugBuildExit = $LASTEXITCODE

dotnet run
$runExit = $LASTEXITCODE

dotnet build -c Release
$releaseBuildExit = $LASTEXITCODE
```

注意：`dotnet run` 会阻塞当前会话直到 App 退出（winapp 集成）。人工检查时窗口关闭后命令才会返回；自动化会话不要用它做后台启动（见 commands.md「命令执行硬规则 1」——7 次卡死教训，正确姿势是 `explorer shell:AppsFolder\<PFN>!App` 或结尾杀进程）。

人工检查：

- 窗口是否打开。
- 导航页是否显示。
- 窗口是否正常关闭。
- 第二次启动是否正常。
- Debug 和 Release 的退出码。

## 5. Publish

```powershell
dotnet publish -c Release -r win-x64 -o ./publish
$publishExit = $LASTEXITCODE
Get-ChildItem ./publish -Recurse | Select-Object FullName, Length
```

记录 Publish 输出目录、文件数量、总大小、warning 和退出码。

## 6. Inspect and run the packaging command

先保存当前工具帮助：

```powershell
winapp package --help | Tee-Object -FilePath ./winapp-pack-help.txt
```

然后试运行当前候选命令：

```powershell
winapp package ./publish --generate-cert --install-cert
$packExit = $LASTEXITCODE
```

根据真实输出记录：

- 最终采用的命令。
- MSIX 绝对路径。
- 证书路径。
- 管理员确认情况。
- warning / error。
- 退出码。

## 7. Install, launch, uninstall, reinstall

使用 `winapp package --help` 和打包输出给出的实际安装方式完成：

1. 首次安装。
2. 从开始菜单启动。
3. 关闭 App。
4. 卸载。
5. 使用同一包重新安装。
6. 再次启动。

记录每一步的命令、用户界面提示和结果。

## 8. Repeat Bootstrap

重新执行第 1 步，检查：

- 已安装 Git 是否被发现并复用。
- .NET、WinUI template 和 WinAppCLI 是否被跳过。
- Bootstrap 第二次执行耗时。
- 缓存和日志是否保持清楚。

## 9. Result summary

```text
BOOTSTRAP_READY:
Create project:
Debug build:
Run:
Release build:
Publish:
Pack:
Install:
Launch:
Uninstall:
Reinstall:
Second bootstrap:
```

## 10. Feed findings back

每个真实问题按下面格式写入 `commands.md`：

```markdown
### Short issue title

- Environment:
- Command:
- Exit code:
- Error text:
- Root cause:
- Fix:
- Retest result:
```

只写已经复现并重新测试过的结论。
