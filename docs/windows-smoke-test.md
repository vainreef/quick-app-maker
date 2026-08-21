# Windows Smoke Test

这份手册用于真实 Windows 机器测试整条链路。每次测试都以实际输出为准；发现稳定坑点后，把结论同步到 `skills/vainreef-fast-publish/references/toolchain/v1/commands.md`。

## Test record

填写本次环境：

| Field | Value |
| --- | --- |
| Date | pending |
| Tester | pending |
| Repository commit | pending |
| Windows edition | pending |
| Windows build | pending |
| Architecture | pending |
| Fresh machine / existing tools | pending |
| Result | pending |

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
winapp pack --help | Tee-Object -FilePath ./winapp-pack-help.txt
```

然后试运行当前候选命令：

```powershell
winapp pack ./publish --generate-cert --install-cert
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

使用 `winapp pack --help` 和打包输出给出的实际安装方式完成：

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
