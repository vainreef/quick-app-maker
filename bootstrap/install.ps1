[CmdletBinding()]
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$GitPath = ''
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:WINAPP_CLI_TELEMETRY_OPTOUT = '1'

$manifestPath = Join-Path $PSScriptRoot 'toolchain.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Toolchain manifest is missing: $manifestPath"
}
$toolchain = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

$toolchainVersion = $toolchain.release
$cacheRoot = Join-Path $env:LOCALAPPDATA "Vainreef\QuickAppMaker\cache\$toolchainVersion"
$logsRoot = Join-Path $cacheRoot 'logs'
$workersRoot = Join-Path $PSScriptRoot 'workers'

New-Item -ItemType Directory -Force -Path $cacheRoot, $logsRoot | Out-Null

$dotnetVersion = $toolchain.dotnet.version
$dotnetUrl = $toolchain.dotnet.url
$dotnetInstaller = Join-Path $cacheRoot $toolchain.dotnet.file

$templateVersion = $toolchain.winui_template.version
$templateUrl = $toolchain.winui_template.url
$templatePackage = Join-Path $cacheRoot $toolchain.winui_template.file

$winAppVersion = $toolchain.winappcli.version
$winAppPackageVersion = $toolchain.winappcli.package_version
$winAppPackage = Join-Path $RepoRoot ($toolchain.winappcli.repository_path -replace '/', '\')
$dotnetExe = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

function Write-Step {
    param([string]$Message)
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message"
}

function Resolve-GitPath {
    param([string]$PreferredPath)

    if ($PreferredPath -and (Test-Path -LiteralPath $PreferredPath)) {
        return (Resolve-Path -LiteralPath $PreferredPath).Path
    }

    $command = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @(
        'C:\Program Files\Git\cmd\git.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\Git\cmd\git.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

function Test-DotNetSdk {
    if (-not (Test-Path -LiteralPath $dotnetExe)) { return $false }
    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $sdks = (& $dotnetExe --list-sdks 2>$null | Out-String)
    $ErrorActionPreference = $previousErrorPreference
    return $sdks -match "(?m)^$([regex]::Escape($dotnetVersion))"
}

function Test-WinAppCli {
    $package = Get-AppxPackage -Name 'winapp' -ErrorAction SilentlyContinue
    return ($package -and $package.Version.ToString() -eq $winAppPackageVersion)
}

function Test-WinUiTemplate {
    if (-not (Test-DotNetSdk)) { return $false }
    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $templates = (& $dotnetExe new list winui 2>$null | Out-String)
    $ErrorActionPreference = $previousErrorPreference
    return $templates -match 'winui-navview'
}

function Start-ScriptProcess {
    param(
        [string]$ScriptPath,
        [string]$Arguments
    )
    $argumentLine = "-NoProfile -ExecutionPolicy Bypass -File `"$ScriptPath`" $Arguments"
    return Start-Process -FilePath 'powershell.exe' -ArgumentList $argumentLine -PassThru -WindowStyle Hidden
}

function Wait-ProcessWithFeedback {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Name,
        [string]$PartialPath = ''
    )

    while (-not $Process.HasExited) {
        if ($PartialPath -and (Test-Path -LiteralPath $PartialPath)) {
            $sizeMb = [math]::Round((Get-Item -LiteralPath $PartialPath).Length / 1MB, 1)
            Write-Step "$Name running: $sizeMb MB"
        }
        else {
            Write-Step "$Name running"
        }
        Start-Sleep -Seconds 2
        $Process.Refresh()
    }

    if ($Process.ExitCode -ne 0) {
        throw "$Name failed with exit code $($Process.ExitCode)"
    }
    Write-Step "$Name complete"
}

function Write-ArtifactFailureDiagnostic {
    param(
        [string]$Path,
        [string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path)) { return }

    try {
        $item = Get-Item -LiteralPath $Path
        $hash = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Step "$Name failed artifact: bytes=$($item.Length) sha256=$hash"
    }
    catch {
        Write-Step "$Name failed artifact could not be inspected: $($_.Exception.Message)"
    }
}

function Start-DownloadProcess {
    param(
        [string]$Id,
        [string]$Url,
        [string]$OutputPath,
        [string]$LogName
    )

    $downloadScript = Join-Path $workersRoot 'download-file.ps1'
    $arguments = "-Id `"$Id`" -Url `"$Url`" -OutputPath `"$OutputPath`" -LogPath `"$(Join-Path $logsRoot $LogName)`""
    return Start-ScriptProcess -ScriptPath $downloadScript -Arguments $arguments
}

$gitPath = Resolve-GitPath -PreferredPath $GitPath
if (-not $gitPath) {
    throw 'Git is missing. Run bootstrap/entry.ps1 first.'
}
if (-not (Test-Path -LiteralPath $winAppPackage)) {
    throw "WinAppCLI repository package is missing: $winAppPackage"
}

Write-Step "Repository ready: $RepoRoot"
Write-Step "Git executable: $gitPath"

$dotnetReady = Test-DotNetSdk
$winAppReady = Test-WinAppCli
$templateReady = Test-WinUiTemplate

if ($dotnetReady) { Write-Step ".NET SDK $dotnetVersion already installed" }
if ($winAppReady) { Write-Step "WinAppCLI $winAppVersion already installed" }
if ($templateReady) { Write-Step "WinUI template $templateVersion already installed" }

$dotnetDownloadProcess = $null
$templateDownloadProcess = $null
$winAppInstallProcess = $null

if (-not $dotnetReady -and -not (Test-Path -LiteralPath $dotnetInstaller)) {
    Write-Step 'Starting .NET SDK download'
    $dotnetDownloadProcess = Start-DownloadProcess -Id 'dotnet-sdk' -Url $dotnetUrl -OutputPath $dotnetInstaller -LogName 'dotnet-download.log'
}

if (-not $templateReady -and -not (Test-Path -LiteralPath $templatePackage)) {
    Write-Step 'Starting WinUI template download'
    $templateDownloadProcess = Start-DownloadProcess -Id 'winui-template' -Url $templateUrl -OutputPath $templatePackage -LogName 'winui-download.log'
}

if (-not $winAppReady) {
    Write-Step 'Starting WinAppCLI install from repository package'
    $script = Join-Path $workersRoot 'install-winappcli.ps1'
    $arguments = "-MsixPath `"$winAppPackage`" -LogPath `"$(Join-Path $logsRoot 'winapp-install.log')`""
    $winAppInstallProcess = Start-ScriptProcess -ScriptPath $script -Arguments $arguments
}

$dotnetInstallProcess = $null
if (-not $dotnetReady) {
    if ($dotnetDownloadProcess) {
        Wait-ProcessWithFeedback -Process $dotnetDownloadProcess -Name '.NET SDK download' -PartialPath "$dotnetInstaller.part"
    }
    Write-Step 'Starting .NET SDK install'
    $script = Join-Path $workersRoot 'install-dotnet.ps1'
    $arguments = "-InstallerPath `"$dotnetInstaller`" -LogPath `"$(Join-Path $logsRoot 'dotnet-install.log')`""
    $dotnetInstallProcess = Start-ScriptProcess -ScriptPath $script -Arguments $arguments
}

if ($templateDownloadProcess) {
    Wait-ProcessWithFeedback -Process $templateDownloadProcess -Name 'WinUI template download' -PartialPath "$templatePackage.part"
}

if ($dotnetInstallProcess) {
    try {
        Wait-ProcessWithFeedback -Process $dotnetInstallProcess -Name '.NET SDK install'
    }
    catch {
        Write-ArtifactFailureDiagnostic -Path $dotnetInstaller -Name '.NET SDK install'
        Remove-Item -LiteralPath $dotnetInstaller -Force -ErrorAction SilentlyContinue
        Write-Step '.NET SDK install failed; refreshing the cached installer and retrying once'
        $retryDownload = Start-DownloadProcess -Id 'dotnet-sdk-retry' -Url $dotnetUrl -OutputPath $dotnetInstaller -LogName 'dotnet-download-retry.log'
        Wait-ProcessWithFeedback -Process $retryDownload -Name '.NET SDK retry download' -PartialPath "$dotnetInstaller.part"
        $retryScript = Join-Path $workersRoot 'install-dotnet.ps1'
        $retryArguments = "-InstallerPath `"$dotnetInstaller`" -LogPath `"$(Join-Path $logsRoot 'dotnet-install-retry.log')`""
        $retryInstall = Start-ScriptProcess -ScriptPath $retryScript -Arguments $retryArguments
        Wait-ProcessWithFeedback -Process $retryInstall -Name '.NET SDK retry install'
    }
    if (-not (Test-DotNetSdk)) {
        throw ".NET SDK $dotnetVersion was not found after installation"
    }
    $dotnetReady = $true
}

$templateInstallProcess = $null
if (-not $templateReady) {
    Write-Step 'Starting WinUI template install'
    $script = Join-Path $workersRoot 'install-winui-template.ps1'
    $arguments = "-PackagePath `"$templatePackage`" -LogPath `"$(Join-Path $logsRoot 'winui-install.log')`""
    $templateInstallProcess = Start-ScriptProcess -ScriptPath $script -Arguments $arguments
}

if ($winAppInstallProcess) {
    try {
        Wait-ProcessWithFeedback -Process $winAppInstallProcess -Name 'WinAppCLI install'
    }
    catch {
        Write-ArtifactFailureDiagnostic -Path $winAppPackage -Name 'WinAppCLI install'
        throw
    }
}
if ($templateInstallProcess) {
    try {
        Wait-ProcessWithFeedback -Process $templateInstallProcess -Name 'WinUI template install'
    }
    catch {
        Write-ArtifactFailureDiagnostic -Path $templatePackage -Name 'WinUI template install'
        Remove-Item -LiteralPath $templatePackage -Force -ErrorAction SilentlyContinue
        Write-Step 'WinUI template install failed; refreshing the cached package and retrying once'
        $retryDownload = Start-DownloadProcess -Id 'winui-template-retry' -Url $templateUrl -OutputPath $templatePackage -LogName 'winui-download-retry.log'
        Wait-ProcessWithFeedback -Process $retryDownload -Name 'WinUI template retry download' -PartialPath "$templatePackage.part"
        $retryScript = Join-Path $workersRoot 'install-winui-template.ps1'
        $retryArguments = "-PackagePath `"$templatePackage`" -LogPath `"$(Join-Path $logsRoot 'winui-install-retry.log')`""
        $retryInstall = Start-ScriptProcess -ScriptPath $retryScript -Arguments $retryArguments
        Wait-ProcessWithFeedback -Process $retryInstall -Name 'WinUI template retry install'
    }
}

$dotnetReady = Test-DotNetSdk
$winAppReady = Test-WinAppCli
$templateReady = Test-WinUiTemplate

if (-not ($dotnetReady -and $winAppReady -and $templateReady)) {
    throw "Toolchain incomplete: dotnet=$dotnetReady winapp=$winAppReady winui=$templateReady"
}

Write-Host ''
Write-Host 'BOOTSTRAP_READY'
Write-Host "Git: $(& $gitPath --version)"
Write-Host ".NET SDK: $dotnetVersion"
Write-Host "WinAppCLI: $winAppVersion"
Write-Host "WinUI template: $templateVersion"
Write-Host "WORKSPACE_ROOT: $(Split-Path -Parent $RepoRoot)（所有 App 项目、round-notes 与临时文件都放这里，与仓库同级；禁止写其他路径）"
Write-Host '仓库目录只读：不修改仓库内容，不 git add/commit/push'
Write-Host 'NEXT_ACTION: read skills/vainreef-fast-publish/SKILL.md and start discovery'
