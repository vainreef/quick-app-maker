# Windows Toolchain v1 Command Runbook

- Status: `windows-verified-runbook`
- Release selector: `bootstrap/toolchain.json -> release`
- Target: `win-x64`
- Applies to: the portable `.NET 10` SDK and bundled WinApp CLI from this repository

This file is the command source of truth for building, launching, testing, publishing, and packaging an app. Historical round results remain in `docs/windows-smoke-test.md`; they are evidence, not executable instructions.

## Choose one delivery route

Do not mix these routes in one troubleshooting loop.

| Goal | Route | Certificate behavior |
| --- | --- | --- |
| Build and let the user try the app locally | `dotnet build` then `winapp run --detach` | No package-signing certificate |
| Produce an MSIX for Microsoft Store upload | self-contained `dotnet publish` then unsigned `winapp package` | The Store signs the accepted package |
| Install the exact MSIX directly outside the Store | explicit sideloading task | Separate opt-in workflow; machine trust is required |

Default to the first route during development. A local product check does not require repeatedly creating, importing, or replacing certificates.

## 0. Prepare one deterministic session

Run commands from `WORKSPACE_ROOT`, the directory that contains `quick-app-maker/`, `dotnet/`, and the app directory.

```powershell
$workspaceRoot = (Get-Location).Path
$repoRoot = Join-Path $workspaceRoot 'quick-app-maker'
$appRoot = Join-Path $workspaceRoot '<app-slug>'
$project = Join-Path $appRoot '<AppName>.csproj'
$manifest = Join-Path $appRoot 'Package.appxmanifest'
$dotnetRoot = Join-Path $workspaceRoot 'dotnet'
$dotnet = Join-Path $dotnetRoot 'dotnet.exe'

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_MULTILEVEL_LOOKUP = '0'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:WINAPP_CLI_TELEMETRY_OPTOUT = '1'
if ($env:PATH -notlike "*$dotnetRoot*") { $env:PATH = "$dotnetRoot;$env:PATH" }

if (-not (Test-Path -LiteralPath $dotnet)) { throw "Portable dotnet missing: $dotnet" }
if (-not (Test-Path -LiteralPath $project)) { throw "Project missing: $project" }
if (-not (Test-Path -LiteralPath $manifest)) { throw "Manifest missing: $manifest" }
New-Item -ItemType Directory -Force -Path (Join-Path $appRoot 'build') | Out-Null
$winappCommand = Get-Command winapp.exe -ErrorAction SilentlyContinue
if (-not $winappCommand) { $winappCommand = Get-Command winapp -ErrorAction Stop }
$winapp = $winappCommand.Source
```

Every command uses an explicit project path and working directory. Do not rely on a previous `Set-Location` surviving across tool calls.

## 1. Inspect the environment once

```powershell
Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsBuildNumber, OsArchitecture
& $dotnet --list-sdks
& $dotnet new list winui
Get-AppxPackage -Name 'winapp' | Select-Object Name, Version, Status, InstallLocation
& $winapp --version
& $winapp run --help
& $winapp package --help

$devMode = Get-ItemProperty `
  'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' `
  -Name AllowDevelopmentWithoutDevLicense `
  -ErrorAction SilentlyContinue
$devMode | Select-Object AllowDevelopmentWithoutDevLicense
```

Developer Mode is relevant to loose-layout development registration. It does not waive MSIX signature trust. If it is off when `winapp run` reports that requirement, ask the user to enable it in Windows Settings, verify the registry value, and resume directly with `winapp run`.

## 2. Create the project

```powershell
Set-Location -LiteralPath $workspaceRoot
& $dotnet new winui-navview -n '<AppName>' -o $appRoot
if ($LASTEXITCODE -ne 0) { throw "Project creation failed: $LASTEXITCODE" }
```

Read the generated `.csproj`, XAML, code-behind, manifest, and assets before editing. Preserve the generated packaging integration unless a verified requirement calls for a change.

## 3. Build in the foreground

```powershell
& $dotnet restore $project --source https://nuget.azure.cn/v3/index.json
if ($LASTEXITCODE -ne 0) { throw "Restore failed: $LASTEXITCODE" }

