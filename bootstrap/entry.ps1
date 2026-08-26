[CmdletBinding()]
param(
    [string]$Destination = '',
    [string]$Branch = 'main'
)

$repoUrl = 'https://gitee.com/freevian/quick-app-maker.git'
$gitVersion = '2.47.1.windows.1'
$gitUrl = 'https://registry.npmmirror.com/-/binary/git-for-windows/v2.47.1.windows.1/MinGit-2.47.1-64-bit.zip'

$current = (Get-Location).Path
$workspaceRoot = if (Test-Path -LiteralPath (Join-Path $current '.git')) { Split-Path -Parent $current } else { $current }
if (-not $workspaceRoot) { $workspaceRoot = $current }

$minGitDir = Join-Path $workspaceRoot 'git'
$minGitExe = Join-Path $minGitDir 'cmd\git.exe'
$bootstrapCache = Join-Path $workspaceRoot '.cache\bootstrap'
$gitArchive = Join-Path $bootstrapCache 'MinGit-2.47.1-64-bit.zip'
$gitLog = Join-Path $bootstrapCache 'git-clone.log'

New-Item -ItemType Directory -Force -Path $bootstrapCache | Out-Null

function Write-Step {
    param([string]$Message)
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message"
}

function Find-SystemGit {
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
    if (Test-Path -LiteralPath (Join-Path $current '.git')) {
        $Destination = $current
    }
    else {
        $Destination = Join-Path $workspaceRoot 'quick-app-maker'
    }
}

$git = $null
if (Test-Path -LiteralPath $minGitExe) {
    $git = (Resolve-Path -LiteralPath $minGitExe).Path
    Write-Step "Workspace MinGit ready: $git"
}
else {
    Write-Step "MinGit $gitVersion (portable, zero-UAC) missing; downloading from npmmirror"
    if (-not (Test-Path -LiteralPath $gitArchive)) {
        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($curl) {
            $previousErrorPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            & $curl.Source --silent --show-error -L --fail --retry 3 --retry-delay 2 --connect-timeout 20 --max-time 1800 -o $gitArchive $gitUrl
            $curlExitCode = $LASTEXITCODE
            $ErrorActionPreference = $previousErrorPreference
            if ($curlExitCode -ne 0) {
                Write-Step "MinGit download via curl failed (exit $curlExitCode), falling back to Invoke-WebRequest"
                Invoke-WebRequest -UseBasicParsing -Uri $gitUrl -OutFile $gitArchive
            }
        }
        else {
            Invoke-WebRequest -UseBasicParsing -Uri $gitUrl -OutFile $gitArchive
        }
    }

    if (Test-Path -LiteralPath $gitArchive) {
        Write-Step "Extracting MinGit (portable, zero-UAC) to $minGitDir"
        New-Item -ItemType Directory -Force -Path $minGitDir | Out-Null
        Expand-Archive -LiteralPath $gitArchive -DestinationPath $minGitDir -Force
        if (Test-Path -LiteralPath $minGitExe) {
            $git = (Resolve-Path -LiteralPath $minGitExe).Path
        }
    }

    if (-not $git) {
        Write-Step "Workspace MinGit extraction incomplete; checking system Git as fallback"
        $git = Find-SystemGit
    }
}

if (-not $git -or -not (Test-Path -LiteralPath $git)) { throw 'Git executable was not found after bootstrap' }

$gitCmdDir = Split-Path -Parent $git
if ($env:PATH -notlike "*$gitCmdDir*") {
    $env:PATH = "$gitCmdDir;$env:PATH"
}

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
$workspaceRoot = Split-Path -Parent $Destination
Write-Step "Repository ready: $head"
Write-Step "Git executable: $git"
Write-Step "Workspace root: $workspaceRoot"
Write-Step "NOTE: 所有 App 项目、.cache/ 工具缓存与临时文件均放置于当前工作目录。"

$installerScript = Join-Path $Destination 'bootstrap\install.ps1'
if (-not (Test-Path -LiteralPath $installerScript)) {
    throw "Repository installer is missing: $installerScript"
}

Write-Step 'Starting repository toolchain installer'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installerScript -RepoRoot $Destination -GitPath $git
if ($LASTEXITCODE -ne 0) {
    throw "Repository toolchain installer exit code $LASTEXITCODE"
}
