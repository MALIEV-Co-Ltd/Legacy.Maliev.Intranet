using System.Xml.Linq;

namespace Legacy.Maliev.Intranet.Tests;

/// <summary>Guards localized failure guidance used when a purchase-order response is lost.</summary>
public sealed class PurchaseOrderFailureMessageContractTests
{
    [Theory]
    [InlineData("PurchaseOrderCreate.resx", "Reconcile this attempt", "rolled back")]
    [InlineData("PurchaseOrderCreate.th.resx", "ตรวจสอบผลของคำขอนี้", "ยกเลิกการเปลี่ยนแปลง")]
    public void UnconfirmedFailureGuidanceRequiresReconciliationWithoutClaimingRollback(
        string resourceFile, string reconciliation, string rollbackClaim)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.Client.Features.Procurement", "Pages", resourceFile);
        var values = XDocument.Load(path).Root!.Elements("data")
            .Where(value => (string?)value.Attribute("name") == "CreateFailed")
            .Select(value => (string?)value.Element("value")).ToArray();
        var message = Assert.Single(values);
        Assert.NotNull(message);
        Assert.Contains(reconciliation, message, StringComparison.Ordinal);
        Assert.DoesNotContain(rollbackClaim, message, StringComparison.OrdinalIgnoreCase);
    }
}
