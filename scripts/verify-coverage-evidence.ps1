param(
    [Parameter(Mandatory = $true)][ValidateSet('Capture', 'Verify')][string] $Mode,
    [Parameter(Mandatory = $true)][string] $ExpectedSourceRevision,
    [Parameter(Mandatory = $true)][string] $BuildStartedUtc,
    [string] $WorkspaceRoot = (Get-Location).Path,
    [string] $SourceRepositoryRoot = $WorkspaceRoot,
    [string] $ResultsDirectory = 'TestResults',
    [string[]] $Projects = @('Legacy.Maliev.Intranet.Bff', 'Legacy.Maliev.Intranet.Server', 'Legacy.Maliev.Intranet.Contracts')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:Stage = 'initialization'

function Require([bool] $Condition) {
    if (-not $Condition) { throw 'Invalid coverage evidence.' }
}

function OwnedPath([string] $Relative) {
    Require (-not [string]::IsNullOrWhiteSpace($Relative))
    $full = [IO.Path]::GetFullPath([IO.Path]::Combine($script:Root, $Relative.Replace('\', '/')))
    Require ($full.StartsWith($script:Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal))
    $item = $full
    while ($item.Length -gt $script:Root.Length) {
        if (Test-Path -LiteralPath $item) { Require ((Get-Item -LiteralPath $item -Force).LinkTarget -eq $null) }
        $item = [IO.Path]::GetDirectoryName($item)
    }
    return $full
}

function NormalizedPath([string] $Relative) {
    return [IO.Path]::GetRelativePath($script:Root, (OwnedPath $Relative)).Replace('\', '/')
}

function Bytes([string] $Relative) {
    $path = OwnedPath $Relative
    Require ([IO.File]::Exists($path))
    Require ((Get-Item -LiteralPath $path).Length -gt 0 -and (Get-Item -LiteralPath $path).Length -le 32MB)
    return ,([IO.File]::ReadAllBytes($path))
}

function Sha([byte[]] $Data) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Data)).ToLowerInvariant()
}

function Xml([string] $Relative) {
    $data = Bytes $Relative
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.MaxCharactersInDocument = 32MB
    $stream = [IO.MemoryStream]::new($data)
    $reader = [Xml.XmlReader]::Create($stream, $settings)
    try {
        $document = [Xml.XmlDocument]::new()
        $document.XmlResolver = $null
        $document.Load($reader)
        return $document
    }
    finally { $reader.Dispose(); $stream.Dispose() }
}

function SourceRevision {
    $script:Stage = 'source-revision'
    $start = [Diagnostics.ProcessStartInfo]::new('git')
    $start.WorkingDirectory = $SourceRepositoryRoot
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($key in @($start.Environment.Keys)) { if ($key.StartsWith('GIT_', [StringComparison]::Ordinal)) { [void] $start.Environment.Remove($key) } }
    $start.ArgumentList.Add('rev-parse')
    $start.ArgumentList.Add('HEAD')
    $process = [Diagnostics.Process]::Start($start)
    try {
        $output = $process.StandardOutput.ReadToEndAsync()
        $errorOutput = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(10000)) {
            $process.Kill($true)
            [void] $process.WaitForExit(5000)
            throw 'Source revision unavailable.'
        }
        $revision = $output.GetAwaiter().GetResult().Trim()
        [void] $errorOutput.GetAwaiter().GetResult()
        Require ($process.ExitCode -eq 0 -and $revision -cmatch '^[a-f0-9]{40}$' -and $revision -ceq $ExpectedSourceRevision)
        $start.ArgumentList.Clear()
        $script:Stage = 'tracked-source-clean'
        foreach ($argument in @('diff', '--quiet', 'HEAD', '--')) { $start.ArgumentList.Add($argument) }
        $clean = [Diagnostics.Process]::Start($start)
        try {
            $cleanOutput = $clean.StandardOutput.ReadToEndAsync()
            $cleanError = $clean.StandardError.ReadToEndAsync()
            if (-not $clean.WaitForExit(10000)) {
                $clean.Kill($true)
                [void] $clean.WaitForExit(5000)
                throw 'Source identity unavailable.'
            }
            [void] $cleanOutput.GetAwaiter().GetResult()
            [void] $cleanError.GetAwaiter().GetResult()
            Require ($clean.ExitCode -eq 0)
        }
        finally { $clean.Dispose() }
        return $revision
    }
    finally { $process.Dispose() }
}

function Pair([string] $Project, [bool] $Fresh) {
    $script:Stage = 'produced-outputs'
    $prefix = "$Project/bin/Release/net10.0/$Project"
    $dll = Bytes ($prefix + '.dll')
    $pdb = Bytes ($prefix + '.pdb')
    if ($Fresh) {
        foreach ($extension in @('dll', 'pdb')) { Require ((Get-Item -LiteralPath (OwnedPath ($prefix + '.' + $extension))).LastWriteTimeUtc -ge $script:BuildStart) }
    }
    $runtime = 'Legacy.Maliev.Intranet.Tests/bin/Release/net10.0/' + $Project
    $script:Stage = 'runtime-copy-pairing'
    Require ((Sha (Bytes ($runtime + '.dll'))) -ceq (Sha $dll))
    Require ((Sha (Bytes ($runtime + '.pdb'))) -ceq (Sha $pdb))
    $peStream = [IO.MemoryStream]::new($dll)
    $script:Stage = 'pdb-assembly-pairing'
    $pdbStream = [IO.MemoryStream]::new($pdb)
    $pe = [Reflection.PortableExecutable.PEReader]::new($peStream)
    $provider = [Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream, [Reflection.Metadata.MetadataStreamOptions]::Default, 0)
    try {
        $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        Require ($metadata.GetString($metadata.GetAssemblyDefinition().Name) -ceq $Project)
        $reader = $provider.GetMetadataReader([Reflection.Metadata.MetadataReaderOptions]::Default, $null)
        $id = [Reflection.Metadata.BlobContentId]::new($reader.DebugMetadataHeader.Id)
        $entries = @($pe.ReadDebugDirectory() | Where-Object Type -eq CodeView)
        Require ($entries.Count -eq 1)
        $codeView = $pe.ReadCodeViewDebugDirectoryData($entries[0])
        Require ($codeView.Guid -eq $id.Guid -and $entries[0].Stamp -eq $id.Stamp -and $codeView.Age -eq 1)
        $documents = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        $script:Stage = 'pdb-source-checksum'
        foreach ($handle in $reader.Documents) {
            $document = $reader.GetDocument($handle)
            $name = $reader.GetString($document.Name).Replace('\', '/')
            $index = $name.IndexOf($Project + '/', [StringComparison]::Ordinal)
            Require ($index -ge 0)
            $path = NormalizedPath $name.Substring($index)
            Require ($path.StartsWith($Project + '/', [StringComparison]::Ordinal) -and -not $documents.ContainsKey($path))
            Require ($reader.GetGuid($document.HashAlgorithm).ToString() -ceq '8829d00f-11b8-4213-878b-770e8597ac16')
            $hash = Sha (Bytes $path)
            Require ($hash -ceq [Convert]::ToHexString($reader.GetBlobBytes($document.Hash)).ToLowerInvariant())
            $documents.Add($path, [ordered]@{ path = $path; sha256 = $hash; visibleSequencePoints = 0; generated = $path.Contains('/obj/', [StringComparison]::Ordinal) })
        }
        foreach ($handle in $reader.MethodDebugInformation) {
            $script:Stage = 'pdb-sequence-points'
            $method = $reader.GetMethodDebugInformation($handle)
            foreach ($point in $method.GetSequencePoints()) {
                if ($point.IsHidden) { continue }
                $documentHandle = $point.Document
                if ($documentHandle.IsNil) { $documentHandle = $method.Document }
                if ($documentHandle.IsNil) { continue }
                $document = $reader.GetDocument($documentHandle)
                $name = $reader.GetString($document.Name).Replace('\', '/')
                $index = $name.IndexOf($Project + '/', [StringComparison]::Ordinal)
                Require ($index -ge 0)
                $path = NormalizedPath $name.Substring($index)
                Require ($documents.ContainsKey($path))
                $documents[$path].visibleSequencePoints++
            }
        }
        $nonmembers = 0
        $script:Stage = 'physical-source-inventory'
        foreach ($candidate in Get-ChildItem -LiteralPath (OwnedPath $Project) -Filter '*.cs' -File -Recurse) {
            $relative = NormalizedPath ([IO.Path]::GetRelativePath($script:Root, $candidate.FullName))
            if (-not $documents.ContainsKey($relative)) { $nonmembers++ }
        }
        return [ordered]@{ project = $Project; dllSha256 = (Sha $dll); pdbSha256 = (Sha $pdb); documents = @($documents.Values); nonMemberSourceCandidates = $nonmembers }
    }
    finally { $provider.Dispose(); $pe.Dispose(); $pdbStream.Dispose(); $peStream.Dispose() }
}

function WriteNewJson([string] $Path, $Value) {
    $data = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 12) + "`n")
    $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.Write($data, 0, $data.Length) }
    finally { $stream.Dispose() }
}

