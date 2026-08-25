[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Id,
    [Parameter(Mandatory = $true)][string]$Url,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [Parameter(Mandatory = $true)][string]$LogPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogPath) | Out-Null

$partialPath = "$OutputPath.part"
Remove-Item -LiteralPath $partialPath -Force -ErrorAction SilentlyContinue

try {
    "[$(Get-Date -Format o)] START $Id $Url" | Set-Content -LiteralPath $LogPath -Encoding UTF8

    $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
    if ($curl) {
        $previousErrorPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & $curl.Source --silent --show-error -L --fail --retry 3 --retry-delay 2 --connect-timeout 20 --max-time 1800 -o $partialPath $Url 2>> $LogPath
        $curlExitCode = $LASTEXITCODE
        $ErrorActionPreference = $previousErrorPreference
        if ($curlExitCode -ne 0) {
            throw "curl exit code $curlExitCode"
        }
    }
    else {
        Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $partialPath
    }

    Move-Item -LiteralPath $partialPath -Destination $OutputPath -Force
    $size = (Get-Item -LiteralPath $OutputPath).Length
    "[$(Get-Date -Format o)] DONE $Id bytes=$size" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit 0
}
catch {
    "[$(Get-Date -Format o)] FAIL $Id $($_.Exception.Message)" | Add-Content -LiteralPath $LogPath -Encoding UTF8
    exit 1
}
