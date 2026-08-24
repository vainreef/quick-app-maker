<#
.SYNOPSIS
    Vainreef Edge Store CLI - Declarative Partner Center Automation Driver
    Fully compatible with Windows PowerShell 5.1 & PowerShell 7+ on Windows x64.
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
    [string]$EdgePath = '',
    [string]$ProfilePath = '',
    [switch]$Apply,
    [switch]$Submit,
    [switch]$ConfirmSubmit,
    [switch]$KeepOpen,
    [switch]$ReloadVerify,
    [int]$TimeoutSeconds = 45,
    [int]$LoginTimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ToolRoot = $PSScriptRoot
$script:StateRoot = Join-Path $script:ToolRoot 'state'
$script:LogRoot = Join-Path $script:StateRoot 'logs'
$script:StatePath = Join-Path $script:StateRoot 'store-state.json'
$script:LiveStatePath = Join-Path $script:StateRoot 'live-state.json'
$script:Port = 0
$script:EdgeProcess = $null
$script:CdpSocket = $null
$script:CdpNextId = 1
$script:Config = $null
$script:State = $null
$script:LiveState = $null
$script:ManifestPath = $null
$script:StartedByUs = $false
$script:ExitCode = 1
$script:RunMutex = $null

function Get-PropertyValue {
    param(
        [AllowNull()][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowNull()][object]$Default = $null
    )

    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) { return $Default }
    return $property.Value
}

function Write-Log {
    param(
        [ValidateSet('INFO', 'PLAN', 'WARN', 'ERROR', 'PASS', 'WAIT')]
        [string]$Level,
        [string]$Message
    )

    New-Item -ItemType Directory -Force -Path $script:LogRoot | Out-Null
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [$Level] $Message"
    Add-Content -LiteralPath (Join-Path $script:LogRoot 'edge-store.log') -Value $line -Encoding UTF8
    Write-Host $line
}

function Stop-WithCode {
    param([int]$Code, [string]$Message)
    if ($Message) { Write-Log -Level 'ERROR' -Message $Message }
    $script:ExitCode = $Code
    throw [System.OperationCanceledException]::new($Message)
}

function Acquire-RunMutex {
    if ($Action -eq 'stop' -or $Action -eq 'preflight') { return }
    $script:RunMutex = [Threading.Mutex]::new($false, 'Local\VainreefQuickAppMakerEdgeStoreCli')
    if (-not $script:RunMutex.WaitOne(0)) {
        Stop-WithCode 4 'Another Edge Store CLI process is already running. Resume that process or use -Action stop.'
    }
}

function Release-RunMutex {
    if ($null -ne $script:RunMutex) {
        try { $script:RunMutex.ReleaseMutex() } catch { }
        $script:RunMutex.Dispose()
        $script:RunMutex = $null
    }
}

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        Stop-WithCode 2 "JSON file not found: $Path"
    }
    try {
        return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        Stop-WithCode 2 "Invalid JSON: $Path ($($_.Exception.Message))"
    }
}

function Resolve-ManifestPath {
    param([string]$Path, [string]$BaseDirectory)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $BaseDirectory $Path))
}

function Resolve-ConfigPaths {
    param([object]$Config, [string]$BaseDirectory)

    $assets = Get-PropertyValue $Config 'assets'
    if ($null -ne $assets) {
        foreach ($property in $assets.PSObject.Properties) {
            if ($property.Value -is [string] -and -not [string]::IsNullOrWhiteSpace($property.Value)) {
                $property.Value = Resolve-ManifestPath $property.Value $BaseDirectory
            }
        }
    }
}

