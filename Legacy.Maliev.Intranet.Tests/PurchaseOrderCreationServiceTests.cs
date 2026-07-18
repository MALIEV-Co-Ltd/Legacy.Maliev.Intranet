using Legacy.Maliev.Intranet.Contracts;
using Legacy.Maliev.Intranet.PurchaseOrders;
using Microsoft.Extensions.Logging.Abstractions;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class PurchaseOrderCreationServiceTests
{
    [Fact]
    public async Task CreateAsync_CompletesOrderItemsQuestPdfUploadAndMetadataLink()
    {
        var gateway = new Gateway();
        var service = new PurchaseOrderCreationService(gateway, NullLogger<PurchaseOrderCreationService>.Instance);

        var result = await service.CreateAsync(Request, "attempt-1", CancellationToken.None);

        Assert.Equal(PurchaseOrderCreationStatus.Created, result.Status);
        Assert.Equal(84, result.PurchaseOrderId);
        Assert.Equal([9, 10], gateway.CreatedItemIds);
        Assert.Equal([("attempt-1", 0), ("attempt-1", 1)], gateway.ItemAttempts);
        Assert.NotNull(gateway.Document);
        Assert.Equal("ไม้เอก ไม้โท", gateway.Document!.Notes);
        Assert.Equal("คุณสมชาย", gateway.Document.Supplier.ContactName);
        Assert.Equal(84, gateway.UploadedOrderId);
        Assert.Equal(5, gateway.LinkedFileId);
        Assert.Equal("attempt-1", gateway.LinkAttempt);
    }

    [Fact]
    public async Task CreateAsync_FileLinkFailure_CompensatesStoredFileItemsAndOrderInReverseOwnershipOrder()
    {
        var gateway = new Gateway { FailFileLink = true };
        var service = new PurchaseOrderCreationService(gateway, NullLogger<PurchaseOrderCreationService>.Instance);

        var result = await service.CreateAsync(Request, "attempt-2", CancellationToken.None);

        Assert.Equal(PurchaseOrderCreationStatus.Unavailable, result.Status);
        Assert.Equal(
            ["stored:purchaseorders/84.pdf", "item:10", "item:9", "order:84"],
            gateway.Compensations);
    }

    private static readonly PurchaseOrderCreateRequest Request = new()
    {
        SupplierId = 4,
        SupplierContactPerson = "คุณสมชาย",
        ShippingAddressId = 1,
        ShippingCompanyName = "MALIEV Co., Ltd.",
        BillingAddressId = 2,
        BillingCompanyName = "MALIEV Co., Ltd.",
        EmployeeId = 7,
        Fob = "Bangkok",
        Terms = "Net 30",
        ShippingMethod = "Courier",
        Notes = "ไม้เอก ไม้โท",
        Items =
        [
            new() { PartNumber = "R-1", Description = "Resin", Quantity = 2, UnitPrice = 450m },
            new() { PartNumber = "M-1", Description = "Metal", Quantity = 1, UnitPrice = 900m },
        ],
    };

    private sealed class Gateway : IPurchaseOrderCreationGateway
    {
        private int nextItemId = 9;

        public bool FailFileLink { get; init; }
        public List<int> CreatedItemIds { get; } = [];
        public List<(string AttemptId, int ItemIndex)> ItemAttempts { get; } = [];
        public List<string> Compensations { get; } = [];
        public PurchaseOrderPdfDocument? Document { get; private set; }
        public int? UploadedOrderId { get; private set; }
        public int? LinkedFileId { get; private set; }
        public string? LinkAttempt { get; private set; }

        public Task<PurchaseOrderCreateOptions> GetOptionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PurchaseOrderCreateOptions([], [], []));

        public Task<PurchaseOrderCreatedData> CreateOrderAsync(PurchaseOrderCreateRequest request, string attemptId, CancellationToken cancellationToken) =>
            Task.FromResult(new PurchaseOrderCreatedData(84, new DateTime(2030, 7, 15, 10, 30, 0, DateTimeKind.Utc)));

        public Task<int> CreateItemAsync(int purchaseOrderId, PurchaseOrderCreateItem item, string attemptId, int itemIndex, CancellationToken cancellationToken)
        {
            var id = nextItemId++;
            CreatedItemIds.Add(id);
            ItemAttempts.Add((attemptId, itemIndex));
            return Task.FromResult(id);
        }

        public Task<PurchaseOrderDocumentReferences> GetDocumentReferencesAsync(PurchaseOrderCreateRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new PurchaseOrderDocumentReferences(
                new PurchaseOrderParty("ACME", request.SupplierContactPerson, "02", "08", null, Address("Supplier road")),
                Address("Shipping road"),
                Address("Billing road"),
                "Natt",
                new Dictionary<int, string> { [66] = "Thailand" }));

        public Task<byte[]> RenderPdfAsync(PurchaseOrderPdfDocument document, CancellationToken cancellationToken)
        {
            Document = document;
            return Task.FromResult("%PDF-1.7"u8.ToArray());
        }

        public Task<PurchaseOrderStoredFile> UploadPdfAsync(int purchaseOrderId, byte[] pdf, string attemptId, CancellationToken cancellationToken)
        {
            UploadedOrderId = purchaseOrderId;
            return Task.FromResult(new PurchaseOrderStoredFile("maliev.com", "purchaseorders/84.pdf"));
        }

        public Task<int> LinkFileAsync(int purchaseOrderId, PurchaseOrderStoredFile file, string attemptId, CancellationToken cancellationToken)
        {
            LinkAttempt = attemptId;
            if (FailFileLink)
            {
                throw new HttpRequestException("link failed");
            }

            LinkedFileId = 5;
            return Task.FromResult(5);
        }

        public Task DeleteFileLinkAsync(int fileId, CancellationToken cancellationToken)
        {
            Compensations.Add($"link:{fileId}");
            return Task.CompletedTask;
        }

        public Task DeleteStoredFileAsync(PurchaseOrderStoredFile file, CancellationToken cancellationToken)
        {
            Compensations.Add($"stored:{file.ObjectName}");
            return Task.CompletedTask;
        }

        public Task DeleteItemAsync(int itemId, CancellationToken cancellationToken)
        {
            Compensations.Add($"item:{itemId}");
            return Task.CompletedTask;
        }

        public Task DeleteOrderAsync(int purchaseOrderId, CancellationToken cancellationToken)
        {
            Compensations.Add($"order:{purchaseOrderId}");
            return Task.CompletedTask;
        }

        private static PurchaseOrderPostalAddress Address(string line) =>
            new(line, null, null, "Bangkok", null, "10110", 66);
    }
}
