using System.Text.Json.Serialization;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.Intranet.PurchaseOrders;

/// <summary>Normalized outcomes from the complete purchase-order creation workflow.</summary>
public enum PurchaseOrderCreationStatus
{
    /// <summary>The order, line items, PDF, clean object and metadata link were created.</summary>
    Created,
    /// <summary>A downstream service rejected invalid input.</summary>
    BadRequest,
    /// <summary>The server-owned service identity was not authenticated.</summary>
    Unauthorized,
    /// <summary>The server-owned service identity lacked a required permission.</summary>
    Forbidden,
    /// <summary>The workflow identity conflicts with an existing operation.</summary>
    Conflict,
    /// <summary>A downstream service throttled the operation.</summary>
    RateLimited,
    /// <summary>A successful downstream response could not be validated.</summary>
    BadGateway,
    /// <summary>A downstream boundary was unavailable without an acknowledged workflow creation.</summary>
    Unavailable,
    /// <summary>The workflow failed and complete compensation could not be proven.</summary>
    OutcomeUnknown,
}

/// <summary>Safe workflow result returned to the thin BFF endpoint.</summary>
public sealed record PurchaseOrderCreationResult(
    PurchaseOrderCreationStatus Status,
    int? PurchaseOrderId = null,
    TimeSpan? RetryAfter = null);

/// <summary>Safe result for loading browser purchase-order selector options.</summary>
public sealed record PurchaseOrderOptionsResult(
    PurchaseOrderCreationStatus Status,
    PurchaseOrderCreateOptions? Options = null,
    TimeSpan? RetryAfter = null);

/// <summary>Transport failure normalized by a downstream gateway.</summary>
public sealed class PurchaseOrderGatewayException(
    PurchaseOrderCreationStatus status,
    TimeSpan? retryAfter = null,
    Exception? innerException = null)
    : Exception("A purchase-order service boundary rejected the operation.", innerException)
{
    /// <summary>Gets the normalized failure status.</summary>
    public PurchaseOrderCreationStatus Status { get; } = status;
    /// <summary>Gets the bounded downstream retry delay, when supplied.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>Created ProcurementService purchase-order projection.</summary>
public sealed record PurchaseOrderCreatedData(int Id, DateTime CreatedDate);

/// <summary>Postal address required by the QuestPDF purchase-order contract.</summary>
public sealed record PurchaseOrderPostalAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? Building,
    string? City,
    string? State,
    string? PostalCode,
    int CountryId);

/// <summary>Supplier party used while constructing the PDF.</summary>
public sealed record PurchaseOrderParty(
    string CompanyName,
    string? ContactName,
    string? Telephone,
    string? Mobile,
    string? Fax,
    PurchaseOrderPostalAddress Address);

/// <summary>Reference data required after the purchase order has been created.</summary>
public sealed record PurchaseOrderDocumentReferences(
    PurchaseOrderParty Supplier,
    PurchaseOrderPostalAddress ShippingAddress,
    PurchaseOrderPostalAddress BillingAddress,
    string EmployeeFullName,
    IReadOnlyDictionary<int, string> Countries);

/// <summary>DocumentService purchase-order contract rendered through QuestPDF.</summary>
public sealed record PurchaseOrderPdfDocument(
    PurchaseOrderPdfParty Billing,
    DateTime Date,
    [property: JsonPropertyName("FOB")] string? Fob,
    string? Notes,
    string OrderedBy,
    IReadOnlyList<PurchaseOrderPdfLine> OrderItems,
    int ReferenceNumber,
    string? ShippedVia,
    PurchaseOrderPdfParty Shipping,
    PurchaseOrderPdfParty Supplier,
    string? Terms);

/// <summary>Company and contact block serialized to DocumentService.</summary>
public sealed record PurchaseOrderPdfParty(
    PurchaseOrderPdfAddress Address,
    string CompanyName,
    string? ContactName,
    string? Fax,
    string? Mobile,
    string? Telephone);

/// <summary>Resolved postal-address block serialized to DocumentService.</summary>
public sealed record PurchaseOrderPdfAddress(
    string? AddressLine1,
    string? AddressLine2,
    string? Building,
    string? City,
    string? Country,
    string? PostalCode,
    string? State);

/// <summary>Calculated purchase-order line serialized to DocumentService.</summary>
public sealed record PurchaseOrderPdfLine(
    string Currency,
    string Description,
    string? PartNumber,
    int Quantity,
    decimal Subtotal,
    decimal UnitPrice);

/// <summary>Clean FileService object awaiting ProcurementService metadata linking.</summary>
public sealed record PurchaseOrderStoredFile(string Bucket, string ObjectName);

