[CmdletBinding()]
param(
    [string]$Destination = '',
    [string]$Branch = 'main'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$workspaceRoot = (Get-Location).Path
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $workspaceRoot 'quick-app-maker'
}
if (Test-Path -LiteralPath (Join-Path $workspaceRoot '.git')) {
    $Destination = $workspaceRoot
}

$nodeVersion = '24.20.0'
$nodeFile = "node-v$nodeVersion-win-x64.zip"
$nodeUrl = "https://registry.npmmirror.com/-/binary/node/v$nodeVersion/$nodeFile"
$nodeSha256 = '6cac9ffbca8f6a47091e4b5c772e0606049c3871cb67d900c0cedde630e545ba'
$cacheRoot = Join-Path $workspaceRoot '.cache\bootstrap'
$nodeRoot = Join-Path $workspaceRoot 'node'
$nodeExe = Join-Path $nodeRoot 'node.exe'
$nodeArchive = Join-Path $cacheRoot $nodeFile
$gitRoot = Join-Path $workspaceRoot 'git'
$gitExe = Join-Path $gitRoot 'cmd\git.exe'
$gitFile = 'MinGit-2.47.1-64-bit.zip'
$gitUrl = 'https://registry.npmmirror.com/-/binary/git-for-windows/v2.47.1.windows.1/MinGit-2.47.1-64-bit.zip'
$gitSha256 = '50b04b55425b5c465d076cdb184f63a0cd0f86f6ec8bb4d5860114a713d2c29a'
$gitArchive = Join-Path $cacheRoot $gitFile

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null

function Write-Step([string]$Message) {
    Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $Message"
}

function Download-Verified([string]$Url, [string]$OutputPath, [string]$ExpectedSha256) {
    $part = "$OutputPath.part"
    if ((Test-Path -LiteralPath $OutputPath) -and $ExpectedSha256) {
        $existing = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($existing -eq $ExpectedSha256.ToLowerInvariant()) { return }
        Remove-Item -LiteralPath $OutputPath -Force
    }
    if (-not (Test-Path -LiteralPath $OutputPath)) {
        Write-Step "Downloading $([IO.Path]::GetFileName($OutputPath)) from the China mirror"
        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($curl) {
            & $curl.Source --fail --location --retry 3 --retry-delay 2 --connect-timeout 20 --max-time 1800 -o $part $Url
            if ($LASTEXITCODE -ne 0) { throw "Download failed: $Url (exit $LASTEXITCODE)" }
        } else {
            Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $part
        }
        if (-not (Test-Path -LiteralPath $part)) { throw "Download produced no file: $part" }
        if ($ExpectedSha256) {
            $actual = (Get-FileHash -LiteralPath $part -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne $ExpectedSha256.ToLowerInvariant()) { throw "SHA-256 mismatch for ${OutputPath}: ${actual}" }
        }
        Move-Item -LiteralPath $part -Destination $OutputPath -Force
    }
}

$nodeReady = $false
if (Test-Path -LiteralPath $nodeExe) {
    $nodeReady = (& $nodeExe --version 2>$null).Trim() -eq "v$nodeVersion"
}
if (-not $nodeReady) {
    Download-Verified $nodeUrl $nodeArchive $nodeSha256
    $staging = Join-Path $cacheRoot 'node-extract'
    if (Test-Path -LiteralPath $staging) { Get-ChildItem -LiteralPath $staging -Force | Remove-Item -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Expand-Archive -LiteralPath $nodeArchive -DestinationPath $staging -Force
    $archiveRoot = $staging
    if (-not (Test-Path -LiteralPath (Join-Path $archiveRoot 'node.exe'))) {
        $inner = @(Get-ChildItem -LiteralPath $staging -Directory)
        if ($inner.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $inner[0].FullName 'node.exe'))) {
            $archiveRoot = $inner[0].FullName
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $archiveRoot 'node.exe'))) { throw "Node archive has no node.exe: $nodeArchive" }
    $installRoot = Join-Path $cacheRoot 'node-ready'
    if (Test-Path -LiteralPath $installRoot) { Get-ChildItem -LiteralPath $installRoot -Force | Remove-Item -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
    Get-ChildItem -LiteralPath $archiveRoot -Force | Move-Item -Destination $installRoot -Force
    if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'node.exe'))) { throw "Node staging is incomplete: $installRoot" }
    if (Test-Path -LiteralPath $nodeRoot) { Get-ChildItem -LiteralPath $nodeRoot -Force | Remove-Item -Recurse -Force }
    Move-Item -LiteralPath $installRoot -Destination $nodeRoot -Force
}
if (-not (Test-Path -LiteralPath $nodeExe)) { throw "Portable Node was not found at $nodeExe" }

