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

if (-not $TaskName) {
  $TaskName = "QAM_Edge_$Port"
}

$cmdFile = Join-Path $dir "launch-$Port.cmd"
$cmd = "@echo off`r`nstart `"`" `"$EdgePath`" --user-data-dir=`"$Profile`" --remote-debugging-port=$Port --remote-debugging-address=127.0.0.1 --remote-allow-origins=http://localhost,http://127.0.0.1 --no-first-run --no-default-browser-check --start-maximized `"$Url`""
Set-Content -LiteralPath $cmdFile -Value $cmd -Encoding ascii

$st = (Get-Date).AddMinutes(5).ToString('HH:mm')
Start-Process schtasks.exe -ArgumentList "/create /tn $TaskName /tr `"`"$cmdFile`"`" /sc ONCE /st $st /it /f" -NoNewWindow -Wait
Start-Process schtasks.exe -ArgumentList "/run /tn $TaskName" -NoNewWindow -Wait

$deadline = (Get-Date).AddSeconds(20)
$edgePid = 0
while ((Get-Date) -lt $deadline) {
  try {
    $res = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/json/version" -ErrorAction Stop
    if ($res) {
      $conns = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
      if ($conns -and $conns.OwningProcess) {
        $edgePid = $conns.OwningProcess
        break
      }
    }
  } catch {
    Start-Sleep -Milliseconds 250
  }
}

Start-Process schtasks.exe -ArgumentList "/delete /tn $TaskName /f" -NoNewWindow -Wait
Remove-Item -Force $cmdFile -ErrorAction SilentlyContinue

if ($edgePid -gt 0) {
  Set-Content -LiteralPath $PidFile -Value ([string]$edgePid) -Encoding ascii
  Write-Output $edgePid
} else {
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
}
