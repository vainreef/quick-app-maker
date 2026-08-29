[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$LogPath,
    [string]$DotNetPath = ''
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null

$dotnet = $null
if ($DotNetPath -and (Test-Path -LiteralPath $DotNetPath)) {
    $dotnet = (Resolve-Path -LiteralPath $DotNetPath).Path
}
if (-not $dotnet -and $env:DOTNET_ROOT) {
    $envDotNet = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
    if (Test-Path -LiteralPath $envDotNet) { $dotnet = $envDotNet }
}
if (-not $dotnet) {
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
    $repoRoot = Split-Path -Parent $scriptDir
    $workspaceRoot = if (Test-Path -LiteralPath (Join-Path $repoRoot '.git')) { Split-Path -Parent $repoRoot } else { $repoRoot }
    $wsDotNet = Join-Path $workspaceRoot 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $wsDotNet) { $dotnet = (Resolve-Path -LiteralPath $wsDotNet).Path }
}
if (-not $dotnet) {
    $cmd = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($cmd) { $dotnet = $cmd.Source }
}
if (-not $dotnet) {
    $sysDotNet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $sysDotNet) { $dotnet = $sysDotNet }
}

if (-not $dotnet -or -not (Test-Path -LiteralPath $dotnet)) {
    "[$(Get-Date -Format o)] FAIL dotnet executable missing" | Set-Content -LiteralPath $LogPath -Encoding UTF8
    exit 1
}

$dotnetRoot = Split-Path -Parent $dotnet
$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
if ($env:PATH -notlike "*$dotnetRoot*") {
    $env:PATH = "$dotnetRoot;$env:PATH"
}

"[$(Get-Date -Format o)] START winui template install" | Set-Content -LiteralPath $LogPath -Encoding UTF8
$installStdout = "$LogPath.install.stdout"
$installStderr = "$LogPath.install.stderr"
Remove-Item $installStdout, $installStderr -Force -ErrorAction SilentlyContinue
$installProcess = Start-Process -FilePath $dotnet `
    -ArgumentList "new install `"$PackagePath`"" `
    -RedirectStandardOutput $installStdout `
    -RedirectStandardError $installStderr `
    -PassThru -Wait
$installExitCode = $installProcess.ExitCode
if (Test-Path $installStdout) { Get-Content $installStdout | Add-Content -LiteralPath $LogPath -Encoding UTF8 }
if (Test-Path $installStderr) { Get-Content $installStderr | Add-Content -LiteralPath $LogPath -Encoding UTF8 }

if ($installExitCode -ne 0) {
    "[$(Get-Date -Format o)] FAIL winui template install exit=$installExitCode" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit $installExitCode
}

$listStdout = "$LogPath.list.stdout"
$listStderr = "$LogPath.list.stderr"
Remove-Item $listStdout, $listStderr -Force -ErrorAction SilentlyContinue
$listProcess = Start-Process -FilePath $dotnet `
    -ArgumentList 'new list winui' `
    -RedirectStandardOutput $listStdout `
    -RedirectStandardError $listStderr `
    -PassThru -Wait
$templateList = if (Test-Path $listStdout) { Get-Content $listStdout | Out-String } else { '' }
if ($listProcess.ExitCode -ne 0 -or $templateList -notmatch 'winui-navview') {
    "[$(Get-Date -Format o)] FAIL winui-navview template missing exit=$($listProcess.ExitCode)" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit 1
}

"[$(Get-Date -Format o)] END winui template install" | Add-Content -LiteralPath $LogPath -Encoding UTF8
exit 0
