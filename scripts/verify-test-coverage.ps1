param(
    [Parameter(Mandatory = $true)]
    [string] $CoverageFile
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CoverageFile -PathType Leaf)) {
    throw "Coverage report was not found: $CoverageFile"
}

[xml] $report = Get-Content -LiteralPath $CoverageFile
$packages = @($report.coverage.packages.package)
$thresholds = [ordered]@{
    'Legacy.Maliev.Intranet.Bff' = 0.80
    'Legacy.Maliev.Intranet.Server' = 0.85
    'Legacy.Maliev.Intranet.Contracts' = 0.95
}

foreach ($entry in $thresholds.GetEnumerator()) {
    $package = $packages | Where-Object { $_.name -ceq $entry.Key } | Select-Object -First 1
    if ($null -eq $package) {
        throw "Coverage report is missing required package '$($entry.Key)'."
    }

    $lineRate = [double]::Parse(
        [string] $package.'line-rate',
        [System.Globalization.CultureInfo]::InvariantCulture)
    $percentage = [Math]::Round($lineRate * 100, 2)
    $minimum = [Math]::Round([double] $entry.Value * 100, 2)
    Write-Host "$($entry.Key): $percentage% line coverage (minimum $minimum%)"

    if ($lineRate -lt [double] $entry.Value) {
        throw "$($entry.Key) line coverage is $percentage%, below the required $minimum%."
    }
}
