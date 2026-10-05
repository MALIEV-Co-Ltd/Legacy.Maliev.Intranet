using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CoverageEvidenceVerifierTests
{
    [Fact]
    public async Task CompleteEvidence_VerifiesActualAssemblyPdbSourcesAndControlledResults()
    {
        using var fixture = new EvidenceFixture();
        Assert.Equal(0, (await fixture.RunAsync("Capture")).ExitCode);
        fixture.WriteResults();
        Assert.Equal(0, (await fixture.RunAsync("Verify")).ExitCode);
        Assert.True(File.Exists(fixture.SummaryPath));
        using var summary = JsonDocument.Parse(File.ReadAllText(fixture.SummaryPath));
        Assert.True(summary.RootElement.GetProperty("complete").GetBoolean());
        Assert.Equal(2, summary.RootElement.GetProperty("executedTests").GetInt32());
    }

    [Fact]
    public async Task Capture_UnrebuiltAssemblyBeforeBuildStart_IsRejected()
    {
        using var fixture = new EvidenceFixture();
        File.SetLastWriteTimeUtc(fixture.DllPath, fixture.BuildStartedUtc.AddSeconds(-1));
        Assert.NotEqual(0, (await fixture.RunAsync("Capture")).ExitCode);
    }

    [Fact]
    public async Task Capture_PreexistingResultsDirectory_IsRejected()
    {
        using var fixture = new EvidenceFixture();
        Directory.CreateDirectory(fixture.ResultsPath);
        Assert.NotEqual(0, (await fixture.RunAsync("Capture")).ExitCode);
    }

    [Fact]
    public async Task Capture_DifferentActualAssemblyPdb_IsRejected()
    {
        using var fixture = new EvidenceFixture();
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Legacy.Maliev.Intranet.Server.pdb"), fixture.PdbPath, overwrite: true);
        File.SetLastWriteTimeUtc(fixture.PdbPath, DateTime.UtcNow);
        Assert.NotEqual(0, (await fixture.RunAsync("Capture")).ExitCode);
    }

    [Theory]
    [InlineData("dll")]
    [InlineData("pdb")]
    [InlineData("source")]
    public async Task Verify_ReplacedCapturedIdentity_IsRejected(string identity)
    {
        using var fixture = new EvidenceFixture();
        Assert.Equal(0, (await fixture.RunAsync("Capture")).ExitCode);
        fixture.WriteResults();
        var path = identity switch { "dll" => fixture.DllPath, "pdb" => fixture.PdbPath, _ => fixture.AuthoredSourcePath };
        using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write)) stream.WriteByte(0);
        Assert.NotEqual(0, (await fixture.RunAsync("Verify")).ExitCode);
    }

    [Fact]
    public async Task Capture_MissingActualExecutableGeneratedSource_IsRejected()
    {
        using var fixture = new EvidenceFixture();
        Assert.True(File.Exists(fixture.ExecutableGeneratedSourcePath));
        File.Delete(fixture.ExecutableGeneratedSourcePath);
        Assert.NotEqual(0, (await fixture.RunAsync("Capture")).ExitCode);
    }

    [Fact]
    public async Task Verify_DebugSourceCandidate_IsNotReleaseMembership()
    {
        using var fixture = new EvidenceFixture();
        var debug = Path.Combine(fixture.Root, EvidenceFixture.Project, "obj", "Debug", "net10.0", "Unrelated.g.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(debug)!);
        File.WriteAllText(debug, "// Synthetic Debug candidate, not a Release PDB document.\n");
        Assert.Equal(0, (await fixture.RunAsync("Capture")).ExitCode);
        fixture.WriteResults();
        Assert.Equal(0, (await fixture.RunAsync("Verify")).ExitCode);
        Assert.True(File.Exists(fixture.SummaryPath));
        using var summary = JsonDocument.Parse(File.ReadAllText(fixture.SummaryPath));
        Assert.Equal(1, summary.RootElement.GetProperty("nonMemberSourceCandidates").GetInt32());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("old")]
    [InlineData("inconsistent")]
    public async Task Verify_IncompleteOrOtherExecutionTrx_IsRejected(string condition)
    {
        using var fixture = new EvidenceFixture();
        Assert.Equal(0, (await fixture.RunAsync("Capture")).ExitCode);
        fixture.WriteResults();
        var trx = Path.Combine(fixture.ResultsPath, "coverage.trx");
        if (condition == "missing") File.Delete(trx);
        else if (condition == "duplicate") File.Copy(trx, Path.Combine(fixture.ResultsPath, "earlier.trx"));
        else if (condition == "old") File.SetLastWriteTimeUtc(trx, fixture.BuildStartedUtc.AddSeconds(-1));
        else File.WriteAllText(trx, File.ReadAllText(trx).Replace("executed=\"2\"", "executed=\"3\"", StringComparison.Ordinal));
        Assert.NotEqual(0, (await fixture.RunAsync("Verify")).ExitCode);
    }

    [Fact]
    public async Task Verify_RawFileAbsentFromReleasePdb_IsRejected()
    {
        using var fixture = new EvidenceFixture();
        Assert.Equal(0, (await fixture.RunAsync("Capture")).ExitCode);
        fixture.WriteResults();
        var document = XDocument.Load(fixture.CoveragePath);
        document.Descendants("class").First().SetAttributeValue("filename", EvidenceFixture.Project + "/NotCompiled.cs");
        document.Save(fixture.CoveragePath);
        Assert.NotEqual(0, (await fixture.RunAsync("Verify")).ExitCode);
    }

    [Fact]
    public async Task Verify_ExecutableGeneratedDocumentMissingRaw_IsRejected()
    {
        using var fixture = new EvidenceFixture();
        Assert.Equal(0, (await fixture.RunAsync("Capture")).ExitCode);
        fixture.WriteResults();
        var document = XDocument.Load(fixture.CoveragePath);
        document.Descendants("class").First(element => element.Attribute("filename")!.Value.Contains("/obj/", StringComparison.Ordinal)).Remove();
        document.Save(fixture.CoveragePath);
        Assert.NotEqual(0, (await fixture.RunAsync("Verify")).ExitCode);
    }

    [Fact]
    public async Task Verify_ContradictoryNormalizedSourceIdentity_IsRejected()
    {
        using var fixture = new EvidenceFixture();
        Assert.Equal(0, (await fixture.RunAsync("Capture")).ExitCode);
        fixture.WriteResults();
        Assert.True(File.Exists(fixture.CapturePath));
        var capture = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(fixture.CapturePath))!;
        var identities = capture["sourceIdentities"]!.AsArray();
        var conflicting = identities[0]!.DeepClone();
        conflicting["path"] = conflicting["path"]!.GetValue<string>().Replace('/', '\\');
        conflicting["sha256"] = Convert.ToHexString(SHA256.HashData(new byte[] { 1 })).ToLowerInvariant();
        identities.Add(conflicting);
        File.WriteAllText(fixture.CapturePath, capture.ToJsonString());
        Assert.NotEqual(0, (await fixture.RunAsync("Verify")).ExitCode);
    }

    private sealed class EvidenceFixture : IDisposable
    {
        internal const string Project = "Legacy.Maliev.Intranet.Bff";
        private readonly string _repository;
        private readonly string _revision;
        private readonly XDocument _coverage;
        private readonly string _fallbackCoverage;
        private bool _preserveRoot;
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "intranet-coverage-evidence-" + Guid.NewGuid().ToString("N"));
        internal DateTime BuildStartedUtc { get; } = DateTime.UtcNow.AddMinutes(-1);
        internal string ResultsPath => Path.Combine(Root, "TestResults");
        internal string CoveragePath => Path.Combine(ResultsPath, "coverage.cobertura.xml");
        internal string SummaryPath => Path.Combine(ResultsPath, "coverage-evidence.json");
        internal string CapturePath => Path.Combine(ResultsPath, "coverage-capture.json");
        internal string DllPath => Path.Combine(Root, Project, "bin", "Release", "net10.0", Project + ".dll");
        internal string PdbPath => Path.ChangeExtension(DllPath, ".pdb");
        internal string AuthoredSourcePath { get; }
        internal string ExecutableGeneratedSourcePath { get; }

        internal EvidenceFixture()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
            _repository = directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DllPath)!);
                File.Copy(Path.Combine(AppContext.BaseDirectory, Project + ".dll"), DllPath);
                File.Copy(Path.Combine(AppContext.BaseDirectory, Project + ".pdb"), PdbPath);
                File.SetLastWriteTimeUtc(DllPath, DateTime.UtcNow);
                File.SetLastWriteTimeUtc(PdbPath, DateTime.UtcNow);
                using var git = Process.Start(new ProcessStartInfo("git") { WorkingDirectory = _repository, RedirectStandardOutput = true, ArgumentList = { "rev-parse", "HEAD" } })!;
                _revision = git.StandardOutput.ReadToEnd().Trim();
                git.WaitForExit();
                Assert.Equal(0, git.ExitCode);

                using var stream = File.OpenRead(PdbPath);
                using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
                var reader = provider.GetMetadataReader();
                var classes = new XElement("classes");
                var documents = new Dictionary<DocumentHandle, string>();
                foreach (var handle in reader.Documents)
                {
                    var document = reader.GetDocument(handle);
                    var name = reader.GetString(document.Name).Replace('\\', '/');
                    var start = name.IndexOf(Project + "/", StringComparison.Ordinal);
                    Assert.True(start >= 0);
                    var relative = name[start..];
                    var target = Path.GetFullPath(Path.Combine(Root, relative));
                    Assert.StartsWith(Root + Path.DirectorySeparatorChar, target, StringComparison.Ordinal);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var source = Path.Combine(_repository, relative);
                    if (File.Exists(source)) File.Copy(source, target);
                    else File.WriteAllBytes(target, ReadEmbeddedSource(reader, handle));
                    Assert.Equal(Convert.ToHexString(reader.GetBlobBytes(document.Hash)), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))));
                    documents.Add(handle, relative);
                }

                var points = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
                foreach (var handle in reader.MethodDebugInformation)
                {
                    var method = reader.GetMethodDebugInformation(handle);
                    foreach (var point in method.GetSequencePoints())
                    {
                        if (point.IsHidden) continue;
                        var document = point.Document.IsNil ? method.Document : point.Document;
                        if (document.IsNil) continue;
                        var relative = documents[document];
                        if (!points.TryGetValue(relative, out var lines)) points.Add(relative, lines = []);
                        lines.Add(point.StartLine);
                    }
                }
                AuthoredSourcePath = Path.Combine(Root, points.Keys.First(path => !path.Contains("/obj/", StringComparison.Ordinal)));
                ExecutableGeneratedSourcePath = Path.Combine(Root, points.Keys.First(path => path.Contains("/obj/", StringComparison.Ordinal)));
                foreach (var entry in points)
                    classes.Add(new XElement("class", new XAttribute("filename", entry.Key), new XElement("lines", entry.Value.Select(line => new XElement("line", new XAttribute("number", line), new XAttribute("hits", 1))))));
                _coverage = new XDocument(new XElement("coverage", new XElement("packages",
                    new XElement("package", new XAttribute("name", Project), new XAttribute("line-rate", 1), classes),
                    new XElement("package", new XAttribute("name", "Legacy.Maliev.Intranet.Server"), new XAttribute("line-rate", 1)),
                    new XElement("package", new XAttribute("name", "Legacy.Maliev.Intranet.Contracts"), new XAttribute("line-rate", 1)))));
                _fallbackCoverage = Path.Combine(Root, "controlled-coverage.xml");
                _coverage.Save(_fallbackCoverage);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void WriteResults()
        {
            Directory.CreateDirectory(ResultsPath);
            _coverage.Save(CoveragePath);
            File.WriteAllText(Path.Combine(ResultsPath, "coverage.trx"), """
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <Results><UnitTestResult executionId="00000000-0000-0000-0000-000000000001" outcome="Passed"/><UnitTestResult executionId="00000000-0000-0000-0000-000000000002" outcome="Passed"/></Results>
                  <ResultSummary outcome="Completed"><Counters total="2" executed="2" passed="2" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0"/></ResultSummary>
                </TestRun>
                """);
            var trx = XDocument.Load(Path.Combine(ResultsPath, "coverage.trx"));
            var ns = trx.Root!.Name.Namespace;
            var time = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            trx.Root.AddFirst(new XElement(ns + "Times", new XAttribute("creation", time), new XAttribute("queuing", time), new XAttribute("start", time), new XAttribute("finish", time)));
            trx.Save(Path.Combine(ResultsPath, "coverage.trx"));
        }

        internal async Task<(int ExitCode, string Output)> RunAsync(string mode)
        {
            var script = Path.Combine(_repository, "scripts", "verify-coverage-evidence.ps1");
            var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(File.Exists(script) ? script : Path.Combine(_repository, "scripts", "verify-test-coverage.ps1"));
            if (File.Exists(script))
            {
                foreach (var argument in new[] { "-Mode", mode, "-WorkspaceRoot", Root, "-SourceRepositoryRoot", _repository, "-ExpectedSourceRevision", _revision, "-ResultsDirectory", "TestResults", "-Projects", Project, "-BuildStartedUtc", BuildStartedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture) }) start.ArgumentList.Add(argument);
            }
            else
            {
                // Tests-first fallback exercises today's actual rate-only guard; it cannot validate provenance.
                start.ArgumentList.Add("-CoverageFile");
                start.ArgumentList.Add(_fallbackCoverage);
            }
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await process.WaitForExitAsync(cleanupTimeout.Token);
                }
                catch (Exception)
                {
                    // Keep the primary timeout and do not delete files while child exit is uncertain.
                    _preserveRoot = true;
                }
                throw;
            }
            return (process.ExitCode, await output + await error);
        }

        private static byte[] ReadEmbeddedSource(MetadataReader reader, DocumentHandle handle)
        {
            foreach (var informationHandle in reader.GetCustomDebugInformation(handle))
            {
                var information = reader.GetCustomDebugInformation(informationHandle);
                if (reader.GetGuid(information.Kind) != new Guid("0e8a571b-6926-466e-b4ad-8ab04611f5fe")) continue;
                var bytes = reader.GetBlobBytes(information.Value);
                var length = BitConverter.ToInt32(bytes, 0);
                if (length == 0) return bytes[4..];
                using var input = new MemoryStream(bytes, 4, bytes.Length - 4);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                deflate.CopyTo(output);
                Assert.Equal(length, output.Length);
                return output.ToArray();
            }
            throw new InvalidOperationException("Actual PDB source content unavailable.");
        }

        public void Dispose()
        {
            if (!_preserveRoot && Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
