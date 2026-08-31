[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$EdgePath,
  [Parameter(Mandatory=$true)][int]$Port,
  [Parameter(Mandatory=$true)][string]$Profile,
  [Parameter(Mandatory=$true)][string]$Url,
  [Parameter(Mandatory=$true)][string]$PidFile,
  [string]$TaskName = ''
)
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $PidFile
New-Item -ItemType Directory -Force -Path $dir | Out-Null

$args = @(
  "--user-data-dir=$Profile",
  "--remote-debugging-port=$Port",
  '--remote-debugging-address=127.0.0.1',
  '--remote-allow-origins=http://localhost,http://127.0.0.1',
  '--no-first-run',
  '--no-default-browser-check',
  '--start-maximized',
  $Url
)
$p = Start-Process -FilePath $EdgePath -ArgumentList $args -PassThru -WindowStyle Maximized
Set-Content -LiteralPath $PidFile -Value ([string]$p.Id) -Encoding ascii
Write-Output $p.Id