function Import-ListingMarkdown {
    $markdownPath = [string](Get-PropertyValue $script:Config 'listingMarkdown' '')
    if ([string]::IsNullOrWhiteSpace($markdownPath)) { return }
    if (-not (Test-Path -LiteralPath $markdownPath)) { Stop-WithCode 12 "listingMarkdown not found: $markdownPath" }
    $text = Get-Content -LiteralPath $markdownPath -Raw -Encoding UTF8
    $values = Get-PropertyValue $script:Config 'values'
    if ($null -eq $values) {
        $values = [pscustomobject]@{}
        $script:Config | Add-Member -NotePropertyName values -NotePropertyValue $values
    }

    $shortMatch = [regex]::Match($text, '(?ms)^##\s*\u7b80\u77ed\u6458\u8981.*?\r?\n\r?\n(?<value>.*?)(?=\r?\n\r?\n##\s*\u5b8c\u6574\u63cf\u8ff0)')
    $fullMatch = [regex]::Match($text, '(?ms)^##\s*\u5b8c\u6574\u63cf\u8ff0.*?\r?\n(?<value>.*?)(?=\r?\n\r?\n##\s*\u4ea7\u54c1\u529f\u80fd)')
    $featuresMatch = [regex]::Match($text, '(?ms)^##\s*\u4ea7\u54c1\u529f\u80fd.*?\r?\n(?<value>.*?)(?=\r?\n\r?\n##\s*\u641c\u7d22\u5173\u952e\u8bcd)')
    $keywordsMatch = [regex]::Match($text, '(?ms)^##\s*\u641c\u7d22\u5173\u952e\u8bcd.*?\r?\n\r?\n(?<value>[^\r\n]+)')

    if ($shortMatch.Success -and [string]::IsNullOrWhiteSpace([string](Get-PropertyValue $values 'shortDescription' ''))) {
        $values | Add-Member -Force -NotePropertyName shortDescription -NotePropertyValue $shortMatch.Groups['value'].Value.Trim()
    }
    if ($fullMatch.Success -and [string]::IsNullOrWhiteSpace([string](Get-PropertyValue $values 'description' ''))) {
        $values | Add-Member -Force -NotePropertyName description -NotePropertyValue $fullMatch.Groups['value'].Value.Trim()
    }
    if ($featuresMatch.Success -and @(Get-PropertyValue $values 'features' @()).Count -eq 0) {
        $features = @($featuresMatch.Groups['value'].Value -split '\r?\n' | Where-Object { $_ -match '^\s*-\s+' } | ForEach-Object { $_ -replace '^\s*-\s+', '' } | Where-Object { $_.Trim() })
        $values | Add-Member -Force -NotePropertyName features -NotePropertyValue $features
    }
    if ($keywordsMatch.Success -and @(Get-PropertyValue $values 'keywords' @()).Count -eq 0) {
        $keywords = @($keywordsMatch.Groups['value'].Value -split '[;；]' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        $values | Add-Member -Force -NotePropertyName keywords -NotePropertyValue $keywords
    }
}

function Save-State {
    New-Item -ItemType Directory -Force -Path $script:StateRoot | Out-Null
    $temp = "$script:StatePath.tmp"
    $json = $script:State | ConvertTo-Json -Depth 30
    [IO.File]::WriteAllText($temp, $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temp -Destination $script:StatePath -Force
}

function Save-LiveState {
    New-Item -ItemType Directory -Force -Path $script:StateRoot | Out-Null
    $temp = "$script:LiveStatePath.tmp"
    $json = $script:LiveState | ConvertTo-Json -Depth 30
    [IO.File]::WriteAllText($temp, $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temp -Destination $script:LiveStatePath -Force
}

function Initialize-State {
    $existing = $null
    if (Test-Path -LiteralPath $script:StatePath) {
        try { $existing = Read-JsonFile $script:StatePath } catch { $existing = $null }
    }

    if ($null -ne $existing) {
        $script:State = $existing
    }
    else {
        $script:State = [ordered]@{
            schemaVersion = 2
            productId = ''
            submissionId = ''
            currentPhase = ''
            completed = @()
            lastUrl = ''
            lastTitle = ''
            startedAt = (Get-Date).ToString('o')
            updatedAt = (Get-Date).ToString('o')
        }
    }

    if (-not ($script:State.PSObject.Properties.Name -contains 'completed')) {
        $script:State | Add-Member -NotePropertyName completed -NotePropertyValue @()
    }
    if (-not ($script:State.PSObject.Properties.Name -contains 'productId')) {
        $script:State | Add-Member -NotePropertyName productId -NotePropertyValue ''
    }
    if (-not ($script:State.PSObject.Properties.Name -contains 'submissionId')) {
        $script:State | Add-Member -NotePropertyName submissionId -NotePropertyValue ''
    }

    $existingLive = $null
    if (Test-Path -LiteralPath $script:LiveStatePath) {
        try { $existingLive = Read-JsonFile $script:LiveStatePath } catch { $existingLive = $null }
    }
    if ($null -ne $existingLive) {
        $script:LiveState = $existingLive
    }
    else {
        $script:LiveState = [ordered]@{
            productId = ''
            submissionId = ''
            discoveredHrefs = [ordered]@{}
            formStatuses = [ordered]@{}
            updatedAt = (Get-Date).ToString('o')
        }
    }
}

function Get-CompletedPhases {
    $value = Get-PropertyValue $script:State 'completed' @()
    if ($value -is [array]) { return @($value) }
    if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { return @() }
    return @([string]$value)
}

function Mark-PhaseCompleted {
    param([string]$Phase)
    $completed = @(Get-CompletedPhases | Where-Object { $_ -ne $Phase })
    $script:State.completed = @($completed + $Phase)
    $script:State.currentPhase = $Phase
    $script:State.updatedAt = (Get-Date).ToString('o')
    Save-State
    Write-Log -Level 'PASS' -Message "phase complete: $Phase"
}

function Resolve-EdgePath {
    if (-not [string]::IsNullOrWhiteSpace($EdgePath)) {
        if (-not (Test-Path -LiteralPath $EdgePath)) { Stop-WithCode 3 "Edge executable not found: $EdgePath" }
        return (Resolve-Path -LiteralPath $EdgePath).Path
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'),
        (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\Application\msedge.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }
    Stop-WithCode 3 'Microsoft Edge Stable was not found. Pass -EdgePath with the full path to msedge.exe.'
}

function Get-FreeTcpPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally { $listener.Stop() }
}

function Wait-Http {
    param([string]$Uri, [int]$Seconds = 30)
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try { return Invoke-RestMethod -Uri $Uri -TimeoutSec 3 }
        catch { Start-Sleep -Milliseconds 250 }
    } while ((Get-Date) -lt $deadline)
    Stop-WithCode 4 "Edge DevTools endpoint did not become ready: $Uri"
}

function Try-Reuse-Edge {
    $pidPath = Join-Path $script:StateRoot 'edge.pid'
    $portPath = Join-Path $script:StateRoot 'edge.port'
    if (-not (Test-Path -LiteralPath $pidPath) -or -not (Test-Path -LiteralPath $portPath)) { return $false }
    try {
        $savedPid = [int](Get-Content -LiteralPath $pidPath -Raw)
        $port = [int](Get-Content -LiteralPath $portPath -Raw)
        $process = Get-Process -Id $savedPid -ErrorAction SilentlyContinue
        if ($null -eq $process -or $process.HasExited) { return $false }
        [void](Wait-Http -Uri "http://127.0.0.1:$port/json/version" -Seconds 3)
        $script:EdgeProcess = $process
        $script:Port = $port
        $script:StartedByUs = $false
        Write-Log -Level 'INFO' -Message "reusing isolated Edge process pid=$savedPid port=$port"
        return $true
    }
    catch { return $false }
}

function Start-Edge {
    if ($null -ne $script:EdgeProcess -and -not $script:EdgeProcess.HasExited) { return }
    if (Try-Reuse-Edge) { return }

    $edge = Resolve-EdgePath
    if ([string]::IsNullOrWhiteSpace($ProfilePath)) {
        $ProfilePath = Join-Path $script:StateRoot 'edge-profile'
    }
    New-Item -ItemType Directory -Force -Path $ProfilePath | Out-Null
    $script:Port = Get-FreeTcpPort

    $args = @(
        "--user-data-dir=`"$ProfilePath`"",
        "--remote-debugging-port=$script:Port",
        '--remote-debugging-address=127.0.0.1',
        '--remote-allow-origins=*',
        '--no-first-run',
        '--no-default-browser-check',
        '--start-maximized',
        'https://partner.microsoft.com/zh-cn/dashboard/home'
    )

    $script:EdgeProcess = Start-Process -FilePath $edge -ArgumentList $args -PassThru
    $script:StartedByUs = $true
    Set-Content -LiteralPath (Join-Path $script:StateRoot 'edge.pid') -Value $script:EdgeProcess.Id -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $script:StateRoot 'edge.port') -Value $script:Port -Encoding ASCII
    Write-Log -Level 'INFO' -Message "started isolated Edge process pid=$($script:EdgeProcess.Id) port=$script:Port"
    [void](Wait-Http -Uri "http://127.0.0.1:$script:Port/json/version" -Seconds 45)
}

function Connect-Cdp {
    $targets = Wait-Http -Uri "http://127.0.0.1:$script:Port/json/list" -Seconds 30
    $pages = @($targets | Where-Object { $_.type -eq 'page' -and $_.webSocketDebuggerUrl })
    if ($pages.Count -eq 0) { Stop-WithCode 4 'No Edge page target is available.' }
    $partner = @($pages | Where-Object { $_.url -like '*partner.microsoft.com*' })
    if ($partner.Count -eq 0) { $partner = $pages }
    $lastError = $null
    foreach ($candidate in $partner) {
        try {
            $ws = [Net.WebSockets.ClientWebSocket]::new()
            [void]($ws.ConnectAsync([Uri]$candidate.webSocketDebuggerUrl, [Threading.CancellationToken]::None).GetAwaiter().GetResult())
            $script:CdpSocket = $ws
            $script:CdpNextId = 1
            $cts = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(20))
            try {
                [void](Send-CdpRequest -Method 'Page.enable' -Params @{})
                [void](Send-CdpRequest -Method 'Runtime.enable' -Params @{})
                [void](Send-CdpRequest -Method 'DOM.enable' -Params @{})
            }
            finally { $cts.Dispose() }
            Write-Log -Level 'INFO' -Message "connected to Edge page target"
            return
        }
        catch {
            $lastError = $_.Exception.Message
            try { $script:CdpSocket.Dispose() } catch { }
            $script:CdpSocket = $null
            Write-Log -Level 'WARN' -Message "page target did not respond: $($_.Exception.Message)"
        }
    }
    Stop-WithCode 4 "No Edge page target responded. Last error: $lastError"
}

function Split-CdpMessages {
    param([string]$Payload)
    $result = [System.Collections.Generic.List[string]]::new()
    $depth = 0
    $inString = $false
    $escaped = $false
    $current = [Text.StringBuilder]::new()
    foreach ($ch in $Payload.ToCharArray()) {
        if ($inString) {
            [void]$current.Append($ch)
            if ($escaped) { $escaped = $false }
            elseif ($ch -eq '\') { $escaped = $true }
            elseif ($ch -eq '"') { $inString = $false }
            continue
        }
        if ($ch -eq '"') { $inString = $true; [void]$current.Append($ch); continue }
        if ($ch -eq '{') { $depth++; [void]$current.Append($ch); continue }
        if ($ch -eq '}') {
            $depth--
            [void]$current.Append($ch)
            if ($depth -eq 0) {
                $result.Add($current.ToString())
                $current.Clear() | Out-Null
            }
            continue
        }
        if ($depth -gt 0) { [void]$current.Append($ch) }
    }
    if ($current.Length -gt 0) { $result.Add($current.ToString()) }
    return $result
}

function Send-CdpRequest {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [hashtable]$Params = @{}
    )

    if ($null -eq $script:CdpSocket -or $script:CdpSocket.State -ne [Net.WebSockets.WebSocketState]::Open) {
        Stop-WithCode 4 'Edge DevTools WebSocket is not connected.'
    }

    $id = $script:CdpNextId
    $script:CdpNextId++
    $message = [ordered]@{ id = $id; method = $Method; params = $Params } | ConvertTo-Json -Depth 30 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($message)
    $segment = [ArraySegment[byte]]::new($bytes)
    [void]($script:CdpSocket.SendAsync($segment, [Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).GetAwaiter().GetResult())

    while ($true) {
        $buffer = New-Object byte[] 65536
        $receivedBytes = [ArraySegment[byte]]::new($buffer)
        $builder = [Text.StringBuilder]::new()
        $done = $false
        $receiveDeadline = (Get-Date).AddSeconds(20)
        do {
            $cts = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(20))
            try {
                $received = $script:CdpSocket.ReceiveAsync($receivedBytes, $cts.Token).GetAwaiter().GetResult()
            }
            catch {
                Stop-WithCode 4 "Edge DevTools receive timed out after 20s ($Method)"
            }
            finally { $cts.Dispose() }
            if ($received.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) {
                Stop-WithCode 4 'Edge DevTools WebSocket closed unexpectedly.'
            }
            [void]$builder.Append([Text.Encoding]::UTF8.GetString($buffer, 0, $received.Count))
            if ($received.EndOfMessage) { $done = $true }
            if (-not $done -and (Get-Date) -ge $receiveDeadline) {
                Stop-WithCode 4 "Edge DevTools message did not finish within 20s ($Method)"
            }
        } while (-not $done)

        $payload = $builder.ToString()
        $messages = Split-CdpMessages -Payload $payload
        foreach ($item in $messages) {
            $parsed = $null
            try { $parsed = $item | ConvertFrom-Json } catch { $parsed = $null }
            if ($null -eq $parsed) { continue }
            $responseId = Get-PropertyValue $parsed 'id' $null
            if ($null -eq $responseId) { continue }
            if ([int]$responseId -eq $id) {
                $errorObject = Get-PropertyValue $parsed 'error' $null
                if ($null -ne $errorObject) {
                    Stop-WithCode 5 "CDP $Method failed: $(Get-PropertyValue $errorObject 'message' 'unknown CDP error')"
                }
                return $parsed
            }
        }
    }
}

function Invoke-PageJs {
    param([Parameter(Mandatory = $true)][string]$Expression)
    $response = Send-CdpRequest -Method 'Runtime.evaluate' -Params @{
        expression = $Expression
        returnByValue = $true
        awaitPromise = $true
        userGesture = $true
    }
    $raw = $response | ConvertTo-Json -Depth 20 -Compress
    try {
        $short = if ($Expression.Length -gt 60) { $Expression.Substring(0, 60) + '...' } else { $Expression }
        Add-Content -LiteralPath (Join-Path $script:LogRoot 'cdp-debug.log') -Value "[$(Get-Date -Format 'HH:mm:ss')] $short => $raw" -Encoding UTF8
    } catch { }
    $result = Get-PropertyValue $response 'result' $null
    $exceptionDetails = Get-PropertyValue $result 'exceptionDetails' $null
    if ($null -ne $exceptionDetails) {
        Stop-WithCode 5 "Page JavaScript failed: $(Get-PropertyValue $exceptionDetails 'text' 'unknown JavaScript error')"
    }
    $remoteResult = Get-PropertyValue $result 'result' $null
    if ($null -eq $remoteResult) {
        Write-Log -Level 'WARN' -Message "Runtime.evaluate returned no result"
        return $null
    }
    if ($remoteResult.PSObject.Properties.Name -contains 'value') { return $remoteResult.value }
    return (Get-PropertyValue $remoteResult 'description' $null)
}

function ConvertTo-JsLiteral {
    param([AllowNull()][object]$Value)
    return ($Value | ConvertTo-Json -Depth 30 -Compress)
}

function Get-PageInfo {
    return Invoke-PageJs @'
(() => ({
  url: location.href,
  title: document.title,
  bodyLength: (document.body && document.body.innerText || '').length,
  hasLogin: /login\.microsoftonline|login\.live\.com|signin/i.test(location.href),
  visibleErrorCount: Array.from(document.querySelectorAll('.alert-error, .alert-danger, [role="alert"].alert-error, .has-error')).filter(e => {
    const r=e.getBoundingClientRect(), s=getComputedStyle(e), t=(e.innerText||'').trim(); return r.width>0 && r.height>0 && s.display!=='none' && s.visibility!=='hidden' && /\u4e0d\u80fd\u4e3a\u7a7a|\u5fc5\u987b|\u9519\u8bef|error|failed|invalid|\u81f3\u5c11\u4e00\u5f20|\u552f\u4e00\u6807\u8bc6|\u5220\u9664\u5176\u4e2d/i.test(t);
  }).length
}))()
'@
}

function Wait-ForPagePredicate {
    param([string]$Expression, [int]$Seconds = $TimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try {
            $value = Invoke-PageJs -Expression $Expression
            if ($value -eq $true -or [string]$value -eq 'true') { return $true }
        }
        catch { }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Wait-ForUrl {
    param([string]$Pattern, [int]$Seconds = $TimeoutSeconds)
    $expression = "location.href.includes($(ConvertTo-JsLiteral $Pattern))"
    if (-not (Wait-ForPagePredicate -Expression $expression -Seconds $Seconds)) {
        Stop-WithCode 6 "Timed out waiting for URL fragment: $Pattern"
    }
}

function Wait-ForText {
    param([string]$Text, [int]$Seconds = $TimeoutSeconds)
    $expression = "(document.body && document.body.innerText || '').includes($(ConvertTo-JsLiteral $Text))"
    if (-not (Wait-ForPagePredicate -Expression $expression -Seconds $Seconds)) {
        Stop-WithCode 6 "Timed out waiting for page text: $Text"
    }
}

function Test-VisibleSelector {
    param([string]$Selector)
    $literal = ConvertTo-JsLiteral $Selector
    return [bool](Invoke-PageJs @"
(() => Array.from(document.querySelectorAll($literal)).filter(e => {
  const r=e.getBoundingClientRect(), s=getComputedStyle(e); return r.width>0 && r.height>0 && s.display!=='none' && s.visibility!=='hidden';
}).length > 0)()
"@)
}

function Click-NativeCenter {
    param(
        [Parameter(Mandatory = $true)][double]$X,
        [Parameter(Mandatory = $true)][double]$Y,
        [string]$Label = ''
    )

    [void](Send-CdpRequest -Method 'Input.dispatchMouseEvent' -Params @{
        type = 'mouseMoved'
        x = [int][Math]::Round($X)
        y = [int][Math]::Round($Y)
    })
    Start-Sleep -Milliseconds 50

    [void](Send-CdpRequest -Method 'Input.dispatchMouseEvent' -Params @{
        type = 'mousePressed'
        button = 'left'
        clickCount = 1
        x = [int][Math]::Round($X)
        y = [int][Math]::Round($Y)
    })
    Start-Sleep -Milliseconds 50

    [void](Send-CdpRequest -Method 'Input.dispatchMouseEvent' -Params @{
        type = 'mouseReleased'
        button = 'left'
        clickCount = 1
        x = [int][Math]::Round($X)
        y = [int][Math]::Round($Y)
    })
    Start-Sleep -Milliseconds 150
    if ($Label) { Write-Log -Level 'INFO' -Message "CDP native click $Label at ($([int]$X), $([int]$Y))" }
}

function Click-SelectorStrict {
    param([string[]]$Selectors, [string]$Label, [switch]$NativeClick)
    $literal = ConvertTo-JsLiteral @($Selectors)
    $result = Invoke-PageJs @"
(() => {
  const selectors=$literal, seen=new Set(), found=[];
  const visible=e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden'&&!e.disabled;};
  for(const selector of selectors){ for(const e of document.querySelectorAll(selector)){ if(visible(e)&&!seen.has(e)){seen.add(e);found.push({e,selector});} } }
  if(found.length!==1) return {ok:false,count:found.length,selectors:found.map(x=>x.selector)};
  const r=found[0].e.getBoundingClientRect();
  return {ok:true,selector:found[0].selector,x:r.left+r.width/2,y:r.top+r.height/2};
})()
"@
    if (-not $result.ok) {
        Stop-WithCode 7 "UI schema mismatch for [$Label]: visible matches=$($result.count)"
    }
    if ($NativeClick) {
        Click-NativeCenter -X ([double]$result.x) -Y ([double]$result.y) -Label $Label
    }
    else {
        [void](Invoke-PageJs "document.querySelector($(ConvertTo-JsLiteral ([string]$result.selector))).click()")
        Write-Log -Level 'INFO' -Message "DOM click $Label via $($result.selector)"
    }
}

function Set-FieldStrict {
    param([string[]]$Selectors, [AllowNull()][string]$Value, [string]$Label)
    $literal = ConvertTo-JsLiteral @($Selectors)
    $valueLiteral = ConvertTo-JsLiteral $Value
    $result = Invoke-PageJs @"
(() => {
  const selectors=$literal, value=$valueLiteral;
  const visible=e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden';};
  let found=[]; for(const selector of selectors){for(const e of document.querySelectorAll(selector)){if(visible(e))found.push(e);}}
  if(found.length!==1)return {ok:false,count:found.length};
  const e=found[0];
  if(e.tagName==='SELECT'){
    const option=Array.from(e.options).find(o=>o.value===value||o.textContent.trim()===value||o.label===value);
    if(!option)return {ok:false,count:1,detail:'option not found'};
    e.value=option.value;
  } else {
    const proto=e.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;
    const setter=Object.getOwnPropertyDescriptor(proto,'value');
    if(setter&&setter.set)setter.set.call(e,value); else e.value=value;
  }
  e.dispatchEvent(new Event('input',{bubbles:true})); e.dispatchEvent(new Event('change',{bubbles:true}));
  return {ok:true,value:e.value};
})()
"@
    if (-not $result.ok) { Stop-WithCode 7 "UI schema mismatch for [$Label]: $(Get-PropertyValue $result 'detail' '') matches=$(Get-PropertyValue $result 'count' 0)" }
    Write-Log -Level 'INFO' -Message "set $Label"
}

function Set-RadioStrict {
    param([string]$Selector, [string]$Label)
    $result = Invoke-PageJs @"
(() => {
  const e=document.querySelector($(ConvertTo-JsLiteral $Selector));
  if(!e)return {ok:false};
  const r=e.getBoundingClientRect();
  return {ok:true,x:r.left+r.width/2,y:r.top+r.height/2};
})()
"@
    if (-not $result.ok) { Stop-WithCode 7 "UI schema mismatch for radio [$Label]" }
    Click-NativeCenter -X ([double]$result.x) -Y ([double]$result.y) -Label $Label
    Write-Log -Level 'INFO' -Message "select radio $Label"
}

function Set-CheckboxByText {
    param([string]$Text, [bool]$Checked, [string]$Label = '')
    $textLiteral = ConvertTo-JsLiteral $Text
    $want = if ($Checked) { 'true' } else { 'false' }
    $result = Invoke-PageJs @"
(() => {
 const target=$textLiteral, want=$want;
 const visible=e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden';};
 const boxes=Array.from(document.querySelectorAll('he-checkbox,input[type="checkbox"]')).filter(e=>visible(e)&&((e.innerText||e.parentElement?.innerText||'')+' '+(e.getAttribute('name')||'')+' '+(e.id||'')).toLowerCase().includes(target.toLowerCase()));
 if(boxes.length===0)return {ok:false,count:0};
 const clicks=[];
 for(const e of boxes){
   const current=e.checked===true||e.hasAttribute('checked')||e.getAttribute('aria-checked')==='true';
   if(current!==want){
     const r=e.getBoundingClientRect();
     clicks.push({x:r.left+r.width/2,y:r.top+r.height/2});
   }
 }
 return {ok:true,count:boxes.length,clicks};
})()
"@
    if (-not $result.ok) { Stop-WithCode 7 "UI schema mismatch for checkbox [$Text]" }
    $clicks = @(Get-PropertyValue $result 'clicks' @())
    foreach ($pt in $clicks) {
        Click-NativeCenter -X ([double]$pt.x) -Y ([double]$pt.y) -Label "checkbox $Text"
    }
    $displayLabel = if (-not [string]::IsNullOrWhiteSpace($Label)) { $Label } else { $Text }
    Write-Log -Level 'INFO' -Message "checkbox $displayLabel => $Checked ($($result.count) matches, $($clicks.Count) clicked)"
}

function Click-TextStrict {
    param([string]$Text, [string]$Label = $Text, [string]$RootSelector = 'body')
    $textLiteral = ConvertTo-JsLiteral $Text
    $rootLiteral = ConvertTo-JsLiteral $RootSelector
    $result = Invoke-PageJs @"
(() => {
 const root=document.querySelector($rootLiteral), target=$textLiteral;
 if(!root)return {ok:false,count:0};
 const visible=e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden'&&!e.disabled;};
 const els=Array.from(root.querySelectorAll('button,a,he-button,he-option,span')).filter(e=>visible(e)&&e.innerText.trim()===target&&(!e.children.length||Array.from(e.children).every(c=>c.innerText.trim()!==target)));
 if(els.length!==1)return {ok:false,count:els.length};
 const r=els[0].getBoundingClientRect();
 return {ok:true,x:r.left+r.width/2,y:r.top+r.height/2};
})()
"@
    if (-not $result.ok) { Stop-WithCode 7 "UI schema mismatch for text [$Label]: matches=$($result.count)" }
    Click-NativeCenter -X ([double]$result.x) -Y ([double]$result.y) -Label "text $Label"
}

function Get-VisibleErrors {
    return Invoke-PageJs @'
(() => Array.from(document.querySelectorAll('.alert-error, .alert-danger, [role="alert"].alert-error, .has-error')).filter(e => {
  const r=e.getBoundingClientRect(), s=getComputedStyle(e), t=(e.innerText||'').trim();
  return r.width>0 && r.height>0 && s.display!=='none' && s.visibility!=='hidden' && /\u4e0d\u80fd\u4e3a\u7a7a|\u5fc5\u987b|\u9519\u8bef|error|failed|invalid|\u81f3\u5c11\u4e00\u5f20|\u552f\u4e00\u6807\u8bc6|\u5220\u9664\u5176\u4e2d/i.test(t);
}).map(e => (e.innerText||'').trim()).filter(Boolean).slice(0,20))()
'@
}

function Assert-NoVisibleErrors {
    $errors = @(Get-VisibleErrors)
    if ($errors.Count -gt 0) {
        Stop-WithCode 8 "Partner Center validation errors: $($errors -join ' | ')"
    }
}

function Get-CurrentUrl {
    return [string](Invoke-PageJs 'location.href')
}

function Navigate-Url {
    param([string]$Url, [string]$Label)
    Write-Log -Level 'INFO' -Message "navigate $Label => $Url"
    [void](Send-CdpRequest -Method 'Page.navigate' -Params @{ url = $Url })
    Wait-ForUrl -Pattern ($Url.Split('?')[0]) -Seconds $TimeoutSeconds
    Start-Sleep -Milliseconds 800
}

function Ensure-SignedIn {
    $info = Get-PageInfo
    if ($info.hasLogin -or $info.url -notlike '*partner.microsoft.com*') {
        Write-Log -Level 'WAIT' -Message 'Edge is waiting for user sign-in/MFA in the isolated Edge window.'
        $deadline = (Get-Date).AddSeconds($LoginTimeoutSeconds)
        do {
            Start-Sleep -Seconds 2
            $info = Get-PageInfo
            if (-not $info.hasLogin -and $info.url -like '*partner.microsoft.com*') { break }
        } while ((Get-Date) -lt $deadline)
        if ($info.hasLogin -or $info.url -notlike '*partner.microsoft.com*') {
            Stop-WithCode 10 'Sign-in was not completed before the login timeout. The Edge window is left open.'
        }
    }
    Write-Log -Level 'PASS' -Message 'Partner Center session is active'
}

function Get-BaseUrl {
    return [string](Get-PropertyValue (Get-PropertyValue $script:Config 'site') 'baseUrl' 'https://partner.microsoft.com/zh-cn/dashboard/products')
}

function Update-IdsFromUrl {
    $url = Get-CurrentUrl
    $match = [regex]::Match($url, '/products/([^/]+)/submissions/([^/?]+)')
    if ($match.Success) {
        if ([string]::IsNullOrWhiteSpace([string]$script:State.productId)) { $script:State.productId = $match.Groups[1].Value }
        if ([string]::IsNullOrWhiteSpace([string]$script:State.submissionId)) { $script:State.submissionId = $match.Groups[2].Value }
    }
    $script:State.lastUrl = $url
    $script:State.lastTitle = [string](Invoke-PageJs 'document.title')
    $script:State.updatedAt = (Get-Date).ToString('o')
    Save-State
}

function Get-ProductId {
    $id = [string](Get-PropertyValue $script:Config 'productId' '')
    if (-not [string]::IsNullOrWhiteSpace($ProductId)) { $id = $ProductId }
    if ([string]::IsNullOrWhiteSpace($id)) { $id = [string](Get-PropertyValue $script:State 'productId' '') }
    if ([string]::IsNullOrWhiteSpace($id)) { Update-IdsFromUrl; $id = [string]$script:State.productId }
    if ([string]::IsNullOrWhiteSpace($id)) { Stop-WithCode 11 'ProductId is missing. Pass -ProductId or navigate to the product page.' }
    $script:State.productId = $id
    return $id
}

function Discover-LiveSubmissionUrls {
    $product = Get-ProductId
    $overview = "$(Get-BaseUrl)/$product/overview"
    Navigate-Url -Url $overview -Label 'product overview to probe submission links'
    Start-Sleep -Seconds 2

    $discovery = Invoke-PageJs @'
(() => {
  const result = { submissionId: '', hrefs: {}, statuses: {}, canStartSubmission: false };
  const startBtn = document.querySelector('he-button[data-l10n-key="Start_Submission"], button[data-l10n-key="Start_Submission"], [data-automation-id="Start_Submission"]');
  if (startBtn) result.canStartSubmission = true;

  const links = Array.from(document.querySelectorAll('a[href*="/submissions/"]'));
  for (const a of links) {
    const href = a.href || '';
    const m = href.match(/\/submissions\/([^\/?#]+)/);
    if (m && !result.submissionId) result.submissionId = m[1];

    const name = a.getAttribute('name') || '';
    if (name === 'princingAndAvailability' || href.includes('/availability')) result.hrefs['availability'] = href;
    else if (name === 'properties' || href.includes('/properties')) result.hrefs['properties'] = href;
    else if (name === 'ageRatings' || href.includes('/ageratings')) result.hrefs['ageRatings'] = href;
    else if (name === 'packages' || href.includes('/packages')) result.hrefs['packages'] = href;
    else if (name === 'storeListing' || href.includes('/listings') || href.includes('/managelanguages')) result.hrefs['listing'] = href;
    else if (href.includes('/options')) result.hrefs['options'] = href;
  }
  return result;
})()
'@

    if ($discovery.submissionId) {
        $script:State.submissionId = [string]$discovery.submissionId
        $script:LiveState.submissionId = [string]$discovery.submissionId
        $script:LiveState.discoveredHrefs = $discovery.hrefs
        $script:LiveState.updatedAt = (Get-Date).ToString('o')
        Save-State
        Save-LiveState
        Write-Log -Level 'PASS' -Message "live submission discovered: $($discovery.submissionId)"
        return $true
    }
    return $false
}

function Ensure-Submission {
    $product = Get-ProductId
    if (Discover-LiveSubmissionUrls) { return }

    $overview = "$(Get-BaseUrl)/$product/overview"
    Navigate-Url -Url $overview -Label 'product overview before starting submission'
    if (-not $Apply) {
        Write-Log -Level 'PLAN' -Message 'dry-run: would click Start Submission to create a draft submission'
        Stop-WithCode 14 'SubmissionId is missing in dry-run mode. Pass -Apply to create a draft submission.'
    }
    Click-SelectorStrict @('he-button[data-l10n-key="Start_Submission"]', 'button[data-l10n-key="Start_Submission"]', '[data-automation-id="Start_Submission"]') 'Start Submission' -NativeClick
    Wait-ForUrl -Pattern '/submissions/' -Seconds $TimeoutSeconds
    Start-Sleep -Seconds 2
    if (-not (Discover-LiveSubmissionUrls)) {
        Update-IdsFromUrl
    }
    if ([string]::IsNullOrWhiteSpace([string]$script:State.submissionId)) {
        Stop-WithCode 11 'Failed to discover submission ID after clicking Start Submission.'
    }
    Write-Log -Level 'PASS' -Message "draft submission ready: $($script:State.submissionId)"
}

function Get-SubmissionId {
    $id = [string](Get-PropertyValue $script:State 'submissionId' '')
    if (-not [string]::IsNullOrWhiteSpace($SubmissionId)) { $id = $SubmissionId }
    if ([string]::IsNullOrWhiteSpace($id)) { Ensure-Submission; $id = [string]$script:State.submissionId }
    return $id
}

function Get-PhaseUrl {
    param([string]$Phase)
    $discovered = Get-PropertyValue (Get-PropertyValue $script:LiveState 'discoveredHrefs') $Phase $null
    if ($null -ne $discovered -and -not [string]::IsNullOrWhiteSpace([string]$discovered)) {
        return [string]$discovered
    }

    $base = Get-BaseUrl
    $product = Get-ProductId
    $submission = Get-SubmissionId
    $languageId = [string](Get-PropertyValue (Get-PropertyValue $script:Config 'site') 'languageId' '5')
    $languageCode = [string](Get-PropertyValue (Get-PropertyValue $script:Config 'site') 'languageCode' 'zh-cn')

    switch ($Phase) {
        'availability' { return "$base/$product/submissions/$submission/availability" }
        'properties' { return "$base/$product/submissions/$submission/properties" }
        'ageRatings' { return "$base/$product/submissions/$submission/ageratings" }
        'packages' { return "$base/$product/submissions/$submission/packages" }
        'listing' { return "$base/$product/submissions/$submission/listings?languageid=$languageId&languagecode=$languageCode" }
        'options' { return "$base/$product/submissions/$submission/options" }
        'overview' { return "$base/$product/submissions/$submission/overview" }
        default { Stop-WithCode 11 "Unknown phase URL: $Phase" }
    }
}

function Save-CurrentPage {
    param([string]$Phase)
    if (-not $Apply) {
        Write-Log -Level 'PLAN' -Message "dry-run: would save $Phase"
        return
    }
    switch ($Phase) {
        'availability' { Click-SelectorStrict @('input#saveButtonPricing', 'button#saveButtonPricing', 'input[uitestid="saveButtonPricing"]', 'button[data-l10n-key="AppSubmission_SaveButton"]') 'availability save' -NativeClick }
        'properties' { Click-SelectorStrict @('button[data-l10n-key="appsubmission_savebutton"]', 'input[value="\u4fdd\u5b58"]', 'button[value="\u4fdd\u5b58"]') 'properties save' -NativeClick }
        'ageRatings' { Click-SelectorStrict @('he-button[data-l10n-key="AppSubmission_AgeRating_SaveDraftButton"]', 'button[data-l10n-key="AppSubmission_AgeRating_SaveDraftButton"]') 'age ratings save draft' -NativeClick }
        'packages' { Click-SelectorStrict @('input[value="Save"]', 'button[value="Save"]', 'button[data-l10n-key="appsubmission_savebutton"]') 'packages save' -NativeClick }
        'listing' { Click-SelectorStrict @('button[name="save_button"]', 'button[data-l10n-key="appsubmission_savebutton"]') 'listing save' -NativeClick }
        'options' { Click-SelectorStrict @('button[data-l10n-key="optionsSave"]', 'button[data-l10n-key="appsubmission_savebutton"]') 'options save' -NativeClick }
    }
    Start-Sleep -Seconds 3
    Assert-NoVisibleErrors

    if ($ReloadVerify) {
        Write-Log -Level 'INFO' -Message "verifying persistence via F5 reload for $Phase"
        [void](Send-CdpRequest -Method 'Page.reload' -Params @{ ignoreCache = $true })
        Start-Sleep -Seconds 3
        Assert-NoVisibleErrors
    }
}

function Get-FileInputNodeId {
    param([string[]]$ContextTexts, [string]$Label, [int]$InputIndex = -1)
    if ($InputIndex -ge 0) {
        $doc = Send-CdpRequest -Method 'DOM.getDocument' -Params @{ depth = -1; pierce = $true }
        $all = Send-CdpRequest -Method 'DOM.querySelectorAll' -Params @{ nodeId = $doc.result.root.nodeId; selector = 'input[type="file"]' }
        if ($InputIndex -ge @($all.result.nodeIds).Count) { Stop-WithCode 7 "File input index does not exist for [$Label]: $InputIndex" }
        return $all.result.nodeIds[$InputIndex]
    }
    $literal = ConvertTo-JsLiteral @($ContextTexts)
    $indices = Invoke-PageJs @"
(() => {
 const texts=$literal.map(x=>x.toLowerCase());
 const files=Array.from(document.querySelectorAll('input[type="file"]'));
 const matches=[];
 files.forEach((e,i)=>{
   const card=e.closest('.listing-image-inner, .asset-card');
   let t=card ? (card.innerText||'') : '';
   if(!t){let n=e;for(let k=0;k<12&&n;k++,n=n.parentElement)t+=' '+(n.innerText||'');}
   t=t.toLowerCase(); if(texts.some(x=>t.includes(x)))matches.push(i);
 });
 return matches;
})()
"@
    $indices = @($indices)
    if ($indices.Count -ne 1) { Stop-WithCode 7 "UI schema mismatch for upload [$Label]: file inputs matched=$($indices.Count)" }
    $doc = Send-CdpRequest -Method 'DOM.getDocument' -Params @{ depth = -1; pierce = $true }
    $all = Send-CdpRequest -Method 'DOM.querySelectorAll' -Params @{ nodeId = $doc.result.root.nodeId; selector = 'input[type="file"]' }
    return $all.result.nodeIds[[int]$indices[0]]
}

function Upload-File {
    param([string[]]$ContextTexts, [string]$Path, [string]$Label, [int]$InputIndex = -1)
    if (-not (Test-Path -LiteralPath $Path)) { Stop-WithCode 12 "Upload file not found: $Path" }
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message "dry-run: would upload $Label"; return }
    $nodeId = Get-FileInputNodeId -ContextTexts $ContextTexts -Label $Label -InputIndex $InputIndex
    [void](Send-CdpRequest -Method 'DOM.setFileInputFiles' -Params @{ nodeId = $nodeId; files = @([IO.Path]::GetFullPath($Path)) })
    Write-Log -Level 'INFO' -Message "uploaded $Label from $Path"
    Start-Sleep -Seconds 3
}

function Test-SectionHasImage {
    param([string[]]$ContextTexts, [int]$InputIndex = -1)
    $literal = ConvertTo-JsLiteral @($ContextTexts)
    $indexLiteral = [string]$InputIndex
    return [bool](Invoke-PageJs @"
(() => {
 const texts=$literal.map(x=>x.toLowerCase()), files=Array.from(document.querySelectorAll('input[type="file"]'));
 const candidates=$indexLiteral>=0 ? [files[$indexLiteral]].filter(Boolean) : files;
 for(const input of candidates){
   const card=input.closest('.listing-image-inner, .asset-card');
   let t=card ? (card.innerText||'') : '';
   if(!t){let n=input;for(let k=0;k<12&&n;k++,n=n.parentElement)t+=' '+(n.innerText||'');}
   t=t.toLowerCase(); if(($indexLiteral>=0||texts.some(x=>t.includes(x)))){const root=card||input.closest('section')||input.parentElement;return !!root.querySelector('img[src]');}
 }
 return false;
})()
"@)
}

function Invoke-PreflightStore0 {
    Write-Log -Level 'INFO' -Message '--- Starting STORE 0: Offline Static Preflight Quality Inspection ---'
    $assets = Get-PropertyValue $script:Config 'assets'
    $expectedName = [string](Get-PropertyValue $script:Config 'productName' '')
    $msix = [string](Get-PropertyValue $assets 'msix' '')

    if (-not [string]::IsNullOrWhiteSpace($msix)) {
        if (-not (Test-Path -LiteralPath $msix)) { Stop-WithCode 12 "MSIX package not found at: $msix" }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($msix)
        try {
            $entry = $archive.Entries | Where-Object { $_.FullName -ieq 'AppxManifest.xml' } | Select-Object -First 1
            if ($null -eq $entry) { Stop-WithCode 12 'AppxManifest.xml missing inside MSIX.' }
            $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8)
            $manifestXml = $reader.ReadToEnd()
            $reader.Dispose()

            if ($expectedName) {
                if (-not $manifestXml.Contains("<DisplayName>$expectedName</DisplayName>") -and -not $manifestXml.Contains("DisplayName=""$expectedName""")) {
                    Stop-WithCode 12 "MSIX DisplayName does not match product name [$expectedName]."
                }
                Write-Log -Level 'PASS' -Message "MSIX DisplayName matches [$expectedName]"
            }

            if ($manifestXml.Contains('Name="Windows.Universal"')) {
                Stop-WithCode 12 'MSIX contains Windows.Universal TargetDeviceFamily dependency. Desktop apps must only declare Windows.Desktop.'
            }
            Write-Log -Level 'PASS' -Message 'MSIX TargetDeviceFamily verified: Desktop only'
        }
        finally {
            $archive.Dispose()
        }
    }

    $values = Get-PropertyValue $script:Config 'values'
    $keywords = @(Get-PropertyValue $values 'keywords' @())
    if ($keywords.Count -gt 7) {
        Stop-WithCode 12 "Keywords count exceeds Microsoft Store limit of 7 (currently $($keywords.Count))."
    }
    Write-Log -Level 'PASS' -Message "Keywords count verified ($($keywords.Count) <= 7)"

    Write-Log -Level 'PASS' -Message 'STORE 0: Static preflight passed successfully.'
}

function Set-Keywords {
    param([string[]]$Keywords)
    if ($Keywords.Count -eq 0) { return }
    $existingKeywords = @(Invoke-PageJs @'
(() => { const root=document.querySelector('#search-terms')||document.querySelector('he-select[multiple]'); if(!root)return []; return Array.from(root.querySelectorAll('he-option')).filter(e=>(e.getAttribute('slot')||'').startsWith('selected-')||e.getAttribute('role')==='listitem').map(e=>(e.innerText||e.getAttribute('value')||'').trim()).filter(Boolean); })()
'@)
    foreach ($keyword in $Keywords) {
        if ($existingKeywords -contains [string]$keyword) {
            Write-Log -Level 'INFO' -Message "keyword already present: $keyword"
            continue
        }
        if (-not $Apply) { Write-Log -Level 'PLAN' -Message "dry-run: would add keyword $keyword"; continue }
        Click-SelectorStrict @('#search-terms he-select', 'he-select[multiple]') 'keyword control' -NativeClick
        [void](Send-CdpRequest -Method 'Input.insertText' -Params @{ text = [string]$keyword })
        [void](Send-CdpRequest -Method 'Input.dispatchKeyEvent' -Params @{ type = 'keyDown'; key = 'Enter'; code = 'Enter'; windowsVirtualKeyCode = 13; nativeVirtualKeyCode = 13 })
        [void](Send-CdpRequest -Method 'Input.dispatchKeyEvent' -Params @{ type = 'keyUp'; key = 'Enter'; code = 'Enter'; windowsVirtualKeyCode = 13; nativeVirtualKeyCode = 13 })
        Start-Sleep -Milliseconds 300
    }
}

function Set-AgeAnswer {
    param([string]$QuestionId, [string]$AnswerText)
    $qid = ConvertTo-JsLiteral $QuestionId
    $answer = ConvertTo-JsLiteral $AnswerText
    $questionExpression = "document.querySelector('[role=`"radiogroup`"][aria-labelledby=`"question#$QuestionId`"]') !== null"
    if (-not (Wait-ForPagePredicate -Expression $questionExpression -Seconds $TimeoutSeconds)) {
        Stop-WithCode 7 "Age rating question did not appear: $QuestionId"
    }
    $result = Invoke-PageJs @"
(() => {
 const q=$qid,a=$answer,group=document.querySelector('[role="radiogroup"][aria-labelledby="question#'+q+'"]');
 if(!group)return {ok:false,count:0};
 const radios=Array.from(group.querySelectorAll('input[type="radio"]')).filter(e=>{const l=e.parentElement?.innerText||e.closest('label')?.innerText||'';return l.includes(a)||e.value===a;});
 if(radios.length!==1)return {ok:false,count:radios.length};
 const r=radios[0].getBoundingClientRect();
 return {ok:true,x:r.left+r.width/2,y:r.top+r.height/2};
})()
"@
    if (-not $result.ok) { Stop-WithCode 7 "Age rating question $QuestionId [$AnswerText] matched $($result.count) elements" }
    Click-NativeCenter -X ([double]$result.x) -Y ([double]$result.y) -Label "age question $QuestionId => $AnswerText"
    Start-Sleep -Milliseconds 200
}

function Invoke-Availability {
    Navigate-Url -Url (Get-PhaseUrl 'availability') -Label 'pricing and availability'
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message 'availability: global markets, ASAP, never stop selling, CNY currency, price tier 0, save'; return }
    if (-not (Wait-ForPagePredicate -Expression "document.querySelector('input[name=`"marketSelection`"]') !== null" -Seconds $TimeoutSeconds)) {
        Stop-WithCode 6 'Pricing page did not render its market controls in time.'
    }
    Start-Sleep -Seconds 2
    Set-RadioStrict -Selector 'input[name="marketSelection"][value="true"]' -Label 'all markets'
    if (Test-VisibleSelector '#distributionOption') { Set-FieldStrict -Selectors @('#distributionOption') -Value 'string:Retail' -Label 'retail distribution' }
    if (Test-VisibleSelector '#radioDistribution_PublicAudience') { Set-RadioStrict -Selector '#radioDistribution_PublicAudience' -Label 'public audience' }
    
    $currentAsap = [string](Invoke-PageJs "(() => { const e=document.querySelector('select[uitestid=`"AvailableSelector-0`"]'); return e ? e.value : ''; })()")
    if ($currentAsap -ne 'string:asap') {
        Set-FieldStrict -Selectors @('select[uitestid="AvailableSelector-0"]') -Value 'string:asap' -Label 'publish ASAP'
    }
    $currentStop = [string](Invoke-PageJs "(() => { const e=document.querySelector('select[uitestid=`"StopSellingSelector-0`"]'); return e ? e.value : ''; })()")
    if ($currentStop -ne 'string:auto-fill') {
        Set-FieldStrict -Selectors @('select[uitestid="StopSellingSelector-0"]') -Value 'string:auto-fill' -Label 'never stop selling'
    }

    $pricing = Get-PropertyValue $script:Config 'pricing'
    $currency = [string](Get-PropertyValue $pricing 'currency' 'CN')
    $priceTier = [string](Get-PropertyValue $pricing 'priceTier' '0')

    $currentCurrency = [string](Invoke-PageJs "(() => { const e=document.querySelector('market-group .price-config > he-select'); return e ? (e.getAttribute('value')||'') : ''; })()")
    if ($currentCurrency -ne $currency) {
        Click-SelectorStrict @('market-group .price-config > he-select') 'base currency' -NativeClick
        Start-Sleep -Milliseconds 500
        Click-TextStrict -Text 'CNY - \u4e2d\u56fd' -Label 'CNY currency'
    }

    $currentTier = [string](Invoke-PageJs "(() => { const e=document.querySelector('market-group price-tier-selection he-select'); return e ? (e.getAttribute('value')||'') : ''; })()")
    if ($currentTier -ne $priceTier) {
        Click-SelectorStrict @('market-group price-tier-selection he-select') 'base price tier' -NativeClick
        Start-Sleep -Milliseconds 500
        Click-TextStrict -Text '0' -Label 'zero price tier'
    }

    Start-Sleep -Seconds 1
    Save-CurrentPage -Phase 'availability'
    Mark-PhaseCompleted 'availability'
}

function Invoke-Properties {
    Navigate-Url -Url (Get-PhaseUrl 'properties') -Label 'properties'
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message 'properties: Productivity, privacy text, desktop/x64, save'; return }
    $properties = Get-PropertyValue $script:Config 'properties'
    $category = [string](Get-PropertyValue $properties 'category' 'Productivity')
    $privacy = [string](Get-PropertyValue $properties 'privacy' 'No')
    $privacyText = [string](Get-PropertyValue $properties 'privacyPolicyText' '')

    Set-FieldStrict -Selectors @('select[name="CategorySelect"]') -Value $category -Label 'category'
    Set-FieldStrict -Selectors @('select[name="privacyPolicySelection"]') -Value $privacy -Label 'privacy answer'
    if ($privacy -eq 'No') {
        if ([string]::IsNullOrWhiteSpace($privacyText)) { Stop-WithCode 12 'properties.privacyPolicyText is required when privacy=No.' }
        if (-not (Wait-ForPagePredicate -Expression "document.querySelector('#privacyPolicyText') !== null" -Seconds $TimeoutSeconds)) { Stop-WithCode 7 'Privacy policy text option did not appear.' }
        Set-RadioStrict -Selector '#privacyPolicyText' -Label 'provide privacy policy text'
        if (-not (Wait-ForPagePredicate -Expression 'document.querySelector(''textarea[aria-label="\u63d0\u4f9b\u9690\u79c1\u7b56\u7565\u6587\u672c"]'') !== null' -Seconds $TimeoutSeconds)) { Stop-WithCode 7 'Privacy policy text area did not appear.' }
        Set-FieldStrict -Selectors @('support-info textarea[aria-label="\u63d0\u4f9b\u9690\u79c1\u7b56\u7565\u6587\u672c"]', 'textarea[aria-label="\u63d0\u4f9b\u9690\u79c1\u7b56\u7565\u6587\u672c"]') -Value $privacyText -Label 'privacy policy text'
    }
    Set-CheckboxByText -Text 'storage' -Checked $true -Label 'storage declaration'
    Set-CheckboxByText -Text 'backups' -Checked $true -Label 'backups declaration'
    Set-CheckboxByText -Text 'windows' -Checked $true -Label 'windows declaration'
    Set-CheckboxByText -Text 'usesGenAI' -Checked $false -Label 'usesGenAI declaration'
    Save-CurrentPage -Phase 'properties'
    Mark-PhaseCompleted 'properties'
}

function Invoke-AgeRatings {
    Navigate-Url -Url (Get-PhaseUrl 'ageRatings') -Label 'age ratings'
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message 'age ratings: questionnaire, other type 2558, answers No, save & continue'; return }
    Set-RadioStrict -Selector 'input[name="inputMode"][value="questionnaire"]' -Label 'IARC questionnaire'
    Set-RadioStrict -Selector 'input[name="question#1109"][value="2558"]' -Label 'other application type'
    Set-RadioStrict -Selector '#radioGroup input#noVal' -Label 'physical media no'
    $questionIds = @('1152', '1188', '1193', '1037', '1194', '1195', '1375', '1196', '1197')
    foreach ($questionId in $questionIds) { Set-AgeAnswer -QuestionId $questionId -AnswerText '\u5426' }
    Click-SelectorStrict @('he-checkbox[required]', 'he-checkbox') 'IARC terms agreement' -NativeClick
    Click-SelectorStrict @('he-button[data-l10n-key="AppSubmission_AgeRating_SaveButton"]', 'button[data-l10n-key="AppSubmission_AgeRating_SaveButton"]') 'age ratings preview save' -NativeClick
    Start-Sleep -Seconds 3
    Click-TextStrict -Text '\u7ee7\u7eed' -Label 'continue after age ratings'
    Mark-PhaseCompleted 'ageRatings'
}

function Invoke-Packages {
    Navigate-Url -Url (Get-PhaseUrl 'packages') -Label 'packages'
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message 'packages: upload MSIX, desktop only, save'; return }
    $assets = Get-PropertyValue $script:Config 'assets'
    $msix = [string](Get-PropertyValue $assets 'msix' '')
    if ([string]::IsNullOrWhiteSpace($msix)) { Stop-WithCode 12 'assets.msix is missing from manifest.' }
    
    $packageName = [IO.Path]::GetFileName($msix)
    $packageAlreadyPresent = [bool](Invoke-PageJs "(document.body && document.body.innerText || '').includes($(ConvertTo-JsLiteral $packageName))")
    if ($packageAlreadyPresent) {
        Write-Log -Level 'INFO' -Message "package already uploaded: $packageName"
    }
    else {
        Upload-File -ContextTexts @('Drag your packages here', '\u7a0b\u5e8f\u5305', '.msix') -Path $msix -Label 'MSIX package'
    }
    Wait-ForText -Text 'Windows 10/11 Desktop' -Seconds $TimeoutSeconds
    Set-CheckboxByText -Text 'Windows 10/11 Desktop' -Checked $true -Label 'desktop device family'
    Set-CheckboxByText -Text 'Windows 10 Mobile' -Checked $false -Label 'mobile device family'
    Set-CheckboxByText -Text 'Windows 10/11 Xbox' -Checked $false -Label 'xbox device family'
    Set-CheckboxByText -Text 'Windows 10 Team' -Checked $false -Label 'team device family'
    Set-CheckboxByText -Text 'Windows 10 Mixed Reality' -Checked $false -Label 'mixed reality device family'
    Set-CheckboxByText -Text 'future device families' -Checked $true -Label 'future device families'
    Save-CurrentPage -Phase 'packages'
    Mark-PhaseCompleted 'packages'
}

function Invoke-Listing {
    Navigate-Url -Url (Get-PhaseUrl 'listing') -Label 'Store listing Chinese'
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message 'listing: description, features, screenshots, logos, keywords, save'; return }
    $values = Get-PropertyValue $script:Config 'values'
    $assets = Get-PropertyValue $script:Config 'assets'

    $description = [string](Get-PropertyValue $values 'description' '')
    if ([string]::IsNullOrWhiteSpace($description)) { Stop-WithCode 12 'values.description is missing.' }
    Set-FieldStrict -Selectors @('#description-required') -Value $description -Label 'description'

    $features = @(Get-PropertyValue $values 'features' @())
    for ($featureIndex = 0; $featureIndex -lt $features.Count; $featureIndex++) {
        $featureSelector = "#feature-$featureIndex"
        if (-not (Test-VisibleSelector $featureSelector)) {
            Click-TextStrict -Text '\u6dfb\u52a0\u5176\u4ed6\u9879\u76ee' -Label 'add product feature'
            if (-not (Wait-ForPagePredicate -Expression "document.querySelector($(ConvertTo-JsLiteral $featureSelector)) !== null" -Seconds $TimeoutSeconds)) {
                Stop-WithCode 7 "New product feature input did not appear: $featureSelector"
            }
        }
        Set-FieldStrict -Selectors @($featureSelector) -Value ([string]$features[$featureIndex]) -Label "feature $($featureIndex + 1)"
    }

    $shortDescription = [string](Get-PropertyValue $values 'shortDescription' '')
    if ($shortDescription) { Set-FieldStrict -Selectors @('#shortDescription') -Value $shortDescription -Label 'short description' }
    
    $keywords = @(Get-PropertyValue $values 'keywords' @())
    Set-Keywords -Keywords $keywords

    $uploads = @(
        @{ key = 'screenshot'; label = 'desktop screenshot'; contexts = @(); inputIndex = 0 },
        @{ key = 'poster'; label = '9:16 poster'; contexts = @('9:16', '\u62db\u8d34\u753b') },
        @{ key = 'boxart'; label = '1:1 box art'; contexts = @('1:1', '\u9177\u56fe') },
        @{ key = 'logo300'; label = '300x300 logo'; contexts = @('300x300', '300 x 300') },
        @{ key = 'logo150'; label = '150x150 logo'; contexts = @('150x150', '150 x 150') },
        @{ key = 'logo71'; label = '71x71 logo'; contexts = @('71x71', '71 x 71') },
        @{ key = 'superhero'; label = '16:9 superhero art'; contexts = @('16:9', '\u8d85\u7ea7\u82f1\u96c4\u753b') }
    )
    foreach ($upload in $uploads) {
        $path = [string](Get-PropertyValue $assets $upload.key '')
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $enabled = [bool](Get-PropertyValue (Get-PropertyValue $script:Config 'listing') $upload.key $false)
        if (-not $enabled) { continue }
        $inputIndex = -1
        if ($upload.ContainsKey('inputIndex')) { $inputIndex = [int]$upload.inputIndex }
        if (Test-SectionHasImage -ContextTexts $upload.contexts -InputIndex $inputIndex) {
            Write-Log -Level 'INFO' -Message "already present: $($upload.label)"
            continue
        }
        Upload-File -ContextTexts $upload.contexts -Path $path -Label $upload.label -InputIndex $inputIndex
    }
    Save-CurrentPage -Phase 'listing'
    Mark-PhaseCompleted 'listing'
}

function Invoke-Options {
    Navigate-Url -Url (Get-PhaseUrl 'options') -Label 'submission options'
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message 'options: manual publish mode, runFullTrust explanation, save'; return }
    $options = Get-PropertyValue $script:Config 'submissionOptions'
    $publishMode = [string](Get-PropertyValue $options 'publishMode' 'Manual')
    if ($publishMode -eq 'Manual') {
        Set-RadioStrict -Selector 'input#radioReleaseDate_manual' -Label 'manual publish mode'
    }
    else {
        Set-RadioStrict -Selector 'input#radioReleaseDate_asap' -Label 'ASAP publish mode'
    }

    $reason = [string](Get-PropertyValue $options 'runFullTrustReason' '\u8fd9\u662f\u4e00\u4e2a WinUI 3 \u684c\u9762\u5e94\u7528\uff0c\u9700\u8981\u4ee5\u5168\u4fe1\u4efb\u684c\u9762\u8fdb\u7a0b\u8fd0\u884c\u624d\u80fd\u6b63\u5e38\u542f\u52a8\u5e76\u63d0\u4f9b\u672c\u5730\u901a\u77e5\u3001\u6587\u4ef6\u548c\u7cfb\u7edf\u96c6\u6210\u529f\u80fd\u3002\u5e94\u7528\u4ec5\u5728\u7528\u6237\u672c\u673a\u8fd0\u884c\uff0c\u4e0d\u8bbf\u95ee\u6216\u4fee\u6539\u5176\u4ed6\u7528\u6237\u7684\u6570\u636e\u3002')
    $hasFullTrustBox = [bool](Invoke-PageJs "(() => Array.from(document.querySelectorAll('textarea')).some(e => (e.parentElement?.parentElement?.innerText||'').includes('\u4e3a\u4f55\u9700\u8981\u4f7f\u7528')))()")
    if ($hasFullTrustBox) {
        $result = Invoke-PageJs @"
(() => {
 const needle='\u4e3a\u4f55\u9700\u8981\u4f7f\u7528', els=Array.from(document.querySelectorAll('textarea')).filter(e=>(e.parentElement?.parentElement?.innerText||'').includes(needle));
 if(els.length!==1)return {ok:false,count:els.length};
 const e=els[0], setter=Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value');
 setter.set.call(e,$(ConvertTo-JsLiteral $reason));
 e.dispatchEvent(new Event('input',{bubbles:true})); e.dispatchEvent(new Event('change',{bubbles:true}));
 return {ok:true};
})()
"@
        if ($result.ok) { Write-Log -Level 'PASS' -Message 'filled runFullTrust restricted capability justification' }
    }

    Save-CurrentPage -Phase 'options'
    Mark-PhaseCompleted 'options'
}

function Invoke-Run {
    Invoke-PreflightStore0
    Ensure-SignedIn
    $null = Get-ProductId
    Ensure-Submission
    switch ($Phase) {
        'all' {
            Invoke-Availability
            Invoke-Properties
            Invoke-AgeRatings
            Invoke-Packages
            Invoke-Listing
            Invoke-Options
            if ($Submit) { Invoke-FinalSubmit }
        }
        'availability' { Invoke-Availability }
        'properties' { Invoke-Properties }
        'ageRatings' { Invoke-AgeRatings }
        'packages' { Invoke-Packages }
        'listing' { Invoke-Listing }
        'options' { Invoke-Options }
    }
}

function Invoke-FinalSubmit {
    if (-not $Apply) { Stop-WithCode 13 'Final submission requires -Apply.' }
    if (-not $Submit -or -not $ConfirmSubmit) { Stop-WithCode 13 'Final submission requires both -Submit and -ConfirmSubmit.' }
    if ($Phase -ne 'all') { Stop-WithCode 13 'Final submission requires -Phase all.' }
    $product = Get-ProductId
    $submission = Get-SubmissionId
    Navigate-Url -Url (Get-PhaseUrl 'overview') -Label 'submission overview'
    Assert-NoVisibleErrors
    $completed = @(Get-CompletedPhases)
    foreach ($phase in @('availability', 'properties', 'ageRatings', 'packages', 'listing', 'options')) {
        if ($completed -notcontains $phase) { Stop-WithCode 13 "Phase is not complete: $phase" }
    }
    Write-Log -Level 'WARN' -Message "final action: submit product=$product submission=$submission"
    Click-SelectorStrict @('button[data-l10n-key="AppSubmission_PublishButton"]', 'button[data-l10n-key="SubmitToStore"]', 'a[data-l10n-key="AppSubmission_PublishButton"]') 'Submit to Store' -NativeClick
    Start-Sleep -Seconds 3
    Write-Log -Level 'PASS' -Message 'submitted successfully to Microsoft Store review'
}

function Invoke-Inspect {
    Ensure-SignedIn
    $product = Get-ProductId
    $liveFound = Discover-LiveSubmissionUrls
    $info = Get-PageInfo

    $inspection = [ordered]@{
        generatedAt = (Get-Date).ToString('o')
        productId = $product
        submissionId = [string]$script:State.submissionId
        liveDiscovery = $liveFound
        discoveredHrefs = $script:LiveState.discoveredHrefs
        page = $info
        stableSelectors = [ordered]@{
            startSubmission = (Test-VisibleSelector 'he-button[data-l10n-key="Start_Submission"],button[data-l10n-key="Start_Submission"]')
            availabilitySave = (Test-VisibleSelector 'input#saveButtonPricing,button#saveButtonPricing')
            propertiesCategory = (Test-VisibleSelector 'select[name="CategorySelect"]')
            ageMode = (Test-VisibleSelector 'input[name="inputMode"]')
            packageUpload = (Test-VisibleSelector 'input[type="file"]')
            listingDescription = (Test-VisibleSelector '#description-required')
            optionsManual = (Test-VisibleSelector '#radioReleaseDate_manual')
        }
        visibleErrors = @(Get-VisibleErrors)
    }
    $path = Join-Path $script:StateRoot ("inspect-{0}.json" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $inspection | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding UTF8
    Write-Log -Level 'PASS' -Message "inspection written: $path"
}

function Invoke-Identity {
    $product = Get-ProductId
    Navigate-Url -Url "$(Get-BaseUrl)/$product/overview" -Label 'product identity'
    if ([bool](Invoke-PageJs "document.querySelector('#collapseApplicationIdentity') !== null")) {
        $expanded = [bool](Invoke-PageJs "(() => { const e=document.querySelector('#collapseApplicationIdentity'); return e && !e.classList.contains('collapse'); })()")
        if (-not $expanded) {
            Click-SelectorStrict @('a[aria-controls="collapseApplicationIdentity"]', '[data-target="#collapseApplicationIdentity"]') 'expand product identity' -NativeClick
            Start-Sleep -Milliseconds 500
        }
    }
    $identity = Invoke-PageJs @'
(() => Array.from(document.querySelectorAll('#collapseApplicationIdentity li')).map(li => {
 const key=li.querySelector('.key')?.innerText?.trim()||'', value=li.querySelector('.app-id-contents')?.innerText?.trim()||''; return {key,value};
}).filter(x=>x.key&&x.value))()
'@
    $required = @('Package/Identity/Name', 'Package/Identity/Publisher', 'Package/Properties/PublisherDisplayName')
    foreach ($key in $required) {
        if (-not @($identity | Where-Object { $_.key -eq $key }).Count) {
            Stop-WithCode 7 "Product identity field missing: $key"
        }
    }
    $path = Join-Path $script:StateRoot 'product-identity.json'
    $identity | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $path -Encoding UTF8
    Write-Log -Level 'PASS' -Message "product identity written: $path"
}

function Invoke-Status {
    $info = Get-PageInfo
    Write-Log -Level 'INFO' -Message "url=$($info.url) title=$($info.title) login=$($info.hasLogin) visibleErrors=$($info.visibleErrorCount)"
    Write-Log -Level 'INFO' -Message "product=$($script:State.productId) submission=$($script:State.submissionId) completed=$((Get-CompletedPhases) -join ',')"
}

function Stop-Edge {
    if ($script:StartedByUs -and $null -ne $script:EdgeProcess -and -not $script:EdgeProcess.HasExited) {
        $script:EdgeProcess.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 500
        if (-not $script:EdgeProcess.HasExited) { $script:EdgeProcess.Kill() }
        Write-Log -Level 'INFO' -Message 'stopped isolated Edge process'
    }
}

function Close-Cdp {
    if ($null -ne $script:CdpSocket) {
        try { [void]($script:CdpSocket.CloseAsync([Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'done', [Threading.CancellationToken]::None).GetAwaiter().GetResult()) } catch { }
        $script:CdpSocket.Dispose()
        $script:CdpSocket = $null
    }
}

try {
    New-Item -ItemType Directory -Force -Path $script:StateRoot, $script:LogRoot | Out-Null
    if ([string]::IsNullOrWhiteSpace($Manifest)) {
        $Manifest = Join-Path $script:ToolRoot 'examples\store-automation.json'
    }
    $script:ManifestPath = (Resolve-Path -LiteralPath $Manifest).Path
    $script:Config = Read-JsonFile $script:ManifestPath
    Resolve-ConfigPaths -Config $script:Config -BaseDirectory (Split-Path -Parent $script:ManifestPath)
    $listingMarkdown = [string](Get-PropertyValue $script:Config 'listingMarkdown' '')
    if ($listingMarkdown) {
        $script:Config | Add-Member -Force -NotePropertyName listingMarkdown -NotePropertyValue (Resolve-ManifestPath $listingMarkdown (Split-Path -Parent $script:ManifestPath))
    }
    Import-ListingMarkdown
    Initialize-State

    if ($Action -eq 'preflight') {
        Invoke-PreflightStore0
        exit 0
    }

    $requestedProduct = [string](Get-PropertyValue $script:Config 'productId' '')
    if (-not [string]::IsNullOrWhiteSpace($ProductId)) { $requestedProduct = $ProductId }
    if ($requestedProduct -and [string]$script:State.productId -and [string]$script:State.productId -ne $requestedProduct) {
        Write-Log -Level 'WARN' -Message 'product changed; clearing previous submission checkpoint'
        $script:State.productId = $requestedProduct
        $script:State.submissionId = ''
        $script:State.completed = @()
        $script:State.currentPhase = ''
    }
    if ($requestedProduct -and -not [string]$script:State.productId) { $script:State.productId = $requestedProduct }
    Acquire-RunMutex
    if (-not [string]::IsNullOrWhiteSpace($ProductId)) { $script:State.productId = $ProductId }
    if (-not [string]::IsNullOrWhiteSpace($SubmissionId)) { $script:State.submissionId = $SubmissionId }
    Save-State

    if ($Action -eq 'stop') {
        if (Test-Path -LiteralPath (Join-Path $script:StateRoot 'edge.pid')) {
            $savedPid = [int](Get-Content -LiteralPath (Join-Path $script:StateRoot 'edge.pid'))
            $process = Get-Process -Id $savedPid -ErrorAction SilentlyContinue
            if ($null -ne $process) { $process.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 300; if (-not $process.HasExited) { $process.Kill() } }
        }
        Write-Log -Level 'PASS' -Message 'isolated Edge stop requested'
        Release-RunMutex
        exit 0
    }

    Start-Edge
    Connect-Cdp
    Update-IdsFromUrl
    if ($Action -eq 'launch') {
        Ensure-SignedIn
        Write-Log -Level 'PASS' -Message 'Edge is ready. User session established.'
    }
    elseif ($Action -eq 'inspect') {
        Invoke-Inspect
    }
    elseif ($Action -eq 'identity') {
        Ensure-SignedIn
        Invoke-Identity
    }
    elseif ($Action -eq 'status') {
        Invoke-Status
    }
    elseif ($Action -eq 'run') {
        Invoke-Run
    }
    else {
        Stop-WithCode 2 "Unknown action: $Action"
    }

    if (-not $KeepOpen -and $Action -ne 'launch') { Close-Cdp; Stop-Edge }
    Release-RunMutex
    exit 0
}
catch [System.OperationCanceledException] {
    if ($null -eq $script:ExitCode) { $script:ExitCode = 1 }
    Close-Cdp
    if ($Action -ne 'launch' -and -not $KeepOpen) { Stop-Edge }
    Release-RunMutex
    exit $script:ExitCode
}
catch {
    Write-Log -Level 'ERROR' -Message $_.Exception.Message
    Write-Log -Level 'ERROR' -Message ("STACK: " + ($_.ScriptStackTrace -replace '\r?\n', ' | '))
    Close-Cdp
    if ($Action -ne 'launch' -and -not $KeepOpen) { Stop-Edge }
    Release-RunMutex
    exit 1
}
