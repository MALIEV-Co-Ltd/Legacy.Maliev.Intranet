using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Orders;

/// <summary>QuestPDF order-label payload.</summary>
public sealed record OrderLabelRequest(
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

/// <summary>DocumentService order-label boundary.</summary>
public interface IOrderDocumentClient
{
    /// <summary>Renders the preserved four-by-three QuestPDF order label.</summary>
    Task<byte[]> RenderOrderLabelAsync(OrderLabelRequest request, string token, CancellationToken cancellationToken);
}

/// <summary>Renders order labels through the extracted QuestPDF DocumentService.</summary>
public sealed class OrderDocumentClient(HttpClient httpClient) : IOrderDocumentClient
{
    /// <inheritdoc />
    public async Task<byte[]> RenderOrderLabelAsync(OrderLabelRequest payload, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Pdfs/orderlabel") { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DocumentService did not return a PDF order label.");
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}

/// <summary>NotificationService order event boundary.</summary>
public interface ILegacyOrderNotificationClient
{
    /// <summary>Sends the optional customer order-created message.</summary>
    Task SendCreatedAsync(string email, int orderId, string token, CancellationToken cancellationToken);
}

/// <summary>Sends order messages through the provider-independent NotificationService API.</summary>
public sealed class LegacyOrderNotificationClient(HttpClient httpClient) : ILegacyOrderNotificationClient
{
    /// <inheritdoc />
    public async Task SendCreatedAsync(string email, int orderId, string token, CancellationToken cancellationToken)
    {
        var payload = new SendEmailNotificationRequest(
            email,
            $"Manufacturing Order #{orderId}",
            "<p>Hello,</p><p>Your order has been created successfully. We will review it and provide a detailed quotation as soon as possible.</p><p>This message was automatically generated. Please do not reply.</p>",
            null,
            null,
            ["mail-tracking@maliev.com"]);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/notifications/v1/email/NoReply") { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record SendEmailNotificationRequest(
        string To,
        string Subject,
        string Body,
        string? ReplyTo,
        IReadOnlyList<string>? Cc,
        IReadOnlyList<string>? Bcc);
}
