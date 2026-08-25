<#
.SYNOPSIS
    Vainreef Edge Store CLI - Ultra-thin Direct PowerShell Launcher for .NET 10 Driver
#>

[CmdletBinding()]
param(
    [ValidateSet('preflight', 'launch', 'inspect', 'dumpdom', 'answerno', 'fixpackage', 'filloptions', 'waitpackage', 'canceluploads', 'fixprivacy', 'reserve', 'identity', 'run', 'status', 'verify', 'stop')]
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

$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if (-not $dotnet) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
}
if (-not $dotnet) {
    Write-Error ".NET 10 SDK (dotnet) is required to run Edge Store CLI. Please run bootstrap/install.ps1."
    exit 3
}

$dllPath = Join-Path $toolRoot 'bin\Release\net10.0\EdgeStore.Cli.dll'
if (-not (Test-Path -LiteralPath $dllPath)) {
    $dllPath = Join-Path $toolRoot 'bin\Debug\net10.0\EdgeStore.Cli.dll'
}

# Build if DLL does not exist yet
if (-not (Test-Path -LiteralPath $dllPath)) {
    Write-Host "[INFO] Building Edge Store CLI..."
    $build = Start-Process -FilePath $dotnet.Source -ArgumentList "build `"$projectPath`" -c Release --nologo -v q" -NoNewWindow -PassThru -Wait
    if ($build.ExitCode -ne 0) {
        Write-Error "Edge Store CLI build failed with exit code $($build.ExitCode)."
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
    $forwardArgs.Add($Manifest)
}

if (-not [string]::IsNullOrWhiteSpace($ProductId)) {
    $forwardArgs.Add("--product-id")
    $forwardArgs.Add($ProductId)
}

if (-not [string]::IsNullOrWhiteSpace($AppName)) {
    $forwardArgs.Add("--app-name")
    $forwardArgs.Add($AppName)
}

$effectiveStateDir = if (-not [string]::IsNullOrWhiteSpace($StateDir)) { $StateDir } else { Join-Path $toolRoot 'state' }
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
