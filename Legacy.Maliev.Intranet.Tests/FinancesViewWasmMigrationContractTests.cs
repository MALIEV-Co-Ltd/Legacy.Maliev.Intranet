using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Accounting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class FinancesViewWasmMigrationContractTests
{
    [Fact]
    public void FinanceView_IsLazyWasmAndUsesOwnedBffBoundaries()
    {
        var root = FindRepositoryRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages", "FinanceView.razor"));
        var program = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var proxy = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Accounting", "FinancesProxy.cs"));
        var files = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Accounting", "FinanceFileProxy.cs"));

        Assert.Contains("@page \"/Finances/View\"", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("uploadAttemptId", page, StringComparison.Ordinal);
        Assert.Contains("If-Unmodified-Since", proxy, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", proxy, StringComparison.Ordinal);
        Assert.Contains("/uploads/SignedUrl", files, StringComparison.Ordinal);
        Assert.Contains("/Uploads?bucket=maliev.com", files, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingUpdate", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingDelete", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingFilesWrite", program, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingFilesDelete", program, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_WhenMetadataLinkFails_DeletesEveryScannedObject()
    {
        var workflow = new FinanceFileWorkflow(NullLogger<FinanceFileWorkflow>.Instance);
        var stored = new[]
        {
            new FinanceStoredFile("maliev.com", "accounting/84/one.pdf"),
            new FinanceStoredFile("maliev.com", "accounting/84/two.pdf"),
        };
        var deleted = new List<string>();

        await Assert.ThrowsAsync<HttpRequestException>(() => workflow.UploadAsync(
            84,
            Array.Empty<IFormFile>(),
            (_, _) => Task.FromResult<IReadOnlyList<FinanceStoredFile>>(stored),
            (_, file, _) => file.ObjectName.EndsWith("two.pdf", StringComparison.Ordinal)
                ? throw new HttpRequestException("link failed")
                : Task.FromResult(new FinanceFileItem(11, 84, file.Bucket, file.ObjectName, null, null)),
            (_, _) => Task.CompletedTask,
            (file, _) => { deleted.Add(file.ObjectName); return Task.CompletedTask; },
            CancellationToken.None));

        Assert.Equal(stored.Select(value => value.ObjectName), deleted);
    }

    [Fact]
    public async Task Remove_RejectsFileNotOwnedByPaymentBeforeStorageWrite()
    {
        var workflow = new FinanceFileWorkflow(NullLogger<FinanceFileWorkflow>.Instance);
        var calls = 0;
        var removed = await workflow.RemoveAsync(
            99,
            [new FinanceFileItem(11, 84, "maliev.com", "accounting/84/one.pdf", null, null)],
            (_, _) => { calls++; return Task.CompletedTask; },
            (_, _) => { calls++; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.False(removed);
        Assert.Equal(0, calls);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
