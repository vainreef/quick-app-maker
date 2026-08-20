[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][string]$LogPath
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null

try {
    "[$(Get-Date -Format o)] START dotnet install" | Set-Content -LiteralPath $LogPath -Encoding UTF8
    $installer = Start-Process -FilePath $InstallerPath `
        -ArgumentList '/install', '/quiet', '/norestart' `
        -PassThru -Wait

    "[$(Get-Date -Format o)] END dotnet install exit=$($installer.ExitCode)" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit $installer.ExitCode
}
catch {
    "[$(Get-Date -Format o)] FAIL dotnet install $($_.Exception.Message)" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit 1
}
