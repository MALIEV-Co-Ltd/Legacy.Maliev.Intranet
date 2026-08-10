$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($args.Count -gt 0) {
    throw "The verifier accepts no parameters; received: $($args -join ' ')"
}

$root = Split-Path -Parent $PSScriptRoot
$manifestFile = Join-Path $root 'Maliev.ShadcnBlazor\Reference\shadcn-reference.json'
$manifest = Get-Content -Raw -LiteralPath $manifestFile | ConvertFrom-Json
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

$headers = @{ 'User-Agent' = 'Maliev-Shadcn-reference-verifier' }
$token = if (-not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    $env:GH_TOKEN
} elseif (-not [string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    $env:GITHUB_TOKEN
} elseif ($null -ne (Get-Command gh -ErrorAction SilentlyContinue)) {
    (& gh auth token 2>$null)
}
if (-not [string]::IsNullOrWhiteSpace($token)) {
    $headers.Authorization = "Bearer $token"
}
$apiRoot = 'https://api.github.com/repos/shadcn-ui/ui/contents'
$encodedRegistryRoot = ($manifest.registryRoot -split '/' | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/'
$registryResponse = Invoke-RestMethod -Headers $headers -Uri "$apiRoot/$encodedRegistryRoot`?ref=$($manifest.commit)"
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

$encodedStylePath = ($manifest.styleSource.path -split '/' | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/'
$actualStyle = Invoke-RestMethod -Headers $headers -Uri "$apiRoot/$encodedStylePath`?ref=$($manifest.commit)"
if ($actualStyle.sha -ne $manifest.styleSource.blobSha) {
    $failures.Add("Vega style: expected $($manifest.styleSource.blobSha), received $($actualStyle.sha)")
}

if ($failures.Count -gt 0) {
    [Console]::Error.WriteLine("Pinned Shadcn reference mismatch:`n$($failures -join "`n")")
    exit 1
}

Write-Host "Verified $($registryComponents.Count) Base registry files and Vega style at $($manifest.commit)."
