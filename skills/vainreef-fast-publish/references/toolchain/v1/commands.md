# Windows Toolchain v1 Commands

- Status: `awaiting-windows-smoke-test`
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
dotnet run
dotnet build -c Release
```

运行时记录：

- 第一次还原包需要多久。
- App 是否正常打开和关闭。
- Debug 与 Release 的退出码。
- stdout/stderr 中与 WinUI item template 相关的信息。

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

下面的参数等待当前 Windows 工具链 Smoke Test 确认：

```powershell
winapp pack ./publish --generate-cert --install-cert
```

执行前保存：

```powershell
winapp pack --help
```

记录最终命令、输出 MSIX 路径、是否弹出管理员确认、证书安装过程和退出码。

## 6. Install, launch, uninstall, reinstall

根据 `winapp pack --help` 的实测输出选择安装命令。记录：

- 包身份和版本。
- 安装是否成功。
- App 是否能从开始菜单启动。
- 卸载结果。
- 同一 MSIX 的重新安装结果。

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

## Confirmed Windows findings

### PowerShell worker process handling

- 原生命令通过独立 `Start-Process` 捕获 stdout、stderr 和退出码。
- Git 的 `Cloning into ...` 可能出现在 stderr；仓库状态和 Git 退出码用于判断结果。
- 目标 .NET SDK 就绪后再运行 `dotnet new list winui`。

### Download/cache handling

- 正常路径直接使用现有缓存。
- 安装或包读取失败后记录文件大小和 SHA-256。
- 失败的外部缓存文件会被移除、重新下载并重试一次。
- 下载始终先写入 `.part`，成功后再移动到正式文件名。

## Findings to add after the first Smoke Test

| Date | Windows build | Command | Result | Error / observation | Verified fix |
| --- | --- | --- | --- | --- | --- |
| pending | pending | pending | pending | pending | pending |
