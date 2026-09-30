[CmdletBinding()]
param(
    [string] $SourceRoot = 'B:/maliev-legacy'
)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath((Join-Path $workspace 'TestResults/.dependencies/recovery-chain'))
if (-not $output.StartsWith($workspace + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Acceptance clones must stay inside this worktree.'
}
$pins = [ordered]@{
    'Legacy.Maliev.AuthService' = 'a1fb10334f8504a4bf441bde5a2420f4fa686d43' # Public reviewed Git revision, not a credential. gitleaks:allow
    'Legacy.Maliev.ServiceDefaults' = '5c5f9479313710fa576f83d3b396442997a2fcf4'
    'Legacy.Maliev.CompatibilityContracts' = '78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7'
}
foreach ($entry in $pins.GetEnumerator()) {
    $source = [IO.Path]::GetFullPath((Join-Path $SourceRoot $entry.Key))
    $target = [IO.Path]::GetFullPath((Join-Path $output $entry.Key))
    if (Test-Path -LiteralPath $target) {
        $head = & git -C $target rev-parse HEAD
        if ($LASTEXITCODE -ne 0 -or $head -ne $entry.Value) { throw "Existing clone pin mismatch: $($entry.Key)" }
    }
    else {
        & git -C $source cat-file -e "$($entry.Value)^{commit}"
        if ($LASTEXITCODE -ne 0) { throw "Required committed source object unavailable: $($entry.Key)" }
        & git clone -c core.longpaths=true --no-hardlinks --no-checkout -- $source $target
        if ($LASTEXITCODE -ne 0) { throw "Acceptance clone failed: $($entry.Key)" }
        & git -C $target checkout --detach $entry.Value
        if ($LASTEXITCODE -ne 0) { throw "Acceptance pin checkout failed: $($entry.Key)" }
    }
    $dirty = & git -C $target status --porcelain
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw "Acceptance source clone is not clean: $($entry.Key)" }
    $head = & git -C $target rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $head -ne $entry.Value) { throw "Acceptance source verification failed: $($entry.Key)" }
    Write-Output "$($entry.Key): $head"
}
Write-Output "MalievWorkspaceRoot=$output"