& $dotnet build $project -c Debug --nologo -v minimal /p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "Debug build failed: $LASTEXITCODE" }

& $dotnet build $project -c Release --nologo -v minimal /p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "Release build failed: $LASTEXITCODE" }
```

Build and publish commands run directly with `&`. Do not wrap compiler invocations in `Start-Process`, a background job, `cmd /c start`, or a polling wrapper.

## 4. Run locally with package identity

This is the default local delivery route. It registers a loose-layout development package and launches the app without an MSIX signing certificate.

```powershell
$runOutput = & $winapp run $project --no-build --detach --json 2>&1
$runExit = $LASTEXITCODE
$runOutput | Tee-Object -FilePath (Join-Path $appRoot 'build\winapp-run.log')
if ($runExit -ne 0) { throw "winapp run failed: $runExit" }
```

`--detach` is the automation-safe launch mode: WinApp CLI returns after launch instead of keeping the tool call attached to the app lifetime. Preserve the returned PID/output as evidence.

Verify both process state and UI state:

```powershell
Start-Sleep -Seconds 3
Get-Process -Name '<AppName>' -ErrorAction SilentlyContinue |
  Select-Object Id, ProcessName, Responding, StartTime

& $winapp ui search '<visible text>' -a '<AppName>'
```

After every UI mutation, query the UI tree again before reusing an element identifier. WinUI can recreate controls and invalidate earlier runtime IDs.

### Stop or reset the development registration

Prefer the exact PID returned by `winapp run`:

```powershell
Stop-Process -Id <APP_PID> -Force -ErrorAction SilentlyContinue
```

When a clean registration is required:

```powershell
& $winapp unregister --manifest $manifest
if ($LASTEXITCODE -ne 0) { throw "winapp unregister failed: $LASTEXITCODE" }
```

`winapp unregister` targets development registrations created by `winapp run` or `create-debug-identity`; it does not remove Store-installed packages.

## 5. Diagnose a startup failure

Use evidence in this order:

1. `winapp run` output and exit code.
2. Process existence after three seconds.
3. Recent Application Error and .NET Runtime events.
4. A bounded `winapp run --debug-output` reproduction for an immediate startup crash.
5. A temporary module initializer probe only when the exception occurs before `App` construction.

```powershell
$since = (Get-Date).AddMinutes(-10)
Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=$since } -ErrorAction SilentlyContinue |
  Where-Object { $_.ProviderName -in @('Application Error', '.NET Runtime') } |
  Select-Object -First 20 TimeCreated, ProviderName, Id, Message
```

Do not infer success from a command returning quickly. A fast return can mean an immediate crash.

## 6. Publish a self-contained folder

The Windows App SDK and .NET runtime are separate deployment layers. Configure both:

```xml
<PropertyGroup>
  <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
