using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.Server.Accounting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class FinancesCreateWasmMigrationContractTests
{
    [Fact]
    public void FinanceCreate_PreservesQuickFormAndReplaySafeWriteContracts()
    {
        var root = FindRoot();
        var page = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client.Features.Accounting", "Pages", "FinanceCreate.razor"));
        var bff = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Program.cs"));
        var proxy = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Bff", "Accounting", "FinancesProxy.cs"));
        Assert.Contains("@page \"/Finances/Create\"", page, StringComparison.Ordinal);
        Assert.Contains("paymentDirectionIdInput", page, StringComparison.Ordinal);
        Assert.Contains("Google Asia Pacific Pte. Ltd.", page, StringComparison.Ordinal);
        Assert.Contains("workflowId", page, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", page, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", proxy, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", bff, StringComparison.Ordinal);
        Assert.Contains("LegacyEmployeePermissions.AccountingCreate", bff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownLinkOutcome_RetainsArtifactsForSameKeyReplay()
    {
        var workflow = new FinanceFileWorkflow(NullLogger<FinanceFileWorkflow>.Instance);
        var deleted = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.UploadAsync(
            84,
            Array.Empty<IFormFile>(),
            (_, _) => Task.FromResult<IReadOnlyList<FinanceStoredFile>>([new("maliev.com", "accounting/payments/84/a.pdf")]),
            (_, _, _) => throw new InvalidOperationException("unknown"),
            (_, _) => Task.CompletedTask,
            (_, _) => { deleted++; return Task.CompletedTask; },
            CancellationToken.None,
            _ => false));
        Assert.Equal(0, deleted);
    }

    private static string FindRoot()
    {
        var value = new DirectoryInfo(AppContext.BaseDirectory);
        while (value is not null && !File.Exists(Path.Combine(value.FullName, "Legacy.Maliev.Intranet.slnx"))) value = value.Parent;
        return value?.FullName ?? throw new DirectoryNotFoundException();
    }
}
