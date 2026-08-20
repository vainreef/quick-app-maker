[CmdletBinding()]
param(
    [string]$Destination = '',
    [string]$Branch = 'main'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoUrl = 'https://gitee.com/freevian/quick-app-maker.git'
$gitVersion = '2.47.1.windows.1'
$gitUrl = 'https://registry.npmmirror.com/-/binary/git-for-windows/v2.47.1.windows.1/Git-2.47.1-64-bit.exe'
$bootstrapCache = Join-Path $env:LOCALAPPDATA 'Vainreef\QuickAppMaker\bootstrap'
$gitInstaller = Join-Path $bootstrapCache 'Git-2.47.1-64-bit.exe'
$gitLog = Join-Path $bootstrapCache 'git-clone.log'

New-Item -ItemType Directory -Force -Path $bootstrapCache | Out-Null

function Write-Step {
    param([string]$Message)
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message"
}

function Find-Git {
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

if ([string]::IsNullOrWhiteSpace($Destination)) {
    $current = (Get-Location).Path
    if (Test-Path -LiteralPath (Join-Path $current '.git')) {
        $Destination = $current
    }
    else {
        $Destination = Join-Path $current 'quick-app-maker'
    }
}

$git = Find-Git
if (-not $git) {
    Write-Step "Git $gitVersion missing; downloading from npmmirror"
    if (-not (Test-Path -LiteralPath $gitInstaller)) {
        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($curl) {
            $previousErrorPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            & $curl.Source --silent --show-error -L --fail --retry 3 --retry-delay 2 --connect-timeout 20 --max-time 1800 -o $gitInstaller $gitUrl
            $curlExitCode = $LASTEXITCODE
            $ErrorActionPreference = $previousErrorPreference
            if ($curlExitCode -ne 0) { throw "Git download failed with exit code $curlExitCode" }
        }
        else {
            Invoke-WebRequest -UseBasicParsing -Uri $gitUrl -OutFile $gitInstaller
        }
    }

    Write-Step 'Installing Git'
    $installer = Start-Process -FilePath $gitInstaller `
        -ArgumentList '/VERYSILENT', '/NORESTART', '/NOCANCEL', '/SP-' `
        -PassThru -Wait
    if ($installer.ExitCode -ne 0) {
        Remove-Item -LiteralPath $gitInstaller -Force -ErrorAction SilentlyContinue
        throw "Git installer exit code $($installer.ExitCode)"
    }
    $git = Find-Git
}
else {
    Write-Step "Git already installed: $(& $git --version)"
}

if (-not $git) { throw 'Git executable was not found after installation' }

if (Test-Path -LiteralPath (Join-Path $Destination '.git')) {
    Write-Step "Repository already exists: $Destination"
    $pullArgs = "-C `"$Destination`" pull --ff-only origin `"$Branch`""
    $pull = Start-Process -FilePath $git `
        -ArgumentList $pullArgs `
        -RedirectStandardOutput $gitLog `
        -RedirectStandardError "$gitLog.err" `
        -PassThru -Wait
    if ($pull.ExitCode -ne 0) {
        throw "Gitee pull exit code $($pull.ExitCode). See $gitLog.err"
    }
}
else {
    if (Test-Path -LiteralPath $Destination) {
        $contents = @(Get-ChildItem -LiteralPath $Destination -Force -ErrorAction SilentlyContinue)
        if ($contents.Count -gt 0) {
            throw "Destination exists and is not empty: $Destination"
        }
    }
    Write-Step "Cloning Gitee repository to $Destination"
    $cloneArgs = "clone --branch `"$Branch`" `"$repoUrl`" `"$Destination`""
    $clone = Start-Process -FilePath $git `
        -ArgumentList $cloneArgs `
        -RedirectStandardOutput $gitLog `
        -RedirectStandardError "$gitLog.err" `
        -PassThru -Wait
    if ($clone.ExitCode -ne 0) {
        throw "Gitee clone exit code $($clone.ExitCode). See $gitLog.err"
    }
}

$head = (& $git -C $Destination rev-parse --short HEAD | Out-String).Trim()
Write-Step "Repository ready: $head"

$installerScript = Join-Path $Destination 'bootstrap\install.ps1'
if (-not (Test-Path -LiteralPath $installerScript)) {
    throw "Repository installer is missing: $installerScript"
}

Write-Step 'Starting repository toolchain installer'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerScript -RepoRoot $Destination
if ($LASTEXITCODE -ne 0) {
    throw "Repository toolchain installer exit code $LASTEXITCODE"
}