$gitReady = $false
if (Test-Path -LiteralPath $gitExe) {
    try { $gitReady = (& $gitExe --version 2>$null).Trim() -eq 'git version 2.47.1.windows.1' } catch { $gitReady = $false }
}
if (-not $gitReady) {
    Download-Verified $gitUrl $gitArchive $gitSha256
    $staging = Join-Path $cacheRoot 'git-extract'
    if (Test-Path -LiteralPath $staging) { Get-ChildItem -LiteralPath $staging -Force | Remove-Item -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    Expand-Archive -LiteralPath $gitArchive -DestinationPath $staging -Force
    $archiveRoot = $staging
    if (-not (Test-Path -LiteralPath (Join-Path $archiveRoot 'cmd\git.exe'))) {
        $inner = @(Get-ChildItem -LiteralPath $staging -Directory)
        if ($inner.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $inner[0].FullName 'cmd\git.exe'))) {
            $archiveRoot = $inner[0].FullName
        }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $archiveRoot 'cmd\git.exe'))) { throw "MinGit archive has no cmd\git.exe: $gitArchive" }
    $installRoot = Join-Path $cacheRoot 'git-ready'
    if (Test-Path -LiteralPath $installRoot) { Get-ChildItem -LiteralPath $installRoot -Force | Remove-Item -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $installRoot | Out-Null
    Get-ChildItem -LiteralPath $archiveRoot -Force | Move-Item -Destination $installRoot -Force
    if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'cmd\git.exe'))) { throw "Git staging is incomplete: $installRoot" }
    if (Test-Path -LiteralPath $gitRoot) { Get-ChildItem -LiteralPath $gitRoot -Force | Remove-Item -Recurse -Force }
    Move-Item -LiteralPath $installRoot -Destination $gitRoot -Force
}
if (-not (Test-Path -LiteralPath $gitExe)) { throw "Portable Git was not found at $gitExe" }
$gitVersion = (& $gitExe --version 2>$null).Trim()
if ($gitVersion -ne 'git version 2.47.1.windows.1') { throw "Unexpected portable Git version at ${gitExe}: $gitVersion" }
$gitPath = (Resolve-Path -LiteralPath $gitExe).Path

if (-not (Test-Path -LiteralPath (Join-Path $Destination '.git'))) {
    if (Test-Path -LiteralPath $Destination) {
        $items = @(Get-ChildItem -LiteralPath $Destination -Force)
        if ($items.Count -gt 0) { throw "Destination exists and is not empty: $Destination" }
    }
    Write-Step "Cloning quick-app-maker into $Destination"
    & $gitPath clone --branch $Branch 'https://gitee.com/freevian/quick-app-maker.git' $Destination
    if ($LASTEXITCODE -ne 0) { throw "Git clone failed with exit code $LASTEXITCODE" }
} elseif ($Destination -ne $workspaceRoot) {
    Write-Step "Updating quick-app-maker from Gitee"
    & $gitPath -C $Destination pull --ff-only origin $Branch
    if ($LASTEXITCODE -ne 0) { throw "Git pull failed with exit code $LASTEXITCODE" }
}

$env:PATH = "$nodeRoot;$env:PATH"
$npmCli = Join-Path $nodeRoot 'node_modules\npm\bin\npm-cli.js'
$npmCache = Join-Path $workspaceRoot '.cache\npm'
$npmrc = Join-Path $workspaceRoot '.cache\npmrc'
if (-not (Test-Path -LiteralPath $npmCli)) { throw "Bundled npm CLI was not found at $npmCli" }
New-Item -ItemType Directory -Force -Path $npmCache | Out-Null
Set-Content -LiteralPath $npmrc -Encoding ascii -Value @(
    'registry=https://registry.npmmirror.com',
    'fund=false',
    'audit=false',
    'progress=false',
    'prefer-offline=true'
)
$env:npm_config_registry = 'https://registry.npmmirror.com'
$env:npm_config_cache = $npmCache
$env:npm_config_userconfig = $npmrc
$env:npm_config_fund = 'false'
$env:npm_config_audit = 'false'
$env:npm_config_progress = 'false'
$env:npm_config_prefer_offline = 'true'
$env:ELECTRON_MIRROR = 'https://npmmirror.com/mirrors/electron/'
$env:electron_config_cache = Join-Path $workspaceRoot '.cache\electron'
$env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = '1'
$env:QAM_WORKSPACE_ROOT = $workspaceRoot
$env:QAM_REQUIRE_PORTABLE = '1'
$dependencyMarker = Join-Path $Destination 'node_modules\.package-lock.json'
$workspaceCore = Join-Path $Destination 'node_modules\@quick-app\core\package.json'
if (-not (Test-Path -LiteralPath $dependencyMarker) -or -not (Test-Path -LiteralPath $workspaceCore)) {
    if (-not (Test-Path -LiteralPath (Join-Path $Destination 'package-lock.json'))) { throw "package-lock.json was not found at $Destination" }
    Write-Step "Installing quick-app-maker dependencies with bundled npm"
    & $nodeExe $npmCli --prefix $Destination ci --ignore-scripts --prefer-offline
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE" }
}
Write-Step "Running Node bootstrap: $Destination"
& $nodeExe (Join-Path $Destination 'bin\qam.mjs') bootstrap --workspace-root $workspaceRoot
if ($LASTEXITCODE -ne 0) { throw "Node bootstrap failed with exit code $LASTEXITCODE" }
Write-Host ''
Write-Host 'BOOTSTRAP_READY'
Write-Host "NODE_PATH: $nodeExe"
Write-Host "WORKSPACE_ROOT: $workspaceRoot"
Write-Host 'NEXT_ACTION: read skills/vainreef-fast-publish/SKILL.md and start discovery'
