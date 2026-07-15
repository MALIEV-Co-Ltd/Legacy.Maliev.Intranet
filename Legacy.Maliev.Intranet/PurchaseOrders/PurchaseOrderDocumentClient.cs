using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Legacy.Maliev.Intranet.PurchaseOrders;

/// <summary>Renders migrated purchase-order documents through QuestPDF DocumentService.</summary>
public sealed class PurchaseOrderDocumentClient(HttpClient httpClient) : IPurchaseOrderDocumentClient
{
    /// <inheritdoc />
    public async Task<byte[]> RenderAsync(PurchaseOrderDocument request, string token, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/Pdfs/purchaseorder") { Content = JsonContent.Create(request) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType is not "application/pdf") throw new InvalidOperationException("DocumentService did not return a PDF.");
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}

/// <summary>QuestPDF purchase-order boundary.</summary>
public interface IPurchaseOrderDocumentClient
{
    /// <summary>Renders one purchase order to PDF.</summary>
    Task<byte[]> RenderAsync(PurchaseOrderDocument request, string token, CancellationToken cancellationToken);
}

/// <summary>DocumentService purchase-order contract.</summary>
public sealed record PurchaseOrderDocument(CompanyInformation Billing, DateTime Date, [property: JsonPropertyName("FOB")] string? Fob, string? Notes, string OrderedBy, IReadOnlyList<PurchaseOrderDocumentItem> OrderItems, int ReferenceNumber, string? ShippedVia, CompanyInformation Shipping, CompanyInformation Supplier, string? Terms);
/// <summary>Company/address block in the document contract.</summary>
public sealed record CompanyInformation(PurchaseOrderDocumentAddress Address, string CompanyName, string? ContactName, string? Fax, string? Mobile, string? Telephone);
/// <summary>Postal address in the document contract.</summary>
public sealed record PurchaseOrderDocumentAddress(string? AddressLine1, string? AddressLine2, string? Building, string? City, string? Country, string? PostalCode, string? State);
/// <summary>Line in the document contract.</summary>
public sealed record PurchaseOrderDocumentItem(string Currency, string? Description, string? PartNumber, int Quantity, decimal Subtotal, decimal UnitPrice);