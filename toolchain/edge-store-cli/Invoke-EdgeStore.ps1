[CmdletBinding()]
param(
    [ValidateSet('launch', 'inspect', 'identity', 'run', 'status', 'stop')]
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
    [int]$TimeoutSeconds = 45,
    [int]$LoginTimeoutSeconds = 900
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ToolRoot = $PSScriptRoot
$script:StateRoot = Join-Path $script:ToolRoot 'state'
$script:LogRoot = Join-Path $script:StateRoot 'logs'
$script:StatePath = Join-Path $script:StateRoot 'store-state.json'
$script:Port = 0
$script:EdgeProcess = $null
$script:CdpSocket = $null
$script:CdpNextId = 1
$script:Config = $null
$script:State = $null
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
    if ($Action -eq 'stop') { return }
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

    $shortMatch = [regex]::Match($text, '(?ms)^##\s*简短摘要.*?\r?\n\r?\n(?<value>.*?)(?=\r?\n\r?\n##\s*完整描述)')
    $fullMatch = [regex]::Match($text, '(?ms)^##\s*完整描述.*?\r?\n(?<value>.*?)(?=\r?\n\r?\n##\s*产品功能)')
    $featuresMatch = [regex]::Match($text, '(?ms)^##\s*产品功能.*?\r?\n(?<value>.*?)(?=\r?\n\r?\n##\s*搜索关键词)')
    $keywordsMatch = [regex]::Match($text, '(?ms)^##\s*搜索关键词.*?\r?\n\r?\n(?<value>[^\r\n]+)')

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
            schemaVersion = 1
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
        $pid = [int](Get-Content -LiteralPath $pidPath -Raw)
        $port = [int](Get-Content -LiteralPath $portPath -Raw)
        $process = Get-Process -Id $pid -ErrorAction SilentlyContinue
        if ($null -eq $process -or $process.HasExited) { return $false }
        [void](Wait-Http -Uri "http://127.0.0.1:$port/json/version" -Seconds 3)
        $script:EdgeProcess = $process
        $script:Port = $port
        $script:StartedByUs = $false
        Write-Log -Level 'INFO' -Message "reusing isolated Edge process pid=$pid port=$port"
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

function Get-CdpTarget {
    $targets = Wait-Http -Uri "http://127.0.0.1:$script:Port/json/list" -Seconds 30
    $pages = @($targets | Where-Object { $_.type -eq 'page' -and $_.webSocketDebuggerUrl })
    if ($pages.Count -eq 0) { Stop-WithCode 4 'No Edge page target is available.' }
    $partner = @($pages | Where-Object { $_.url -like '*partner.microsoft.com*' })
    if ($partner.Count -gt 0) { return $partner[0] }
    return $pages[0]
}

function Connect-Cdp {
    $target = Get-CdpTarget
    $script:CdpSocket = [Net.WebSockets.ClientWebSocket]::new()
    $script:CdpSocket.ConnectAsync([Uri]$target.webSocketDebuggerUrl, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $script:CdpNextId = 1
    [void](Send-CdpRequest -Method 'Page.enable' -Params @{})
    [void](Send-CdpRequest -Method 'Runtime.enable' -Params @{})
    Write-Log -Level 'INFO' -Message "connected to Edge page target"
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
    $script:CdpSocket.SendAsync($segment, [Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).GetAwaiter().GetResult()

    while ($true) {
        $buffer = New-Object byte[] 65536
        $receivedBytes = [ArraySegment[byte]]::new($buffer)
        $builder = [Text.StringBuilder]::new()
        do {
            $received = $script:CdpSocket.ReceiveAsync($receivedBytes, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
            if ($received.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) {
                Stop-WithCode 4 'Edge DevTools WebSocket closed unexpectedly.'
            }
            [void]$builder.Append([Text.Encoding]::UTF8.GetString($buffer, 0, $received.Count))
        } while (-not $received.EndOfMessage)

        $response = $builder.ToString() | ConvertFrom-Json
        $responseId = Get-PropertyValue $response 'id' $null
        if ($null -ne $responseId -and [int]$responseId -eq $id) {
            $errorObject = Get-PropertyValue $response 'error' $null
            if ($null -ne $errorObject) {
                Stop-WithCode 5 "CDP $Method failed: $(Get-PropertyValue $errorObject 'message' 'unknown CDP error')"
            }
            return $response
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
    $result = Get-PropertyValue $response 'result' $null
    $exceptionDetails = Get-PropertyValue $result 'exceptionDetails' $null
    if ($null -ne $exceptionDetails) {
        Stop-WithCode 5 "Page JavaScript failed: $(Get-PropertyValue $exceptionDetails 'text' 'unknown JavaScript error')"
    }
    $remoteResult = Get-PropertyValue $result 'result' $null
    if ($remoteResult.PSObject.Properties.Name -contains 'value') { return $remoteResult.value }
    return (Get-PropertyValue $remoteResult 'description' $null)
}

function ConvertTo-JsLiteral {
    param([AllowNull()][object]$Value)
    return ($Value | ConvertTo-Json -Depth 30 -Compress)
}

function Invoke-PageJsWithRetry {
    param([string]$Expression, [int]$Seconds = $TimeoutSeconds)
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try { return Invoke-PageJs -Expression $Expression }
        catch {
            if ((Get-Date) -ge $deadline) { throw }
            Start-Sleep -Milliseconds 300
        }
    } while ((Get-Date) -lt $deadline)
}

function Get-PageInfo {
    return Invoke-PageJs @'
(() => ({
  url: location.href,
  title: document.title,
  bodyLength: (document.body && document.body.innerText || '').length,
  hasLogin: /login\.microsoftonline|login\.live\.com|signin/i.test(location.href),
  visibleErrorCount: Array.from(document.querySelectorAll('.alert-error, .alert-danger, [role="alert"].alert-error, .has-error')).filter(e => {
    const r=e.getBoundingClientRect(), s=getComputedStyle(e), t=(e.innerText||'').trim(); return r.width>0 && r.height>0 && s.display!=='none' && s.visibility!=='hidden' && /不能为空|必须|错误|error|failed|invalid|至少一张|唯一标识|删除其中/i.test(t);
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

function Click-SelectorStrict {
    param([string[]]$Selectors, [string]$Label)
    $literal = ConvertTo-JsLiteral @($Selectors)
    $result = Invoke-PageJs @"
(() => {
  const selectors=$literal, seen=new Set(), found=[];
  const visible=e=>{const r=e.getBoundingClientRect(),s=getComputedStyle(e);return r.width>0&&r.height>0&&s.display!=='none'&&s.visibility!=='hidden'&&!e.disabled;};
  for(const selector of selectors){ for(const e of document.querySelectorAll(selector)){ if(visible(e)&&!seen.has(e)){seen.add(e);found.push({e,selector});} } }
  if(found.length!==1) return {ok:false,count:found.length,selectors:found.map(x=>x.selector)};
  found[0].e.click(); return {ok:true,selector:found[0].selector};
})()
"@
    if (-not $result.ok) {
        Stop-WithCode 7 "UI schema mismatch for [$Label]: visible matches=$($result.count)"
    }
    Write-Log -Level 'INFO' -Message "click $Label via $($result.selector)"
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
(() => { const e=document.querySelector($(ConvertTo-JsLiteral $Selector)); if(!e)return {ok:false}; e.click(); return {ok:true,checked:e.checked}; })()
"@
    if (-not $result.ok) { Stop-WithCode 7 "UI schema mismatch for radio [$Label]" }
    Write-Log -Level 'INFO' -Message "select $Label"
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
 let changed=0;
 for(const e of boxes){const current=e.checked===true||e.hasAttribute('checked')||e.getAttribute('aria-checked')==='true';if(current!==want){e.click();changed++;}}
 return {ok:true,count:boxes.length,changed};
})()
"@
    if (-not $result.ok) { Stop-WithCode 7 "UI schema mismatch for checkbox [$Text]" }
    $displayLabel = $Text
    if (-not [string]::IsNullOrWhiteSpace($Label)) { $displayLabel = $Label }
    Write-Log -Level 'INFO' -Message "checkbox $displayLabel => $Checked ($($result.count) matches)"
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
 els[0].click(); return {ok:true};
})()
"@
    if (-not $result.ok) { Stop-WithCode 7 "UI schema mismatch for text [$Label]: matches=$($result.count)" }
    Write-Log -Level 'INFO' -Message "click text $Label"
}

function Get-VisibleErrors {
    return Invoke-PageJs @'
(() => Array.from(document.querySelectorAll('.alert-error, .alert-danger, [role="alert"].alert-error, .has-error')).filter(e => {
  const r=e.getBoundingClientRect(), s=getComputedStyle(e), t=(e.innerText||'').trim();
  return r.width>0 && r.height>0 && s.display!=='none' && s.visibility!=='hidden' && /不能为空|必须|错误|error|failed|invalid|至少一张|唯一标识|删除其中/i.test(t);
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
    Write-Log -Level 'INFO' -Message "navigate $Label"
    [void](Send-CdpRequest -Method 'Page.navigate' -Params @{ url = $Url })
    Wait-ForUrl -Pattern $Url.Split('?')[0] -Seconds $TimeoutSeconds
    Start-Sleep -Milliseconds 800
}

function Ensure-SignedIn {
    $info = Get-PageInfo
    if ($info.hasLogin -or $info.url -notlike '*partner.microsoft.com*') {
        Write-Log -Level 'WAIT' -Message 'Edge is waiting for the user to complete Microsoft sign-in/MFA in the isolated Edge window.'
        $deadline = (Get-Date).AddSeconds($LoginTimeoutSeconds)
        do {
            Start-Sleep -Seconds 2
            $info = Get-PageInfo
            if (-not $info.hasLogin -and $info.url -like '*partner.microsoft.com*') { break }
        } while ((Get-Date) -lt $deadline)
        if ($info.hasLogin -or $info.url -notlike '*partner.microsoft.com*') {
            Stop-WithCode 10 'Sign-in was not completed before the login timeout. The Edge window is left open for resume.'
        }
    }
    Write-Log -Level 'PASS' -Message 'Partner Center session is available'
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
    if ([string]::IsNullOrWhiteSpace($id)) { Stop-WithCode 11 'ProductId is missing. Pass -ProductId or place the browser on a Partner Center product page.' }
    $script:State.productId = $id
    return $id
}

function Get-SubmissionId {
    $id = [string](Get-PropertyValue $script:Config 'submissionId' '')
    if (-not [string]::IsNullOrWhiteSpace($SubmissionId)) { $id = $SubmissionId }
    if ([string]::IsNullOrWhiteSpace($id)) { $id = [string](Get-PropertyValue $script:State 'submissionId' '') }
    if ([string]::IsNullOrWhiteSpace($id)) { Update-IdsFromUrl; $id = [string]$script:State.submissionId }
    if ([string]::IsNullOrWhiteSpace($id)) { Stop-WithCode 11 'SubmissionId is missing. Start a submission in Partner Center or pass -SubmissionId.' }
    $script:State.submissionId = $id
    return $id
}

function Ensure-Submission {
    $product = Get-ProductId
    $existing = [string](Get-PropertyValue $script:State 'submissionId' '')
    if (-not [string]::IsNullOrWhiteSpace($SubmissionId)) { $existing = $SubmissionId }
    if (-not [string]::IsNullOrWhiteSpace([string](Get-PropertyValue $script:Config 'submissionId' ''))) {
        $existing = [string](Get-PropertyValue $script:Config 'submissionId' '')
    }
    if (-not [string]::IsNullOrWhiteSpace($existing)) {
        $script:State.submissionId = $existing
        Save-State
        return
    }

    $overview = "$(Get-BaseUrl)/$product/overview"
    Navigate-Url -Url $overview -Label 'product overview before starting submission'
    if (-not $Apply) {
        Write-Log -Level 'PLAN' -Message 'would click Start Submission to create a draft submission; pass -Apply to create it'
        Stop-WithCode 14 'SubmissionId is missing in dry-run mode. Pass -SubmissionId or rerun with -Apply to create a draft submission.'
    }
    Click-SelectorStrict @('he-button[data-l10n-key="Start_Submission"]', 'button[data-l10n-key="Start_Submission"]', '[data-automation-id="Start_Submission"]') 'Start Submission'
    Wait-ForUrl -Pattern '/submissions/' -Seconds $TimeoutSeconds
    Update-IdsFromUrl
    if ([string]::IsNullOrWhiteSpace([string]$script:State.submissionId)) {
        Stop-WithCode 11 'Partner Center created a submission page but its submission ID was not found in the URL.'
    }
    Write-Log -Level 'PASS' -Message "draft submission created: $($script:State.submissionId)"
}

function Get-BaseUrl {
    return [string](Get-PropertyValue (Get-PropertyValue $script:Config 'site') 'baseUrl' 'https://partner.microsoft.com/zh-cn/dashboard/products')
}

function Get-PhaseUrl {
    param([string]$Phase)
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
        'availability' { Click-SelectorStrict @('input#saveButtonPricing', 'button#saveButtonPricing', 'input[uitestid="saveButtonPricing"]', 'button[data-l10n-key="AppSubmission_SaveButton"]') 'availability save' }
        'properties' { Click-SelectorStrict @('button[data-l10n-key="appsubmission_savebutton"]', 'input[value="保存"]', 'button[value="保存"]') 'properties save' }
        'ageRatings' { Click-SelectorStrict @('he-button[data-l10n-key="AppSubmission_AgeRating_SaveDraftButton"]', 'button[data-l10n-key="AppSubmission_AgeRating_SaveDraftButton"]') 'age ratings save draft' }
        'packages' { Click-SelectorStrict @('input[value="Save"]', 'button[value="Save"]', 'button[data-l10n-key="appsubmission_savebutton"]') 'packages save' }
        'listing' { Click-SelectorStrict @('button[name="save_button"]', 'button[data-l10n-key="appsubmission_savebutton"]') 'listing save' }
        'options' { Click-SelectorStrict @('button[data-l10n-key="optionsSave"]', 'button[data-l10n-key="appsubmission_savebutton"]') 'options save' }
    }
    Start-Sleep -Seconds 2
    Assert-NoVisibleErrors
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
    Write-Log -Level 'INFO' -Message "uploaded $Label"
    Start-Sleep -Seconds 2
}

function Assert-PackageName {
    $expected = [string](Get-PropertyValue $script:Config 'productName' '')
    if ([string]::IsNullOrWhiteSpace($expected)) { return }
    $assets = Get-PropertyValue $script:Config 'assets'
    $msix = [string](Get-PropertyValue $assets 'msix' '')
    if (-not (Test-Path -LiteralPath $msix)) { Stop-WithCode 12 "MSIX not found for identity check: $msix" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = $null
    $reader = $null
    try {
        $archive = [IO.Compression.ZipFile]::OpenRead($msix)
        $entry = $archive.Entries | Where-Object { $_.FullName -ieq 'AppxManifest.xml' } | Select-Object -First 1
        if ($null -eq $entry) { Stop-WithCode 12 'AppxManifest.xml was not found inside the MSIX.' }
        $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8)
        $manifestText = $reader.ReadToEnd()
        $manifestNameElement = "<DisplayName>$expected</DisplayName>"
        $manifestVisualName = 'DisplayName="' + $expected + '"'
        if (-not $manifestText.Contains($manifestNameElement) -and -not $manifestText.Contains($manifestVisualName)) {
            Stop-WithCode 12 "Package display name does not contain the reserved product name [$expected]. Rebuild the Store MSIX before uploading."
        }
        Write-Log -Level 'PASS' -Message "MSIX display name matches product name [$expected]"
    }
    finally {
        if ($null -ne $reader) { $reader.Dispose() }
        if ($null -ne $archive) { $archive.Dispose() }
    }
}

function Set-Keywords {
    param([string[]]$Keywords)
    if ($Keywords.Count -eq 0) { return }
    $existingKeywords = @(Invoke-PageJs @'
(() => { const root=document.querySelector('#search-terms')||document.querySelector('he-select[multiple]'); if(!root)return []; return Array.from(root.querySelectorAll('he-option')).filter(e=>(e.getAttribute('slot')||'').startsWith('selected-')||e.getAttribute('role')==='listitem').map(e=>(e.innerText||e.getAttribute('value')||'').trim()).filter(Boolean); })()
'@)
    foreach ($keyword in $Keywords) {
        if ($existingKeywords -contains [string]$keyword) {
            Write-Log -Level 'INFO' -Message 'keyword already present; skip duplicate'
            continue
        }
        if (-not $Apply) { Write-Log -Level 'PLAN' -Message "dry-run: would add keyword"; continue }
        Click-SelectorStrict @('#search-terms he-select', 'he-select[multiple]') 'keyword control'
        [void](Send-CdpRequest -Method 'Input.insertText' -Params @{ text = [string]$keyword })
        [void](Send-CdpRequest -Method 'Input.dispatchKeyEvent' -Params @{ type = 'keyDown'; key = 'Enter'; code = 'Enter'; windowsVirtualKeyCode = 13; nativeVirtualKeyCode = 13 })
        [void](Send-CdpRequest -Method 'Input.dispatchKeyEvent' -Params @{ type = 'keyUp'; key = 'Enter'; code = 'Enter'; windowsVirtualKeyCode = 13; nativeVirtualKeyCode = 13 })
        Start-Sleep -Milliseconds 250
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
 if(radios.length!==1)return {ok:false,count:radios.length}; radios[0].click(); return {ok:true};
})()
"@
    if (-not $result.ok) { Stop-WithCode 7 "Age rating question $QuestionId [$AnswerText] matched $($result.count) elements" }
    Write-Log -Level 'INFO' -Message "age question $QuestionId => $AnswerText"
    Start-Sleep -Milliseconds 250
}

function Invoke-Availability {
    Navigate-Url -Url (Get-PhaseUrl 'availability') -Label 'pricing and availability'
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message 'availability: global markets, ASAP, never stop selling, CNY currency, price tier 0, save'; return }
    Set-RadioStrict -Selector 'input[name="marketSelection"][value="true"]' -Label 'all markets'
    if (Test-VisibleSelector '#distributionOption') { Set-FieldStrict -Selectors @('#distributionOption') -Value 'string:Retail' -Label 'retail distribution' }
    if (Test-VisibleSelector '#radioDistribution_PublicAudience') { Set-RadioStrict -Selector '#radioDistribution_PublicAudience' -Label 'public audience' }
    Set-FieldStrict -Selectors @('select[uitestid="AvailableSelector-0"]') -Value 'string:asap' -Label 'publish ASAP'
    Set-FieldStrict -Selectors @('select[uitestid="StopSellingSelector-0"]') -Value 'string:auto-fill' -Label 'never stop selling'
    $pricing = Get-PropertyValue $script:Config 'pricing'
    $currency = [string](Get-PropertyValue $pricing 'currency' 'CN')
    $priceTier = [string](Get-PropertyValue $pricing 'priceTier' '0')
    $currentCurrency = [string](Invoke-PageJs "(() => { const e=document.querySelector('market-group .price-config > he-select'); return e ? (e.getAttribute('value')||'') : ''; })()")
    if ($currentCurrency -ne $currency) {
        Click-SelectorStrict @('market-group .price-config > he-select') 'base currency'
        Click-TextStrict -Text 'CNY - 中国' -Label 'CNY currency'
    }
    $currentTier = [string](Invoke-PageJs "(() => { const e=document.querySelector('market-group price-tier-selection he-select'); return e ? (e.getAttribute('value')||'') : ''; })()")
    if ($currentTier -ne $priceTier) {
        if (-not (Wait-ForPagePredicate -Expression "(() => { const e=document.querySelector('market-group price-tier-selection he-select'); return e && !e.hasAttribute('disabled'); })()" -Seconds $TimeoutSeconds)) {
            Stop-WithCode 7 'Price tier control did not become available after selecting currency.'
        }
        Click-SelectorStrict @('market-group price-tier-selection he-select') 'base price tier'
        Click-TextStrict -Text '0' -Label 'zero price tier'
    }
    $pricingState = Invoke-PageJs @'
(() => { const c=document.querySelector('market-group .price-config > he-select'), p=document.querySelector('market-group price-tier-selection he-select'); return {currency:(c?.getAttribute('value')||''),priceTier:(p?.getAttribute('value')||'')}; })()
'@
    if ([string]$pricingState.currency -ne $currency -or [string]$pricingState.priceTier -ne $priceTier) {
        Stop-WithCode 7 "Pricing verification failed: currency=$($pricingState.currency) priceTier=$($pricingState.priceTier)"
    }
    Write-Log -Level 'PASS' -Message "pricing verified: currency=$currency priceTier=$priceTier"
    Save-CurrentPage -Phase 'availability'
    Mark-PhaseCompleted 'availability'
}

function Invoke-Properties {
    Navigate-Url -Url (Get-PhaseUrl 'properties') -Label 'properties'
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message 'properties: Productivity, no personal information, privacy policy text, desktop/x64, keep storage/backups/windows defaults, save'; return }
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
        if (-not (Wait-ForPagePredicate -Expression 'document.querySelector(''textarea[aria-label="提供隐私策略文本"]'') !== null' -Seconds $TimeoutSeconds)) { Stop-WithCode 7 'Privacy policy text area did not appear.' }
        Set-FieldStrict -Selectors @('support-info textarea[aria-label="提供隐私策略文本"]', 'textarea[aria-label="提供隐私策略文本"]') -Value $privacyText -Label 'privacy policy text'
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
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message 'age ratings: questionnaire, other application type 2558, physical media no, all follow-up answers no, agree, save, continue'; return }
    Set-RadioStrict -Selector 'input[name="inputMode"][value="questionnaire"]' -Label 'IARC questionnaire'
    Set-RadioStrict -Selector 'input[name="question#1109"][value="2558"]' -Label 'other application type'
    Set-RadioStrict -Selector '#radioGroup input#noVal' -Label 'physical media no'
    $questionIds = @('1152', '1188', '1193', '1037', '1194', '1195', '1375', '1196', '1197')
    foreach ($questionId in $questionIds) { Set-AgeAnswer -QuestionId $questionId -AnswerText '否' }
    Click-SelectorStrict @('he-checkbox[required]', 'he-checkbox') 'IARC terms agreement'
    Click-SelectorStrict @('he-button[data-l10n-key="AppSubmission_AgeRating_SaveButton"]', 'button[data-l10n-key="AppSubmission_AgeRating_SaveButton"]') 'age ratings preview save'
    Start-Sleep -Seconds 2
    Click-TextStrict -Text '继续' -Label 'continue after age ratings'
    Mark-PhaseCompleted 'ageRatings'
}

function Invoke-Packages {
    Navigate-Url -Url (Get-PhaseUrl 'packages') -Label 'packages'
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message 'packages: upload MSIX, desktop only, future device families delegated to Microsoft, save'; return }
    $assets = Get-PropertyValue $script:Config 'assets'
    $msix = [string](Get-PropertyValue $assets 'msix' '')
    if ([string]::IsNullOrWhiteSpace($msix)) { Stop-WithCode 12 'assets.msix is missing from the manifest.' }
    Assert-PackageName
    $packageName = [IO.Path]::GetFileName($msix)
    $packageAlreadyPresent = [bool](Invoke-PageJs "(document.body && document.body.innerText || '').includes($(ConvertTo-JsLiteral $packageName))")
    if ($packageAlreadyPresent) {
        Write-Log -Level 'INFO' -Message "package already present; skip upload $packageName"
    }
    else {
        Upload-File -ContextTexts @('Drag your packages here', '程序包', '.msix') -Path $msix -Label 'MSIX package'
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

function Invoke-Listing {
    Navigate-Url -Url (Get-PhaseUrl 'listing') -Label 'Store listing Chinese'
    if (-not $Apply) { Write-Log -Level 'PLAN' -Message 'listing: description, features, desktop screenshots/assets, short description, keywords, save'; return }
    $values = Get-PropertyValue $script:Config 'values'
    $assets = Get-PropertyValue $script:Config 'assets'
    $expectedName = [string](Get-PropertyValue $script:Config 'productName' '')
    if ($expectedName) {
        $pageName = [string](Invoke-PageJs "(() => { const e=document.querySelector('#reservedTitleSelect'); return e ? ((e.getAttribute('value')||'')+' '+(e.innerText||'')) : ''; })()")
        if ($pageName -notlike "*$expectedName*") {
            Stop-WithCode 12 "Reserved product name on the Store listing page does not match [$expectedName]."
        }
        Write-Log -Level 'PASS' -Message "Store listing reserved name matches [$expectedName]"
    }
    $description = [string](Get-PropertyValue $values 'description' '')
    if ([string]::IsNullOrWhiteSpace($description)) { Stop-WithCode 12 'values.description is missing.' }
    Set-FieldStrict -Selectors @('#description-required') -Value $description -Label 'description'
    $features = @(Get-PropertyValue $values 'features' @())
    for ($featureIndex = 0; $featureIndex -lt $features.Count; $featureIndex++) {
        $featureSelector = "#feature-$featureIndex"
        if (-not (Test-VisibleSelector $featureSelector)) {
            Click-TextStrict -Text '添加其他项目' -Label 'add product feature'
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
        @{ key = 'poster'; label = '9:16 poster'; contexts = @('9:16', '招贴画') },
        @{ key = 'boxart'; label = '1:1 box art'; contexts = @('1:1', '酷图') },
        @{ key = 'logo300'; label = '300x300 logo'; contexts = @('300x300', '300 x 300') },
        @{ key = 'logo150'; label = '150x150 logo'; contexts = @('150x150', '150 x 150') },
        @{ key = 'logo71'; label = '71x71 logo'; contexts = @('71x71', '71 x 71') },
        @{ key = 'superhero'; label = '16:9 superhero art'; contexts = @('16:9', '超级英雄画', '主角图像') }
    )
    foreach ($upload in $uploads) {
        $path = [string](Get-PropertyValue $assets $upload.key '')
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $enabled = [bool](Get-PropertyValue (Get-PropertyValue $script:Config 'listing') $upload.key $false)
        if (-not $enabled) { Write-Log -Level 'INFO' -Message "skip optional asset $($upload.label)"; continue }
        $inputIndex = -1
        if ($upload.ContainsKey('inputIndex')) { $inputIndex = [int]$upload.inputIndex }
        if (Test-SectionHasImage -ContextTexts $upload.contexts -InputIndex $inputIndex) { Write-Log -Level 'INFO' -Message "already present: $($upload.label)"; continue }
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
    elseif ($publishMode -eq 'Asap') {
        Set-RadioStrict -Selector 'input#radioReleaseDate_asap' -Label 'ASAP publish mode'
    }
    else {
        Stop-WithCode 12 "Unsupported publishMode: $publishMode"
    }
    $reason = [string](Get-PropertyValue $options 'runFullTrustReason' '')
    if ($reason) {
        $result = Invoke-PageJs @"
(() => {
 const needle='为何需要使用', els=Array.from(document.querySelectorAll('textarea')).filter(e=>(e.parentElement?.parentElement?.innerText||'').includes(needle));
 if(els.length!==1)return {ok:false,count:els.length};
 const e=els[0], setter=Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value'); setter.set.call(e,$(ConvertTo-JsLiteral $reason)); e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));return {ok:true};
})()
"@
        if (-not $result.ok) { Stop-WithCode 7 "runFullTrust explanation textarea matched $($result.count)" }
    }
    Save-CurrentPage -Phase 'options'
    Mark-PhaseCompleted 'options'
}

function Invoke-Run {
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
    if ($ConfirmSubmit.ToString() -ne 'True') { Stop-WithCode 13 'Final submission confirmation flag is missing.' }
    Navigate-Url -Url (Get-PhaseUrl 'overview') -Label 'submission overview'
    Assert-NoVisibleErrors
    $completed = @(Get-CompletedPhases)
    foreach ($phase in @('availability', 'properties', 'ageRatings', 'packages', 'listing', 'options')) {
        if ($completed -notcontains $phase) { Stop-WithCode 13 "Phase is not complete: $phase" }
    }
    Write-Log -Level 'WARN' -Message "final action: submit product=$product submission=$submission"
    Click-SelectorStrict @('button[data-l10n-key="AppSubmission_PublishButton"]', 'button[data-l10n-key="SubmitToStore"]', 'a[data-l10n-key="AppSubmission_PublishButton"]', 'a[l10n="AppSubmission_PublishButton"]', 'a[href$="/submit"]') 'Submit to Store'
    Start-Sleep -Seconds 2
    Write-Log -Level 'PASS' -Message 'submit action clicked; inspect the resulting confirmation state'
}

function Invoke-Inspect {
    $info = Get-PageInfo
    $fileInputs = Invoke-PageJs @'
(() => Array.from(document.querySelectorAll('input[type="file"]')).map((e,i)=>({index:i,context:(e.parentElement?.parentElement?.innerText||'').replace(/\s+/g,' ').slice(0,180)})))()
'@
    $inspection = [ordered]@{
        generatedAt = (Get-Date).ToString('o')
        page = $info
        stableSelectors = [ordered]@{
            startSubmission = (Test-VisibleSelector 'he-button[data-l10n-key="Start_Submission"],button[data-l10n-key="Start_Submission"]')
            availabilitySave = (Test-VisibleSelector 'input#saveButtonPricing,button#saveButtonPricing,input[uitestid="saveButtonPricing"]')
            propertiesCategory = (Test-VisibleSelector 'select[name="CategorySelect"]')
            ageMode = (Test-VisibleSelector 'input[name="inputMode"]')
            packageUpload = (Test-VisibleSelector 'input.fileuploader,input[type="file"][name="fileuploader"]')
            listingDescription = (Test-VisibleSelector '#description-required')
            listingSave = (Test-VisibleSelector 'button[name="save_button"]')
            optionsManual = (Test-VisibleSelector '#radioReleaseDate_manual')
        }
        fileInputs = $fileInputs
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
            Click-SelectorStrict @('a[aria-controls="collapseApplicationIdentity"]', '[data-target="#collapseApplicationIdentity"]') 'expand product identity'
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
        Write-Log -Level 'INFO' -Message 'stopped only the isolated Edge process started by this CLI'
    }
}

function Close-Cdp {
    if ($null -ne $script:CdpSocket) {
        try { $script:CdpSocket.CloseAsync([Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'done', [Threading.CancellationToken]::None).GetAwaiter().GetResult() } catch { }
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
            $pid = [int](Get-Content -LiteralPath (Join-Path $script:StateRoot 'edge.pid'))
            $process = Get-Process -Id $pid -ErrorAction SilentlyContinue
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
        Write-Log -Level 'PASS' -Message 'Edge is ready. Sign-in state is persisted only in the isolated profile.'
    }
    elseif ($Action -eq 'inspect') {
        Ensure-SignedIn
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
    Close-Cdp
    if ($Action -ne 'launch' -and -not $KeepOpen) { Stop-Edge }
    Release-RunMutex
    exit 1
}
