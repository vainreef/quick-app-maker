[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [Parameter(Mandatory = $true)][string]$DestinationPath,
    [Parameter(Mandatory = $true)][string]$LogPath
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null
New-Item -ItemType Directory -Force -Path $DestinationPath | Out-Null

try {
    "[$(Get-Date -Format o)] START dotnet extraction from $ZipPath to $DestinationPath" | Set-Content -LiteralPath $LogPath -Encoding UTF8
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $DestinationPath -Force
    "[$(Get-Date -Format o)] END dotnet extraction complete" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit 0
}
catch {
    "[$(Get-Date -Format o)] FAIL dotnet extraction $($_.Exception.Message)" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit 1
}