</PropertyGroup>
```

Then publish:

```powershell
$publishDir = Join-Path $appRoot 'publish'
if (Test-Path -LiteralPath $publishDir) {
  Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

& $dotnet publish $project `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o $publishDir `
  --nologo `
  /p:WindowsAppSDKSelfContained=true `
  /p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $LASTEXITCODE" }
```

Copy assets to the exact destination, never into an already nested `Assets\Assets` directory:

```powershell
$sourceAssets = Join-Path $appRoot 'Assets'
$targetAssets = Join-Path $publishDir 'Assets'
if (Test-Path -LiteralPath $targetAssets) {
  Remove-Item -LiteralPath $targetAssets -Recurse -Force
}
if (Test-Path -LiteralPath $sourceAssets) {
  Copy-Item -LiteralPath $sourceAssets -Destination $targetAssets -Recurse -Force
}

Get-ChildItem -LiteralPath $publishDir -Recurse |
  Select-Object FullName, Length |
  Out-File (Join-Path $appRoot 'build\publish-files.txt')
```

Before publishing again, stop the launched app and finish the previous compiler invocation. A new publish must never overlap an earlier build or publish targeting the same project.

## 7. Create the Microsoft Store package

Run Store name reservation first so the manifest contains the real Partner Center identity and complete reserved display name. Then create one self-contained upload artifact without a development certificate:

```powershell
$storeDir = Join-Path $appRoot 'store-package'
$msix = Join-Path $storeDir '<Identity>_<Version>_x64.msix'
New-Item -ItemType Directory -Force -Path $storeDir | Out-Null

& $winapp package $publishDir `
  --manifest $manifest `
  --self-contained `
  --executable '<AppName>.exe' `
  --output $msix
if ($LASTEXITCODE -ne 0) { throw "Store package creation failed: $LASTEXITCODE" }

Get-Item -LiteralPath $msix | Select-Object FullName, Length, LastWriteTime
Get-FileHash -LiteralPath $msix -Algorithm SHA256
```

For a Microsoft Store submission, omit `--generate-cert`, `--install-cert`, and local `Add-AppxPackage`. Partner Center accepts the upload artifact and the Store signs the accepted package.

Run the repository preflight before browser upload:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass `
  -File .\quick-app-maker\toolchain\edge-store-cli\Invoke-EdgeStore.ps1 `
  -Action preflight `
  -Manifest .\<app-slug>\build\edge-store.json `
  -StateDir .\.cache\edge-store-state
if ($LASTEXITCODE -ne 0) { throw "Store preflight failed: $LASTEXITCODE" }
```

## 8. Exact local MSIX sideloading is a separate route

Select this route only when the user explicitly requests installation of the exact MSIX outside the Store.

Facts that control this route:

- A self-signed MSIX must be signed and its signing certificate trusted by the machine.
- Current Windows App Installer guidance uses `LocalMachine\TrustedPeople`; `CurrentUser\TrustedPeople` and `CurrentUser\Root` are not substitutes for that trust decision.
- Installing a certificate into the machine store is a user-approved administrative action.
- Developer Mode does not repair error `0x800B0109`.
- A normal code-signing development certificate is an end-entity certificate. Do not turn it into a CA certificate or place it in a root store.

If the task is ordinary development or pre-Store validation, return to section 4 instead of entering this route.

## 9. Store automation uses discrete stages

Read the repository contract before these commands. Use a workspace state directory on every call.

```powershell
$edgeStore = '.\quick-app-maker\toolchain\edge-store-cli\Invoke-EdgeStore.ps1'
$storeManifest = '.\<app-slug>\build\edge-store.json'
$storeState = '.\.cache\edge-store-state'

# New product: reserve the approved name and write package identity back to local files.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action reserve -AppName '<AppName>' -Manifest $storeManifest -StateDir $storeState

# Offline package/listing validation.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action preflight -Manifest $storeManifest -StateDir $storeState

# Persistent browser session and live submission discovery.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action launch -Manifest $storeManifest -StateDir $storeState -KeepOpen
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action discover -Manifest $storeManifest -StateDir $storeState

# One phase per call.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action step -Phase availability -Manifest $storeManifest -StateDir $storeState -Apply
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action step -Phase properties -Manifest $storeManifest -StateDir $storeState -Apply
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action step -Phase ageRatings -Manifest $storeManifest -StateDir $storeState -Apply
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action step -Phase packages -Manifest $storeManifest -StateDir $storeState -Apply
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action cleanlanguages -Manifest $storeManifest -StateDir $storeState
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action filllisting -Manifest $storeManifest -StateDir $storeState
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action inspectoptions -Manifest $storeManifest -StateDir $storeState
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action filloptions -Manifest $storeManifest -StateDir $storeState

# Final cold-load verification. The user performs the final certification click.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File $edgeStore `
  -Action verify -Manifest $storeManifest -StateDir $storeState
```

Use `inspect` or `status` as a status probe. `run -Phase all` performs all six phases and is not a read-only status command.

## 命令执行硬规则

These rules are mandatory for every build and local-run turn.

### 1. One compiler at a time

Only one `restore`, `build`, `publish`, or package command may target a project/output directory at once. Finish and inspect the exit code before starting the next command.

### 2. Compiler commands stay in the foreground

Invoke `dotnet` directly with `&` and `/p:UseSharedCompilation=false`. Background builds, `Start-Process -Wait`, and status-poll loops create inherited-handle hangs and hide the real exit code.

### 3. Apps launch through `winapp run --detach`

Do not use a background `dotnet run`. `--detach` returns a PID/output while leaving the app available for UI automation.

### 4. Track exact process ownership

Keep the PID returned by the launch command. Stop that PID rather than killing every `dotnet` process on the machine.

### 5. Stop before cleaning

Before deleting `bin`, `obj`, or `publish`, stop the app PID, finish the active command, and shut down compiler servers:

```powershell
Stop-Process -Id <APP_PID> -Force -ErrorAction SilentlyContinue
& $dotnet build-server shutdown
```

Clean only after a specific cache or file-lock error. Cleaning is not a routine first step.

### 6. Every wait is bounded and observable

Poll a concrete state such as PID existence, an expected UI element, a file timestamp, or a Partner Center status. Emit progress at least every 10 seconds and set a deadline.

### 7. One hypothesis, one evidence-based retry

Capture command, exit code, error text, and relevant state before retrying. Repeat the same fix once. If the same blocker returns, stop branching and report the evidence instead of trying unrelated certificate, runtime, CA, trimming, and cache changes in sequence.

### 8. Keep deployment layers separate

An MSIX trust failure, a missing .NET runtime, and a missing Windows App SDK runtime are different failures. Fix only the layer named by the error.

### 9. Validate success after the action

A successful click, file creation, `EXIT=0`, or equal in-memory DOM values are intermediate evidence. Verify the resulting process/UI/package/overview state separately.

### 10. Preserve the workspace boundary

All scripts, logs, state, packages, and caches stay under `WORKSPACE_ROOT`. Do not use system temporary folders or user profile data paths for diagnostics.

## Error convergence map

| Evidence | Meaning | Next action |
| --- | --- | --- |
| `0x800B0109` during exact MSIX install | signing certificate is not trusted by the machine | Return to `winapp run` for ordinary local testing; use the explicit sideload route only when requested |
| App asks for Windows App Runtime or direct EXE fails before UI | framework-dependent output was launched directly | Run through `winapp run`, or republish with both self-contained settings |
| `0x80070490` before `App` construction in self-contained output | Windows App SDK self-contained property is missing | Set `WindowsAppSDKSelfContained=true`, republish once |
| package tool reports multiple executables | self-contained output contains several `.exe` files | Pass `--executable <AppName>.exe` |
| `MSB3027`, `MSB3021`, or locked EXE/DLL | app or compiler server owns an output file | Stop exact app PID, run `dotnet build-server shutdown`, then retry once |
| `0x80073CFB` or conflicting development identity | stale registration/version conflict | inspect package identity; use `winapp unregister` for the development registration |
| `Assets\Assets` exists | assets were copied into an existing target directory | rebuild the exact target `publish\Assets` directory, then package once |
| WMC9999 after an `x:Bind` DataTemplate edit | compiled binding metadata is incomplete | verify `x:DataType` and namespace first |
| ContentDialog crash `0xc000027b` | dialog is detached from the visual tree | set `XamlRoot` before `ShowAsync` |
| command prints build success but tool call stays active | inherited process/pipe handle | interrupt once, shut down build servers, rerun directly in foreground |

## Stable WinUI findings

- A `DataTemplate` containing `{x:Bind}` needs explicit `x:DataType`.
- A WinUI 3 `ContentDialog` needs the current visual tree's `XamlRoot`.
- WinUI 3 `Window` does not expose WPF-style `Resources` or `Loaded`; use app/page resources and an appropriate activation event.
- `RectangleGeometry` does not provide WPF `RadiusX`/`RadiusY`; use a rounded `Border`/brush composition.
- Icon-only buttons need `AutomationProperties.Name` for reliable UI automation and accessibility.
- Manifest GUID values use the schema format without braces.
- Manifest language and both display-name fields must match the intended Store identity and listing language.
- A Store package containing `runFullTrust` needs the submission-options explanation, even when the package warning itself is expected.

## Recording a new finding

Add only reproduced behavior. Record:

```markdown
### Short title
- Environment:
- Command:
- Exit code:
- Error text:
- Root cause:
- Fix:
- Retest result:
```

Update the executable workflow above when a finding changes the preferred route; do not append a contradictory command at the bottom of the file.
