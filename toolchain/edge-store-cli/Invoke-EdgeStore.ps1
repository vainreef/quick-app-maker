<#
.SYNOPSIS
    Vainreef Edge Store CLI - Ultra-thin Direct PowerShell Launcher for .NET 10 Driver
#>

[CmdletBinding()]
param(
    [ValidateSet('preflight', 'launch', 'step', 'discover', 'inspect', 'dumpdom', 'probelanguages', 'cleanpackages', 'cleanlanguages', 'filllisting', 'reserve', 'identity', 'run', 'status', 'verify', 'stop')]
    [string]$Action = 'run',
    [ValidateSet('all', 'availability', 'properties', 'ageRatings', 'packages', 'listing', 'options')]
    [string]$Phase = 'all',
    [string]$Manifest = '',
    [string]$ProductId = '',
    [string]$AppName = '',
    [string]$StateDir = '',
    [switch]$Apply,
    [switch]$Submit,
    [switch]$ConfirmSubmit,
    [switch]$KeepOpen,
    [switch]$ReloadVerify,
    [switch]$SkipReloadVerify
)

$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$toolRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
if (-not $toolRoot) { $toolRoot = (Get-Location).Path }

$projectPath = Join-Path $toolRoot 'EdgeStore.Cli.csproj'
if (-not (Test-Path -LiteralPath $projectPath)) {
    Write-Error "EdgeStore.Cli.csproj not found at: $projectPath"
    exit 2
}

$dotnet = $null
# 1. 优先自动探测工作区同级免安装版 .NET SDK (Project\dotnet\dotnet.exe)
$repoRoot = Split-Path -Parent (Split-Path -Parent $toolRoot)
$workspaceRoot = if (Test-Path -LiteralPath (Join-Path $repoRoot '.git')) { Split-Path -Parent $repoRoot } else { $repoRoot }
$workspaceDotNet = Join-Path $workspaceRoot 'dotnet\dotnet.exe'
if (Test-Path -LiteralPath $workspaceDotNet) {
    $env:DOTNET_ROOT = Split-Path -Parent $workspaceDotNet
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    if ($env:PATH -notlike "*$($env:DOTNET_ROOT)*") {
        $env:PATH = "$($env:DOTNET_ROOT);$env:PATH"
    }
    $dotnet = [PSCustomObject]@{ Source = (Resolve-Path -LiteralPath $workspaceDotNet).Path }
}

if (-not $dotnet) {
    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
}
if (-not $dotnet) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
}
if (-not $dotnet) {
    $systemDefault = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $systemDefault) {
        $dotnet = [PSCustomObject]@{ Source = (Resolve-Path -LiteralPath $systemDefault).Path }
    }
}
if (-not $dotnet) {
    Write-Error ".NET 10 SDK (dotnet) is required to run Edge Store CLI. Please run bootstrap/install.ps1."
    exit 3
}

$dllPath = Join-Path $toolRoot 'bin\Release\net10.0\EdgeStore.Cli.dll'
if (-not (Test-Path -LiteralPath $dllPath)) {
    $dllPath = Join-Path $toolRoot 'bin\Debug\net10.0\EdgeStore.Cli.dll'
}

# 智能感知：如果 DLL 不存在，或者有任意 .cs 源码修改时间晚于 DLL，则自动增量编译
$needsBuild = -not (Test-Path -LiteralPath $dllPath)
if (-not $needsBuild) {
    $dllTime = (Get-Item -LiteralPath $dllPath).LastWriteTimeUtc
    $latestSource = Get-ChildItem -Path $toolRoot -Filter "*.cs" -Recurse | Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($latestSource -and $latestSource.LastWriteTimeUtc -gt $dllTime) {
        $needsBuild = $true
    }
}

if ($needsBuild) {
    Write-Host "[INFO] Detected source code changes. Auto-rebuilding Edge Store CLI..."
    & $dotnet.Source build $projectPath -c Release --nologo -v q /p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Edge Store CLI build failed with exit code $LASTEXITCODE."
        exit 1
    }
    $dllPath = Join-Path $toolRoot 'bin\Release\net10.0\EdgeStore.Cli.dll'
}

$forwardArgs = [System.Collections.Generic.List[string]]::new()
$forwardArgs.Add($dllPath)
$forwardArgs.Add("--action")
$forwardArgs.Add($Action)
$forwardArgs.Add("--phase")
$forwardArgs.Add($Phase)

if (-not [string]::IsNullOrWhiteSpace($Manifest)) {
    $forwardArgs.Add("--manifest")
    $forwardArgs.Add([System.IO.Path]::GetFullPath($Manifest))
}

if (-not [string]::IsNullOrWhiteSpace($ProductId)) {
    $forwardArgs.Add("--product-id")
    $forwardArgs.Add($ProductId)
}

if (-not [string]::IsNullOrWhiteSpace($AppName)) {
    $forwardArgs.Add("--app-name")
    $forwardArgs.Add($AppName)
}

$effectiveStateDir = if (-not [string]::IsNullOrWhiteSpace($StateDir)) { [System.IO.Path]::GetFullPath($StateDir) } else { Join-Path $toolRoot 'state' }
$forwardArgs.Add("--state-dir")
$forwardArgs.Add($effectiveStateDir)

if ($Apply) { $forwardArgs.Add("--apply") }
if ($Submit) { $forwardArgs.Add("--submit") }
if ($ConfirmSubmit) { $forwardArgs.Add("--confirm-submit") }
if ($KeepOpen) { $forwardArgs.Add("--keep-open") }
if ($ReloadVerify -and $SkipReloadVerify) { throw 'Use either -ReloadVerify or -SkipReloadVerify, not both.' }
if ($SkipReloadVerify) { $forwardArgs.Add("--skip-reload-verify") }

& $dotnet.Source @forwardArgs
exit $LASTEXITCODE
