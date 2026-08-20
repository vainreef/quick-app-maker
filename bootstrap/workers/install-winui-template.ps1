[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$LogPath
)

$ErrorActionPreference = 'Continue'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null

if (-not (Test-Path -LiteralPath $dotnet)) {
    "[$(Get-Date -Format o)] FAIL dotnet executable missing" | Set-Content -LiteralPath $LogPath -Encoding UTF8
    exit 1
}

"[$(Get-Date -Format o)] START winui template install" | Set-Content -LiteralPath $LogPath -Encoding UTF8
& $dotnet new install $PackagePath 1>> $LogPath 2>> $LogPath
$installExitCode = $LASTEXITCODE

if ($installExitCode -ne 0) {
    "[$(Get-Date -Format o)] FAIL winui template install exit=$installExitCode" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit $installExitCode
}

$templateList = (& $dotnet new list winui 2>$null | Out-String)
if ($templateList -notmatch 'winui-navview') {
    "[$(Get-Date -Format o)] FAIL winui-navview template missing" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit 1
}

"[$(Get-Date -Format o)] END winui template install" | Add-Content -LiteralPath $LogPath -Encoding UTF8
exit 0
