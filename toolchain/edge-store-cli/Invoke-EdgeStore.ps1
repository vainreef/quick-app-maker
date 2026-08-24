<#
.SYNOPSIS
    Vainreef Edge Store CLI - Ultra-thin PowerShell Launcher for .NET 10 Driver
#>

[CmdletBinding()]
param(
    [ValidateSet('preflight', 'launch', 'inspect', 'identity', 'run', 'status', 'stop')]
    [string]$Action = 'run',
    [ValidateSet('all', 'availability', 'properties', 'ageRatings', 'packages', 'listing', 'options')]
    [string]$Phase = 'all',
    [string]$Manifest = '',
    [string]$ProductId = '',
    [string]$SubmissionId = '',
    [switch]$Apply,
    [switch]$Submit,
    [switch]$ConfirmSubmit,
    [switch]$KeepOpen,
    [switch]$SkipReloadVerify
)

$ErrorActionPreference = 'Stop'

$toolRoot = $PSScriptRoot
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

$argsList = [System.Collections.Generic.List[string]]::new()
$argsList.Add("run")
$argsList.Add("--project")
$argsList.Add("`"$projectPath`"")
$argsList.Add("--")
$argsList.Add("--action")
$argsList.Add($Action)
$argsList.Add("--phase")
$argsList.Add($Phase)

if (-not [string]::IsNullOrWhiteSpace($Manifest)) {
    $argsList.Add("--manifest")
    $argsList.Add("`"$Manifest`"")
}

if (-not [string]::IsNullOrWhiteSpace($ProductId)) {
    $argsList.Add("--product-id")
    $argsList.Add($ProductId)
}

if ($Apply) { $argsList.Add("--apply") }
if ($Submit) { $argsList.Add("--submit") }
if ($ConfirmSubmit) { $argsList.Add("--confirm-submit") }
if ($KeepOpen) { $argsList.Add("--keep-open") }
if ($SkipReloadVerify) { $argsList.Add("--skip-reload-verify") }

$process = Start-Process -FilePath $dotnet.Source -ArgumentList ($argsList -join ' ') -NoNewWindow -PassThru -Wait
exit $process.ExitCode
