[CmdletBinding()]
param(
  [Parameter(Mandatory=$true)][string]$EdgePath,
  [Parameter(Mandatory=$true)][int]$Port,
  [Parameter(Mandatory=$true)][string]$Profile,
  [Parameter(Mandatory=$true)][string]$Url,
  [Parameter(Mandatory=$true)][string]$PidFile,
  [string]$TaskName = '',
  [switch]$Direct
)
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $PidFile
New-Item -ItemType Directory -Force -Path $dir | Out-Null
if ($TaskName -and -not $Direct) {
  $runAt = (Get-Date).AddMinutes(1)
  $time = $runAt.ToString('HH:mm')
  $date = $runAt.ToString('MM/dd/yyyy')
  $taskArgs = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Direct -TaskName `"$TaskName`" -EdgePath `"$EdgePath`" -Port $Port -Profile `"$Profile`" -Url `"$Url`" -PidFile `"$PidFile`""
  & schtasks.exe /Create /TN $TaskName /TR "powershell.exe $taskArgs" /SC ONCE /ST $time /SD $date /RL LIMITED /IT /F | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "could not create interactive desktop task: $TaskName" }
  & schtasks.exe /Run /TN $TaskName | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "could not run interactive desktop task: $TaskName" }
  exit 0
}
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
