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
    public void Dockerfiles_AreDotNet10NonRootAndPinLegacyBuildDependencies()
    {
        var root = FindRoot();
        var dockerfiles = new[]
        {
            Path.Combine(root, "Legacy.Maliev.Intranet", "Dockerfile"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Dockerfile"),
        };

        Assert.All(dockerfiles, path =>
        {
            var dockerfile = File.ReadAllText(path);
            Assert.Contains("dotnet/sdk:10.0-alpine", dockerfile, StringComparison.Ordinal);
            Assert.Contains("dotnet/aspnet:10.0-alpine", dockerfile, StringComparison.Ordinal);
            Assert.Contains("USER $APP_UID", dockerfile, StringComparison.Ordinal);
            Assert.Contains("Legacy.Maliev.ServiceDefaults.git", dockerfile, StringComparison.Ordinal);
            Assert.Contains("checkout bcab875a7f703d1d9c2d535479e93653720eb62d", dockerfile, StringComparison.Ordinal);
            Assert.Contains("Legacy.Maliev.CompatibilityContracts.git", dockerfile, StringComparison.Ordinal);
            Assert.Contains("checkout 95c62eb6209411f5aada443b315447a2f76ca0cd", dockerfile, StringComparison.Ordinal);
            Assert.DoesNotContain("Maliev.Aspire.git", dockerfile, StringComparison.Ordinal);
            Assert.DoesNotContain("Maliev.MessagingContracts.git", dockerfile, StringComparison.Ordinal);
        });
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
