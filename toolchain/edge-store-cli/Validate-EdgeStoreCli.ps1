[CmdletBinding()]
param(
    [string]$ProjectDir = '',
    [string]$ManifestPath = '',
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
if (-not $scriptDir) { $scriptDir = (Get-Location).Path }
if ([string]::IsNullOrWhiteSpace($ProjectDir)) { $ProjectDir = $scriptDir }
if ([string]::IsNullOrWhiteSpace($ManifestPath)) { $ManifestPath = Join-Path $ProjectDir 'examples\store-automation.json' }

$csproj = Join-Path $ProjectDir 'EdgeStore.Cli.csproj'
if (-not (Test-Path -LiteralPath $csproj)) { throw "C# project file not found: $csproj" }
if (-not (Test-Path -LiteralPath $ManifestPath)) { throw "Manifest not found: $ManifestPath" }

$launcher = Join-Path $ProjectDir 'Invoke-EdgeStore.ps1'
if (Test-Path -LiteralPath $launcher) {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($launcher, [ref]$tokens, [ref]$errors) | Out-Null
    if ($errors.Count -gt 0) {
        $errors | ForEach-Object { Write-Error ("{0}:{1}: {2}" -f $_.Extent.StartLineNumber, $_.Extent.StartColumnNumber, $_.Message) }
        exit 1
    }
}

$config = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($Strict) {
    foreach ($name in @('productId', 'productName')) {
        if ([string]::IsNullOrWhiteSpace([string]$config.$name)) { throw "Strict manifest field is empty: $name" }
    }
    if ($null -eq $config.pricing -or $config.pricing.currency -ne 'CN' -or $config.pricing.priceTier -ne '0') {
        throw 'Strict manifest pricing must be currency CN and price tier 0.'
    }
    if ($null -eq $config.properties -or @('No','Yes') -notcontains [string]$config.properties.privacy) {
        throw 'Strict manifest properties.privacy must be Yes or No.'
    }
    if ($config.properties.privacy -eq 'Yes' -and [string]::IsNullOrWhiteSpace([string]$config.properties.privacyPolicyUrl)) {
        throw 'privacy=Yes requires properties.privacyPolicyUrl.'
    }
    $base = Split-Path -Parent $ManifestPath
    foreach ($property in $config.assets.PSObject.Properties) {
        $enabled = $false
        $listingProperty = $config.listing.PSObject.Properties[$property.Name]
        if ($null -ne $listingProperty) { $enabled = [bool]$listingProperty.Value }
        if ($enabled -and [string]::IsNullOrWhiteSpace([string]$property.Value)) { throw "Enabled asset path is empty: $($property.Name)" }
        if ($enabled -and -not [IO.Path]::IsPathRooted([string]$property.Value) -and -not (Test-Path -LiteralPath (Join-Path $base $property.Value))) { throw "Enabled asset file is missing: $($property.Name)" }
    }
}

Write-Output "EDGE_STORE_CLI_VALID (V2 C# .NET 10 Driver)"
if ($Strict) { Write-Output 'MODE: STRICT' }
Write-Output "PROJECT: $([IO.Path]::GetFullPath($csproj))"
Write-Output "MANIFEST: $([IO.Path]::GetFullPath($ManifestPath))"
exit 0
