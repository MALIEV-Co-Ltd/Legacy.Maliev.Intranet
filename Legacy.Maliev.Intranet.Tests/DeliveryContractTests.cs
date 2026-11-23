namespace Legacy.Maliev.Intranet.Tests;

public sealed class DeliveryContractTests
{
    [Fact]
    public void DockerContext_ExcludesBuildAndRepositoryArtifacts()
    {
        var dockerIgnore = File.ReadAllText(Path.Combine(FindRoot(), ".dockerignore"));

        Assert.Contains(".git", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("**/bin", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("**/obj", dockerIgnore, StringComparison.Ordinal);
        Assert.Contains("**/TestResults", dockerIgnore, StringComparison.Ordinal);
    }

    [Fact]
    public void KubernetesResources_AreNamespaceConfinedAndProvisionNoInfrastructure()
    {
        var manifests = Directory.GetFiles(Path.Combine(FindRoot(), "deploy", "base"), "*.yaml");
        var combined = string.Join('\n', manifests.Select(File.ReadAllText));

        Assert.DoesNotContain("kind: Cluster", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("kind: NodePool", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("CloudSQL", combined, StringComparison.OrdinalIgnoreCase);
        Assert.All(
            manifests.Where(path => !path.EndsWith("kustomization.yaml", StringComparison.Ordinal)),
            path => Assert.Contains("namespace: maliev-legacy", File.ReadAllText(path), StringComparison.Ordinal));
    }

    [Fact]
    public void Deployments_AreSmallNonRootAndUseRuntimeSecretProjection()
    {
        var root = FindRoot();
        var deployments = new[]
        {
            Path.Combine(root, "deploy", "base", "deployment.yaml"),
            Path.Combine(root, "deploy", "base", "deployment-bff.yaml"),
        };

        Assert.All(deployments, path =>
        {
            var deployment = File.ReadAllText(path);
            Assert.Contains("replicas: 1", deployment, StringComparison.Ordinal);
            Assert.Contains("runAsNonRoot: true", deployment, StringComparison.Ordinal);
            Assert.Contains("readOnlyRootFilesystem: true", deployment, StringComparison.Ordinal);
            Assert.Contains("name: legacy-maliev-intranet-runtime", deployment, StringComparison.Ordinal);
            Assert.Contains("cpu: 50m", deployment, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Delivery_IsExplicitlyGatedAndNeverImperativelyAppliesManifests()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRoot(), ".github", "workflows", "publish-image.yml"));
        Assert.Contains("vars.LEGACY_DEPLOY_ENABLED == 'true'", workflow, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Workflows/.github/workflows/publish-image.yml@6017816", workflow, StringComparison.Ordinal);
        Assert.Contains("legacy-maliev-intranet-compatibility", workflow, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet/Dockerfile", workflow, StringComparison.Ordinal);
        Assert.Contains("legacy-maliev-intranet-bff", workflow, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Bff/Dockerfile", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("kubectl apply", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dockerfiles_AreDotNet11NonRootAndPinLegacyBuildDependencies()
    {
        var root = FindRoot();
        var dockerfiles = new[]
        {
            (Path: Path.Combine(root, "Legacy.Maliev.Intranet", "Dockerfile"),
                Sdk: "dotnet/sdk:11.0.100-alpine3.24", SdkDigest: "52556c0fb9d0f609ae9aeab9efeeba96f4acc2f7038f7696f2235ccbdbb8e290",
                Runtime: "dotnet/aspnet:11.0.0-alpine3.24", RuntimeDigest: "d39b72ad1534fa0faf52b488bffb0762df7ba269d120f0338713d4dda2a41bf5"),
            (Path: Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Dockerfile"),
                Sdk: "dotnet/sdk:11.0.100-alpine3.24", SdkDigest: "52556c0fb9d0f609ae9aeab9efeeba96f4acc2f7038f7696f2235ccbdbb8e290",
                Runtime: "dotnet/aspnet:11.0.0-alpine3.24", RuntimeDigest: "d39b72ad1534fa0faf52b488bffb0762df7ba269d120f0338713d4dda2a41bf5"),
        };

        Assert.All(dockerfiles, image =>
        {
            var dockerfile = File.ReadAllText(image.Path);
            Assert.Contains(image.Sdk, dockerfile, StringComparison.Ordinal);
            Assert.Contains(image.Runtime, dockerfile, StringComparison.Ordinal);
            Assert.Contains($"{image.Sdk}@sha256:{image.SdkDigest}", dockerfile, StringComparison.Ordinal);
            Assert.Contains($"{image.Runtime}@sha256:{image.RuntimeDigest}", dockerfile, StringComparison.Ordinal);
            Assert.Contains("USER $APP_UID", dockerfile, StringComparison.Ordinal);
            Assert.Contains("Legacy.Maliev.ServiceDefaults.git", dockerfile, StringComparison.Ordinal);
            Assert.Contains("checkout 173d63218d9142b34075bca87f5c6d933bcbdfb6", dockerfile, StringComparison.Ordinal);
            Assert.Contains("Legacy.Maliev.CompatibilityContracts.git", dockerfile, StringComparison.Ordinal);
            Assert.Contains("checkout fe17b8cea8ace3eef5434f8b29c9038f5c975613", dockerfile, StringComparison.Ordinal);
            Assert.DoesNotContain("Maliev.Aspire.git", dockerfile, StringComparison.Ordinal);
            Assert.DoesNotContain("Maliev.MessagingContracts.git", dockerfile, StringComparison.Ordinal);
            Assert.Contains("dotnet restore ", dockerfile, StringComparison.Ordinal);
            Assert.Contains("--locked-mode", dockerfile, StringComparison.Ordinal);
            Assert.Contains("--no-restore", dockerfile, StringComparison.Ordinal);
            Assert.Contains("build/nuget-locks/Legacy.Maliev.ServiceDefaults/packages.lock.json", dockerfile, StringComparison.Ordinal);
            Assert.Contains("build/nuget-locks/Legacy.Maliev.CompatibilityContracts/packages.lock.json", dockerfile, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ImageRestore_LocksEveryLocalProjectAndVerifiesBothDockerBuildsInCi()
    {
        var root = FindRoot();
        var projects = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.dependencies{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith("Tests", StringComparison.OrdinalIgnoreCase));

        Assert.All(projects, path =>
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "packages.lock.json")), $"Missing image restore lock for {path}"));
        Assert.True(File.Exists(Path.Combine(root, "build", "nuget-locks", "Legacy.Maliev.ServiceDefaults", "packages.lock.json")));
        Assert.True(File.Exists(Path.Combine(root, "build", "nuget-locks", "Legacy.Maliev.CompatibilityContracts", "packages.lock.json")));

        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "_build-and-test.yml"));
        Assert.Contains("docker build --file Legacy.Maliev.Intranet/Dockerfile --target build", workflow, StringComparison.Ordinal);
        Assert.Contains("docker build --file Legacy.Maliev.Intranet.Bff/Dockerfile --target build", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageBuilds_DoNotUseRetiredNodeGraphOrPruneHostImages()
    {
        var root = FindRoot();
        var paths = new[]
        {
            Path.Combine(root, "Legacy.Maliev.Intranet", "Dockerfile"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Dockerfile"),
            Path.Combine(root, ".github", "workflows", "publish-image.yml"),
        };

        Assert.All(paths, path =>
        {
            var content = File.ReadAllText(path);
            Assert.DoesNotContain("npm ", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("gulp", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("docker image prune", content, StringComparison.OrdinalIgnoreCase);
        });

        Assert.False(File.Exists(Path.Combine(root, "package.json")));
        Assert.False(File.Exists(Path.Combine(root, "package-lock.json")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "package.json")));
        Assert.False(File.Exists(Path.Combine(root, "Legacy.Maliev.Intranet", "package-lock.json")));
    }

    [Fact]
    public void Kustomization_DeclaresSeparateCompatibilityAndBffRuntimeResources()
    {
        var root = FindRoot();
        var kustomization = File.ReadAllText(Path.Combine(root, "deploy", "base", "kustomization.yaml"));

        Assert.Contains("deployment.yaml", kustomization, StringComparison.Ordinal);
        Assert.Contains("service.yaml", kustomization, StringComparison.Ordinal);
        Assert.Contains("legacy-maliev-intranet-compatibility", kustomization, StringComparison.Ordinal);
        Assert.Contains("deployment-bff.yaml", kustomization, StringComparison.Ordinal);
        Assert.Contains("service-bff.yaml", kustomization, StringComparison.Ordinal);
        Assert.Contains("network-policy-bff.yaml", kustomization, StringComparison.Ordinal);
        Assert.Contains("legacy-maliev-intranet-bff", kustomization, StringComparison.Ordinal);

        var bffDeployment = File.ReadAllText(Path.Combine(root, "deploy", "base", "deployment-bff.yaml"));
        Assert.Contains("path: /intranet-bff/readiness", bffDeployment, StringComparison.Ordinal);
        Assert.Contains("path: /intranet-bff/liveness", bffDeployment, StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