/// <summary>Transport-only boundary used by the server workflow.</summary>
public interface IPurchaseOrderCreationGateway
{
    /// <summary>Loads supplier, employee and address selector options.</summary>
    Task<PurchaseOrderCreateOptions> GetOptionsAsync(CancellationToken cancellationToken);
    /// <summary>Creates the root purchase order with a replay-safe attempt identifier.</summary>
    Task<PurchaseOrderCreatedData> CreateOrderAsync(PurchaseOrderCreateRequest request, string attemptId, CancellationToken cancellationToken);
    /// <summary>Creates one line item owned by the root order.</summary>
    Task<int> CreateItemAsync(int purchaseOrderId, PurchaseOrderCreateItem item, string attemptId, int itemIndex, CancellationToken cancellationToken);
    /// <summary>Loads references required to construct the QuestPDF contract.</summary>
    Task<PurchaseOrderDocumentReferences> GetDocumentReferencesAsync(PurchaseOrderCreateRequest request, CancellationToken cancellationToken);
    /// <summary>Renders the purchase-order PDF through DocumentService.</summary>
    Task<byte[]> RenderPdfAsync(PurchaseOrderPdfDocument document, CancellationToken cancellationToken);
    /// <summary>Uploads and scans the generated PDF through FileService.</summary>
    Task<PurchaseOrderStoredFile> UploadPdfAsync(int purchaseOrderId, byte[] pdf, string attemptId, CancellationToken cancellationToken);
    /// <summary>Links the clean object metadata to the purchase order.</summary>
    Task<int> LinkFileAsync(int purchaseOrderId, PurchaseOrderStoredFile file, string attemptId, CancellationToken cancellationToken);
    /// <summary>Deletes a linked file-metadata record during compensation.</summary>
    Task DeleteFileLinkAsync(int fileId, CancellationToken cancellationToken);
    /// <summary>Deletes a clean stored object during compensation.</summary>
    Task DeleteStoredFileAsync(PurchaseOrderStoredFile file, CancellationToken cancellationToken);
    /// <summary>Deletes a line item during compensation.</summary>
    Task DeleteItemAsync(int itemId, CancellationToken cancellationToken);
    /// <summary>Deletes the root purchase order during compensation.</summary>
    Task DeleteOrderAsync(int purchaseOrderId, CancellationToken cancellationToken);
}

