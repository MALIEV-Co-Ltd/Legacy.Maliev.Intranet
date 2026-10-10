namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyServiceDefaultsIdentityContractTests
{
    private const string DotNetPatchVersion = "10.0.12";
    private const string NativeLoggingReplacementCommit = "4517cf16f5f1159318e184969732d46eae4a8308";

    [Fact]
    public void HostsAndDeliveryPinSharedNativeLoggingReplacement()
    {
        var root = FindRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "_build-and-test.yml"));
        Assert.Contains($"ref: {NativeLoggingReplacementCommit}", workflow, StringComparison.Ordinal);

        foreach (var host in new[] { "Legacy.Maliev.Intranet", "Legacy.Maliev.Intranet.Bff" })
        {
            var program = File.ReadAllText(Path.Combine(root, host, "Program.cs"));
            var project = File.ReadAllText(Path.Combine(root, host, $"{host}.csproj"));
            var dockerfile = File.ReadAllText(Path.Combine(root, host, "Dockerfile"));

            Assert.Contains("builder.AddServiceDefaults();", program, StringComparison.Ordinal);
            Assert.Contains("app.UseStandardMiddleware();", program, StringComparison.Ordinal);
            Assert.DoesNotContain("Maliev.NativeLogging", program, StringComparison.Ordinal);
            Assert.DoesNotContain("Maliev.NativeLogging", project, StringComparison.Ordinal);
            Assert.Contains($"checkout {NativeLoggingReplacementCommit}", dockerfile, StringComparison.Ordinal);
            Assert.DoesNotContain("Maliev.NativeLogging", dockerfile, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryWorkflowDefaultsCheckout_UsesQualifiedObserverProducer()
    {
        var consumers = ValidateWorkflowDefaultsDirectory(Path.Combine(FindRoot(), ".github", "workflows"));
        Assert.True(consumers >= 13, "All existing workflow consumers must retain qualified checkouts.");
    }

    [Theory]
    [InlineData("old-pin")]
    [InlineData("quoted-repository")]
    [InlineData("reordered-fields")]
    [InlineData("key-spacing")]
    [InlineData("wrong-path")]
    public void WorkflowDefaultsGuard_RejectsUnqualifiedOrUnparsedCheckout(string mutation)
    {
        var source = QualifiedWorkflowCheckout();
        source = mutation switch
        {
            "old-pin" => source.Replace(NativeLoggingReplacementCommit, new string('a', 40), StringComparison.Ordinal),
            "quoted-repository" => source.Replace("repository: MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults", "repository: 'MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults'", StringComparison.Ordinal),
            "reordered-fields" => $"path: .dependencies/Legacy.Maliev.ServiceDefaults\nref: {NativeLoggingReplacementCommit}\nrepository: MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults\n",
            "key-spacing" => source.Replace("repository: ", "repository : ", StringComparison.Ordinal),
            "wrong-path" => source.Replace(".dependencies/Legacy.Maliev.ServiceDefaults", ".dependencies/wrong", StringComparison.Ordinal),
            _ => throw new ArgumentException("Unknown mutation", nameof(mutation)),
        };
        Assert.Throws<InvalidOperationException>(() => ValidateWorkflowDefaults(source, mutation));
    }

    [Fact]
    public void WorkflowDefaultsGuard_RetainsValidConsumersAndRejectsAdditionalYamlBypass()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            for (var index = 0; index < 13; index++)
                File.WriteAllText(Path.Combine(directory, $"consumer-{index}.yml"), QualifiedWorkflowCheckout());
            Assert.Equal(13, ValidateWorkflowDefaultsDirectory(directory));
            File.WriteAllText(Path.Combine(directory, "extra.yaml"), QualifiedWorkflowCheckout()
                .Replace(NativeLoggingReplacementCommit, new string('a', 40), StringComparison.Ordinal));
            Assert.Throws<InvalidOperationException>(() => ValidateWorkflowDefaultsDirectory(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string QualifiedWorkflowCheckout() =>
        $"repository: MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults\nref: {NativeLoggingReplacementCommit}\npath: .dependencies/Legacy.Maliev.ServiceDefaults\n";

    private static int ValidateWorkflowDefaultsDirectory(string directory) => Directory.GetFiles(directory)
        .Where(path => Path.GetExtension(path) is ".yml" or ".yaml")
        .Sum(path => ValidateWorkflowDefaults(File.ReadAllText(path), path));

    private static int ValidateWorkflowDefaults(string source, string path)
    {
        const string producer = "MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults";
        var declared = System.Text.RegularExpressions.Regex.Matches(source,
            System.Text.RegularExpressions.Regex.Escape(producer),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
        var checkouts = System.Text.RegularExpressions.Regex.Matches(source,
            @"(?m)^\s*repository: MALIEV-Co-Ltd/Legacy\.Maliev\.ServiceDefaults\r?\n\s*ref: (?<pin>[^\r\n]+)\r?\n\s*path: (?<path>[^\r\n]+)");
        if (declared != checkouts.Count)
            throw new InvalidOperationException($"Unparsed Defaults producer reference in {path}; review checkout format.");
        foreach (System.Text.RegularExpressions.Match checkout in checkouts)
            if (checkout.Groups["pin"].Value.Trim() != NativeLoggingReplacementCommit ||
                checkout.Groups["path"].Value.Trim() != ".dependencies/Legacy.Maliev.ServiceDefaults")
                throw new InvalidOperationException($"Unqualified Defaults producer pin or path in {path}.");
        return checkouts.Count;
    }

    [Fact]
    public void HostsConsumeLegacyServiceDefaultsWithoutNewPlatformRepositoryCollision()
    {
        var root = FindRoot();
        foreach (var project in new[]
        {
            Path.Combine(root, "Legacy.Maliev.Intranet", "Legacy.Maliev.Intranet.csproj"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Legacy.Maliev.Intranet.Bff.csproj"),
        })
        {
            var source = File.ReadAllText(project);
            Assert.Contains("Legacy.Maliev.ServiceDefaults\\src\\Legacy.Maliev.ServiceDefaults\\Legacy.Maliev.ServiceDefaults.csproj", source, StringComparison.Ordinal);
            Assert.Contains("PackageReference Include=\"Legacy.Maliev.ServiceDefaults\"", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Maliev.Aspire\\Maliev.Aspire.ServiceDefaults", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PackageReference Include=\"Maliev.Aspire.ServiceDefaults\"", source, StringComparison.Ordinal);
        }

        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "_build-and-test.yml"));
        Assert.Contains("repository: MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults", workflow, StringComparison.Ordinal);
        Assert.Contains("path: .dependencies/Legacy.Maliev.ServiceDefaults", workflow, StringComparison.Ordinal);
        Assert.Contains($"ref: {NativeLoggingReplacementCommit}", workflow, StringComparison.Ordinal);
        Assert.Contains("repository: MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts", workflow, StringComparison.Ordinal);
        Assert.Contains("path: .dependencies/Legacy.Maliev.CompatibilityContracts", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("repository: MALIEV-Co-Ltd/Maliev.Aspire", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("repository: MALIEV-Co-Ltd/Maliev.MessagingContracts", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectFrameworkPackages_UseOneSupportedPatchBoundary()
    {
        var root = FindRoot();
        var projectSources = Directory
            .GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => IsIntranetProject(root, path))
            .Select(File.ReadAllText)
            .ToArray();

        var source = string.Join(Environment.NewLine, projectSources);
        Assert.DoesNotMatch("Microsoft\\.[^\"]+\" Version=\"10\\.0\\.(?:[0-9]|1[01])\"", source);

        foreach (var package in new[]
        {
            "Microsoft.AspNetCore.Components.Authorization",
            "Microsoft.AspNetCore.Components.Web",
            "Microsoft.AspNetCore.Components.WebAssembly",
            "Microsoft.AspNetCore.Components.WebAssembly.DevServer",
            "Microsoft.AspNetCore.Components.WebAssembly.Server",
            "Microsoft.AspNetCore.DataProtection.StackExchangeRedis",
            "Microsoft.AspNetCore.Mvc.Testing",
            "Microsoft.Extensions.Caching.StackExchangeRedis",
            "Microsoft.Extensions.Localization",
            "Microsoft.Extensions.Localization.Abstractions",
        })
        {
            Assert.Contains($"Include=\"{package}\" Version=\"{DotNetPatchVersion}\"", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FrameworkPackageScanExcludesCheckedOutDependencies()
    {
        var root = FindRoot();
        Assert.False(IsIntranetProject(root, Path.Combine(root, ".dependencies", "Legacy.Maliev.CustomerService", "CustomerService.Tests.csproj")));
        Assert.False(IsIntranetProject(root, Path.Combine(root, ".worktrees", "another-checkout", "Another.csproj")));
        Assert.True(IsIntranetProject(root, Path.Combine(root, "Legacy.Maliev.Intranet.Tests", "Legacy.Maliev.Intranet.Tests.csproj")));
    }

    [Fact]
    public void CompatibilityNamespaceRemainsStableWhileAssemblyAndPackageOwnershipChange()
    {
        var root = FindRoot();
        var webProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet", "Program.cs"));
        var bffProgram = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        Assert.Contains("using Maliev.Aspire.ServiceDefaults;", webProgram, StringComparison.Ordinal);
        Assert.Contains("using Maliev.Aspire.ServiceDefaults;", bffProgram, StringComparison.Ordinal);
        Assert.Contains("builder.AddServiceDefaults();", webProgram, StringComparison.Ordinal);
        Assert.Contains("builder.AddServiceDefaults();", bffProgram, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static bool IsIntranetProject(string root, string projectPath)
    {
        var relativePath = Path.GetRelativePath(root, projectPath);
        return !relativePath.StartsWith($".worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !relativePath.StartsWith($".dependencies{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
}
