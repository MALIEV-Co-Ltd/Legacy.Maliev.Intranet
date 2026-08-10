param(
    [string] $ManifestPath,
    [string] $RegistryResponsePath,
    [string] $StyleResponsePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $root 'Maliev.ShadcnBlazor\Reference\shadcn-reference.json'
}

$manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
if ($manifest.schema -ne 'shadcn-reference/v1') {
    throw "Unsupported Shadcn reference manifest schema: $($manifest.schema)"
}
if ($manifest.commit -notmatch '^[0-9a-f]{40}$') {
    throw "The Shadcn reference commit must be a full lowercase Git commit SHA: $($manifest.commit)"
}

$registryComponents = @($manifest.components | Where-Object sourceKind -eq 'registry-file')
if ($registryComponents.Count -ne 61) {
    throw "Expected 61 registry-file components, received $($registryComponents.Count)."
}

$usesFixtureResponses = -not [string]::IsNullOrWhiteSpace($RegistryResponsePath) -or
    -not [string]::IsNullOrWhiteSpace($StyleResponsePath)
if ($usesFixtureResponses -and
    ([string]::IsNullOrWhiteSpace($RegistryResponsePath) -or [string]::IsNullOrWhiteSpace($StyleResponsePath))) {
    throw 'RegistryResponsePath and StyleResponsePath must be supplied together.'
}

$headers = @{ 'User-Agent' = 'Maliev-Shadcn-reference-verifier' }
$apiRoot = 'https://api.github.com/repos/shadcn-ui/ui/contents'
$encodedRegistryRoot = ($manifest.registryRoot -split '/' | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/'

if ($usesFixtureResponses) {
    $registryResponse = Get-Content -Raw -LiteralPath $RegistryResponsePath | ConvertFrom-Json
} else {
    $registryResponse = Invoke-RestMethod -Headers $headers -Uri "$apiRoot/$encodedRegistryRoot`?ref=$($manifest.commit)"
}
$registry = @($registryResponse.GetEnumerator())

$registryByName = @{}
foreach ($item in $registry) {
    $registryByName[$item.name] = $item
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($component in $registryComponents) {
    $name = "$($component.slug).tsx"
    $actual = $registryByName[$name]
    if ($null -eq $actual) {
        $failures.Add("$($component.name): $name is absent from the pinned Base registry")
        continue
    }
    if ($actual.sha -ne $component.blobSha) {
        $failures.Add("$($component.name): expected $($component.blobSha), received $($actual.sha)")
    }
}

if ($usesFixtureResponses) {
    $actualStyle = Get-Content -Raw -LiteralPath $StyleResponsePath | ConvertFrom-Json
} else {
    $encodedStylePath = ($manifest.styleSource.path -split '/' | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/'
    $actualStyle = Invoke-RestMethod -Headers $headers -Uri "$apiRoot/$encodedStylePath`?ref=$($manifest.commit)"
}
if ($actualStyle.sha -ne $manifest.styleSource.blobSha) {
    $failures.Add("Vega style: expected $($manifest.styleSource.blobSha), received $($actualStyle.sha)")
}

if ($failures.Count -gt 0) {
    [Console]::Error.WriteLine("Pinned Shadcn reference mismatch:`n$($failures -join "`n")")
    exit 1
}

Write-Host "Verified $($registryComponents.Count) Base registry files and Vega style at $($manifest.commit)."
