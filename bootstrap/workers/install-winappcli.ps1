[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$MsixPath,
    [Parameter(Mandatory = $true)][string]$LogPath
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null

try {
    "[$(Get-Date -Format o)] START winapp install" | Set-Content -LiteralPath $LogPath -Encoding UTF8
    Add-AppxPackage -Path $MsixPath -ErrorAction Stop

    $package = Get-AppxPackage -Name 'winapp' -ErrorAction SilentlyContinue
    if (-not $package) {
        throw 'winapp package was not found after Add-AppxPackage'
    }

    "[$(Get-Date -Format o)] END winapp install version=$($package.Version)" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit 0
}
catch {
    "[$(Get-Date -Format o)] FAIL winapp install $($_.Exception.Message)" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit 1
}
