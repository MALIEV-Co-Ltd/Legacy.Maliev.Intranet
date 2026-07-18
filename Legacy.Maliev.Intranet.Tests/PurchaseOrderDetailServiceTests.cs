using Legacy.Maliev.Intranet.PurchaseOrders;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class PurchaseOrderDetailServiceTests
{
    [Fact]
    public async Task GetAsync_AggregatesDisplayDataAndOnlySafeDownloadShape()
    {
        var gateway = new Gateway();
        var result = await new PurchaseOrderDetailService(gateway, NullLogger<PurchaseOrderDetailService>.Instance).GetAsync(84, CancellationToken.None);
        Assert.Equal(PurchaseOrderDetailStatus.Success, result.Status);
        Assert.Equal("Acme", result.Detail?.SupplierName);
        Assert.Equal("Somchai", result.Detail?.OrderedBy);
        Assert.Equal(200m, result.Detail?.Items.Single().Subtotal);
        Assert.Equal("PurchaseOrder_84.pdf", result.Detail?.Downloads.Single().Name);
        Assert.Equal("https://storage.test/signed", result.Detail?.Downloads.Single().Url.AbsoluteUri);
    }

    [Fact]
    public async Task DeleteAsync_RemovesStorageMetadataItemsThenParent()
    {
        var gateway = new Gateway();
        var result = await new PurchaseOrderDetailService(gateway, NullLogger<PurchaseOrderDetailService>.Instance).DeleteAsync(84, CancellationToken.None);
        Assert.Equal(PurchaseOrderDetailStatus.Success, result.Status);
        Assert.Equal(["stored:po.pdf", "link:5", "item:9", "order:84"], gateway.Deletes);
    }

    [Fact]
    public async Task DeleteAsync_DependencyFailureStopsBeforeParentAndReportsRetryablePartialFailure()
    {
        var gateway = new Gateway { FailLink = true };
        var result = await new PurchaseOrderDetailService(gateway, NullLogger<PurchaseOrderDetailService>.Instance).DeleteAsync(84, CancellationToken.None);
        Assert.Equal(PurchaseOrderDetailStatus.PartialFailure, result.Status);
        Assert.Equal(["stored:po.pdf"], gateway.Deletes);
    }

    private sealed class Gateway : IPurchaseOrderDetailGateway
    {
        public bool FailLink { get; init; }
        public List<string> Deletes { get; } = [];
        public Task<PurchaseOrderDetailData?> GetOrderAsync(int id, CancellationToken cancellationToken) => Task.FromResult<PurchaseOrderDetailData?>(new(id, 42, "Buyer", 7, "Courier", "Bangkok", "Net 30", "Note", new DateTime(2026, 7, 18)));
        public Task<string?> GetSupplierNameAsync(int id, CancellationToken cancellationToken) => Task.FromResult<string?>("Acme");
        public Task<string?> GetEmployeeNameAsync(int id, CancellationToken cancellationToken) => Task.FromResult<string?>("Somchai");
        public Task<IReadOnlyList<PurchaseOrderDetailItemData>> GetItemsAsync(int orderId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PurchaseOrderDetailItemData>>([new(9, orderId, "R-1", "Resin", 2, 100, null)]);
        public Task<IReadOnlyList<PurchaseOrderDetailFileData>> GetFilesAsync(int orderId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PurchaseOrderDetailFileData>>([new(5, orderId, "maliev.com", "purchaseorders/PurchaseOrder_84.pdf")]);
        public Task<Uri?> GetSignedUrlAsync(string bucket, string objectName, CancellationToken cancellationToken) => Task.FromResult<Uri?>(new("https://storage.test/signed"));
        public Task DeleteStoredFileAsync(string bucket, string objectName, CancellationToken cancellationToken) { Deletes.Add("stored:po.pdf"); return Task.CompletedTask; }
        public Task DeleteFileLinkAsync(int id, CancellationToken cancellationToken) { if (FailLink) throw new HttpRequestException("failed"); Deletes.Add($"link:{id}"); return Task.CompletedTask; }
        public Task DeleteItemAsync(int id, CancellationToken cancellationToken) { Deletes.Add($"item:{id}"); return Task.CompletedTask; }
        public Task DeleteOrderAsync(int id, CancellationToken cancellationToken) { Deletes.Add($"order:{id}"); return Task.CompletedTask; }
    }
}