try {
    $script:Stage = 'utc-input'
    Add-Type -AssemblyName System.Reflection.Metadata
    Require ($BuildStartedUtc -cmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{1,7}(Z|\+00:00)$')
    $script:BuildStart = [DateTimeOffset]::Parse($BuildStartedUtc, [Globalization.CultureInfo]::InvariantCulture).UtcDateTime
    $script:Root = [IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    Require ([IO.Directory]::Exists($script:Root) -and $script:BuildStart.Kind -eq [DateTimeKind]::Utc)
    Require ($Projects.Count -gt 0 -and $Projects.Count -le 3 -and @($Projects | Select-Object -Unique).Count -eq $Projects.Count)
    foreach ($project in $Projects) { Require (@('Legacy.Maliev.Intranet.Bff', 'Legacy.Maliev.Intranet.Server', 'Legacy.Maliev.Intranet.Contracts') -ccontains $project) }
    $revision = SourceRevision
    $results = OwnedPath $ResultsDirectory
    $captureRelative = $ResultsDirectory + '/coverage-capture.json'
    if ($Mode -eq 'Capture') {
        $script:Stage = 'fresh-results-boundary'
        Require (-not (Test-Path -LiteralPath $results))
        $pairs = @($Projects | ForEach-Object { Pair $_ $true })
        $identities = @($pairs | ForEach-Object { $_.documents } | ForEach-Object { [ordered]@{ path = $_.path; sha256 = $_.sha256 } })
        $script:Stage = 'capture-publication'
        $temporary = [IO.Path]::Combine([IO.Path]::GetDirectoryName($results), '.coverage-capture-' + [Guid]::NewGuid().ToString('N'))
        [void] [IO.Directory]::CreateDirectory($temporary)
        try {
            WriteNewJson ([IO.Path]::Combine($temporary, 'coverage-capture.json')) ([ordered]@{ schemaVersion = 1; sourceRevision = $revision; buildStartedUtc = $script:BuildStart.ToString('O'); capturedUtc = [DateTime]::UtcNow.ToString('O'); projects = $Projects; pairs = $pairs; sourceIdentities = $identities })
            [IO.Directory]::Move($temporary, $results)
        }
        finally { if ([IO.Directory]::Exists($temporary)) { [IO.Directory]::Delete($temporary, $true) } }
        Write-Host '[coverage-evidence] CAPTURED: fresh outputs and test-bin copies verified'
        exit 0
    }

    $script:Stage = 'capture-roundtrip'
    $capture = [Text.Encoding]::UTF8.GetString((Bytes $captureRelative)) | ConvertFrom-Json -AsHashtable -DateKind String
    Require ($capture.schemaVersion -eq 1 -and $capture.sourceRevision -ceq $revision -and $capture.buildStartedUtc -ceq $script:BuildStart.ToString('O'))
    Require (($capture.projects -join [char]0) -ceq ($Projects -join [char]0))
    $capturedUtc = [DateTime]::Parse($capture.capturedUtc, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
    Require ($capturedUtc.Kind -eq [DateTimeKind]::Utc -and $capturedUtc -ge $script:BuildStart -and $capturedUtc -le [DateTime]::UtcNow)
    $pairs = @($Projects | ForEach-Object { Pair $_ $false })
    $script:Stage = 'captured-identities'
    Require ($capture.pairs.Count -eq $pairs.Count)
    for ($index = 0; $index -lt $pairs.Count; $index++) {
        Require ($pairs[$index].project -ceq $capture.pairs[$index].project -and $pairs[$index].dllSha256 -ceq $capture.pairs[$index].dllSha256 -and $pairs[$index].pdbSha256 -ceq $capture.pairs[$index].pdbSha256)
    }
    $identities = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($source in $capture.sourceIdentities) {
        $path = NormalizedPath $source.path
        Require (-not $identities.ContainsKey($path) -and $source.sha256 -cmatch '^[a-f0-9]{64}$')
        $identities.Add($path, $source.sha256)
    }
    $currentDocuments = @($pairs | ForEach-Object { $_.documents })
    Require ($identities.Count -eq $currentDocuments.Count)
    foreach ($source in $currentDocuments) { Require ($identities.ContainsKey($source.path) -and $identities[$source.path] -ceq $source.sha256) }

    $script:Stage = 'trx-execution'
    $trxFiles = @(Get-ChildItem -LiteralPath $results -Filter '*.trx' -File -Recurse)
    $rawFiles = @(Get-ChildItem -LiteralPath $results -Filter 'coverage.cobertura.xml' -File -Recurse)
    Require ($trxFiles.Count -eq 1 -and $rawFiles.Count -eq 1)
    foreach ($file in @($trxFiles[0], $rawFiles[0])) { Require ($file.LastWriteTimeUtc -ge $capturedUtc) }
    $trx = Xml ([IO.Path]::GetRelativePath($script:Root, $trxFiles[0].FullName))
    $ns = [Xml.XmlNamespaceManager]::new($trx.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $counters = @($trx.SelectNodes('/t:TestRun/t:ResultSummary/t:Counters', $ns))
    $times = @($trx.SelectNodes('/t:TestRun/t:Times', $ns))
    $outcomes = @($trx.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns))
    Require ($counters.Count -eq 1 -and $times.Count -eq 1)
    $executed = 0
    Require ([int]::TryParse($counters[0].GetAttribute('executed'), [ref] $executed) -and $executed -gt 0)
    Require ($counters[0].GetAttribute('total') -ceq $executed.ToString() -and $counters[0].GetAttribute('passed') -ceq $executed.ToString() -and $outcomes.Count -eq $executed)
    foreach ($name in @('failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')) { Require ($counters[0].GetAttribute($name) -ceq '0') }
    foreach ($attribute in $counters[0].Attributes) { if ($attribute.Name -cnotin @('total', 'executed', 'passed')) { Require ($attribute.Value -ceq '0') } }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($outcome in $outcomes) { Require ($outcome.GetAttribute('outcome') -ceq 'Passed' -and -not [string]::IsNullOrWhiteSpace($outcome.GetAttribute('executionId')) -and $ids.Add($outcome.GetAttribute('executionId'))) }
    $start = [DateTimeOffset]::Parse($times[0].GetAttribute('start'), [Globalization.CultureInfo]::InvariantCulture)
    $finish = [DateTimeOffset]::Parse($times[0].GetAttribute('finish'), [Globalization.CultureInfo]::InvariantCulture)
    Require ($start.UtcDateTime -ge $capturedUtc -and $finish -ge $start -and $finish.UtcDateTime -le [DateTime]::UtcNow)

    $script:Stage = 'raw-document-mapping'
    $raw = Xml ([IO.Path]::GetRelativePath($script:Root, $rawFiles[0].FullName))
    $reports = @()
    foreach ($pair in $pairs) {
        $packages = @($raw.SelectNodes('/coverage/packages/package') | Where-Object { $_.GetAttribute('name') -ceq $pair.project })
        Require ($packages.Count -eq 1)
        $documents = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        foreach ($document in $pair.documents) { $documents.Add($document.path, $document) }
        $files = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $union = [Collections.Generic.Dictionary[string, bool]]::new([StringComparer]::Ordinal)
        foreach ($class in $packages[0].SelectNodes('classes/class')) {
            $path = NormalizedPath $class.GetAttribute('filename')
            Require ($documents.ContainsKey($path))
            foreach ($line in $class.SelectNodes('lines/line')) {
                $number = 0
                $hits = 0L
                Require ([int]::TryParse($line.GetAttribute('number'), [ref] $number) -and $number -gt 0)
                Require ([long]::TryParse($line.GetAttribute('hits'), [ref] $hits) -and $hits -ge 0)
                [void] $files.Add($path)
                $key = $path + [char]0 + $number.ToString()
                if ($union.ContainsKey($key)) { $union[$key] = $union[$key] -or $hits -gt 0 } else { $union.Add($key, ($hits -gt 0)) }
            }
        }
        Require ($union.Count -gt 0)
        foreach ($document in $pair.documents) { if ($document.generated -and $document.visibleSequencePoints -gt 0) { Require ($files.Contains($document.path)) } }
        $reports += [ordered]@{ project = $pair.project; covered = @($union.Values | Where-Object { $_ }).Count; coverable = $union.Count; generatedDocumentsWithoutVisibleSequencePoints = @($pair.documents | Where-Object { $_.generated -and $_.visibleSequencePoints -eq 0 }).Count }
    }
    Require ((SourceRevision) -ceq $revision)
    $script:Stage = 'summary-publication'
    WriteNewJson (OwnedPath ($ResultsDirectory + '/coverage-evidence.json')) ([ordered]@{ schemaVersion = 1; sourceRevision = $revision; complete = $true; executedTests = $executed; nonMemberSourceCandidates = ($pairs.nonMemberSourceCandidates | Measure-Object -Sum).Sum; rawUnion = $reports; compiledMembershipCertified = $false })
    Write-Host "[coverage-evidence] VERIFIED: executed=$executed; selected assembly/source/raw identities consistent"
}
catch {
    [Console]::Error.WriteLine("[coverage-evidence] FAILED: stage=$script:Stage; type=$($_.Exception.GetType().FullName); line=$($_.InvocationInfo.ScriptLineNumber)")
    exit 2
}