/// <summary>Owns purchase-order, PDF, upload, metadata and failure ownership fencing outside the BFF.</summary>
public sealed class PurchaseOrderCreationService(
    IPurchaseOrderCreationGateway gateway,
    ILogger<PurchaseOrderCreationService> logger)
{
    /// <summary>Loads and validates browser-safe purchase-order selector options.</summary>
    public async Task<PurchaseOrderOptionsResult> GetOptionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var options = await gateway.GetOptionsAsync(cancellationToken);
            if (options.Suppliers is null || options.Employees is null || options.Addresses is null)
            {
                return new(PurchaseOrderCreationStatus.BadGateway);
            }
            return new(PurchaseOrderCreationStatus.Created, options);
        }
        catch (PurchaseOrderGatewayException exception)
        {
            return new(exception.Status, RetryAfter: Bounded(exception.RetryAfter));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(PurchaseOrderCreationStatus.Unavailable);
        }
        catch (HttpRequestException)
        {
            return new(PurchaseOrderCreationStatus.Unavailable);
        }
    }

    /// <summary>Creates the artifact workflow while fencing cleanup when replay ownership or a write outcome is unproven.</summary>
    /// <param name="request">Validated browser-safe purchase-order input.</param>
    /// <param name="attemptId">Stable replay identifier forwarded to idempotent downstream writes.</param>
    /// <param name="cancellationToken">Token to cancel the browser-owned operation.</param>
    /// <returns>The normalized creation result without service credentials.</returns>
    public async Task<PurchaseOrderCreationResult> CreateAsync(
        PurchaseOrderCreateRequest request,
        string attemptId,
        CancellationToken cancellationToken)
    {
        var rootWritePending = false;
        var acknowledgedWithoutOwnership = false;
        try
        {
            rootWritePending = true;
            var order = await gateway.CreateOrderAsync(request, attemptId, cancellationToken);
            if (order.Id <= 0)
            {
                throw new PurchaseOrderGatewayException(PurchaseOrderCreationStatus.BadGateway);
            }

            rootWritePending = false;
            acknowledgedWithoutOwnership = true;
            for (var itemIndex = 0; itemIndex < request.Items.Count; itemIndex++)
            {
                var itemId = await gateway.CreateItemAsync(order.Id, request.Items[itemIndex], attemptId, itemIndex, cancellationToken);
                if (itemId <= 0)
                {
                    throw new PurchaseOrderGatewayException(PurchaseOrderCreationStatus.BadGateway);
                }

            }

            var references = await gateway.GetDocumentReferencesAsync(request, cancellationToken);
            var document = BuildDocument(request, order, references);
            var pdf = await gateway.RenderPdfAsync(document, cancellationToken);
            if (pdf.Length < 4 || !pdf.AsSpan(0, 4).SequenceEqual("%PDF"u8))
            {
                throw new PurchaseOrderGatewayException(PurchaseOrderCreationStatus.BadGateway);
            }

            var storedFile = await gateway.UploadPdfAsync(order.Id, pdf, attemptId, cancellationToken);
            if (string.IsNullOrWhiteSpace(storedFile.Bucket) || string.IsNullOrWhiteSpace(storedFile.ObjectName))
            {
                throw new PurchaseOrderGatewayException(PurchaseOrderCreationStatus.BadGateway);
            }

            var fileId = await gateway.LinkFileAsync(order.Id, storedFile, attemptId, cancellationToken);
            if (fileId <= 0)
            {
                throw new PurchaseOrderGatewayException(PurchaseOrderCreationStatus.BadGateway);
            }

            return new(PurchaseOrderCreationStatus.Created, order.Id);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            // Stable keys and returned IDs do not prove that this invocation owns a replayed resource.
            // A failed write may also have committed without returning its acknowledgement.
            // Current producers expose no exclusive invocation-ownership proof: never delete on that basis.
            var definitiveRootRejection = !acknowledgedWithoutOwnership && exception is PurchaseOrderGatewayException rejection &&
                rejection.Status is PurchaseOrderCreationStatus.BadRequest or PurchaseOrderCreationStatus.Unauthorized or
                    PurchaseOrderCreationStatus.Forbidden or PurchaseOrderCreationStatus.Conflict or PurchaseOrderCreationStatus.RateLimited;
            if (acknowledgedWithoutOwnership || (rootWritePending && !definitiveRootRejection))
            {
                logger.LogWarning("Purchase-order cleanup fenced: acknowledged replay ownership or pending write outcome is unproven.");
                return new(PurchaseOrderCreationStatus.OutcomeUnknown);
            }

            return exception is PurchaseOrderGatewayException boundary
                ? new(boundary.Status, RetryAfter: Bounded(boundary.RetryAfter))
                : new(PurchaseOrderCreationStatus.Unavailable);
        }
    }

    private static PurchaseOrderPdfDocument BuildDocument(
        PurchaseOrderCreateRequest request,
        PurchaseOrderCreatedData order,
        PurchaseOrderDocumentReferences references)
    {
        var countries = references.Countries;
        var supplier = new PurchaseOrderPdfParty(
            Address(references.Supplier.Address, countries),
            references.Supplier.CompanyName,
            request.SupplierContactPerson ?? references.Supplier.ContactName,
            references.Supplier.Fax,
            references.Supplier.Mobile,
            references.Supplier.Telephone);
        var shipping = new PurchaseOrderPdfParty(
            Address(references.ShippingAddress, countries),
            request.ShippingCompanyName,
            request.ShippingContactPerson,
            request.ShippingFax,
            request.ShippingMobile,
            request.ShippingTelephone);
        var billing = new PurchaseOrderPdfParty(
            Address(references.BillingAddress, countries),
            request.BillingCompanyName,
            request.BillingContactPerson,
            request.BillingFax,
            request.BillingMobile,
            request.BillingTelephone);
        var items = request.Items.Select(item => new PurchaseOrderPdfLine(
            "THB",
            item.Description,
            item.PartNumber,
            item.Quantity,
            item.Quantity * item.UnitPrice,
            item.UnitPrice)).ToArray();
        return new(
            billing,
            order.CreatedDate,
            request.Fob,
            request.Notes,
            references.EmployeeFullName,
            items,
            order.Id,
            request.ShippingMethod,
            shipping,
            supplier,
            request.Terms);
    }

    private static PurchaseOrderPdfAddress Address(
        PurchaseOrderPostalAddress value,
        IReadOnlyDictionary<int, string> countries) =>
        new(
            value.AddressLine1,
            value.AddressLine2,
            value.Building,
            value.City,
            countries.GetValueOrDefault(value.CountryId, string.Empty),
            value.PostalCode,
            value.State);

    private static bool IsRecoverable(Exception exception) =>
        exception is PurchaseOrderGatewayException or HttpRequestException or OperationCanceledException ||
        string.Equals(exception.GetType().FullName, "Polly.Timeout.TimeoutRejectedException", StringComparison.Ordinal);

    private static TimeSpan? Bounded(TimeSpan? retryAfter) =>
        retryAfter is { } value && value > TimeSpan.Zero && value <= TimeSpan.FromHours(1) ? value : null;
}
