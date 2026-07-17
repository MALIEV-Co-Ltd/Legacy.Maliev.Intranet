using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Bff.Orders;

/// <summary>Renders the preserved QuestPDF order label through DocumentService.</summary>
public sealed class OrderDocumentProxy(HttpClient httpClient)
{
    /// <summary>Renders an order label and validates the returned media type.</summary>
    public async Task<byte[]?> RenderAsync(OrderLabelPayload payload, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync("/Pdfs/orderlabel", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}

/// <summary>DocumentService order-label payload.</summary>
public sealed record OrderLabelPayload(
    string Id,
    string Name,
    int OrderQuantity,
    int ManufactureQuantity,
    int RemainingQuantity,
    string Process,
    string Material,
    string Color,
    string SurfaceFinish,
    string Description);
