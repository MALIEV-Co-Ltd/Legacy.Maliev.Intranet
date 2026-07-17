using System.Globalization;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Orders;

/// <summary>Forwards order-detail reads and writes while the service credential remains server-side.</summary>
public sealed class OrderDetailProxy(HttpClient httpClient)
{
    /// <summary>Gets one complete order.</summary>
    public Task<HttpResponseMessage> GetAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/Orders/{id}"), cancellationToken);

    /// <summary>Updates one complete order with optimistic concurrency.</summary>
    public Task<HttpResponseMessage> UpdateAsync(
        int id,
        int? customerId,
        OrderUpdateRequest input,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/Orders/{id}")
        {
            Content = JsonContent.Create(new
            {
                CustomerId = customerId,
                input.EmployeeId,
                input.Name,
                input.Description,
                input.ProcessId,
                input.MaterialId,
                input.SurfaceFinishId,
                input.ColorId,
                input.Quantity,
                input.Manufactured,
                input.UnitPrice,
                input.DiscountPercent,
                input.CurrencyId,
                input.LeadTime,
                input.PromisedDate,
                input.FinishedDate,
                input.Comment,
                input.AllowSocialMedia,
                input.AllowCancellation,
                input.AllowPayment,
                input.TrackingNumber,
            }),
        };
        if (input.ModifiedDate is not null)
        {
            request.Headers.Add(
                "X-Expected-Modified-Date",
                input.ModifiedDate.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }

        return SendAsync(request, cancellationToken);
    }

    /// <summary>Gets the latest order status.</summary>
    public Task<HttpResponseMessage> GetLatestStatusAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/orderstatuses/Histories/{id}/latest"), cancellationToken);

    /// <summary>Gets order status history.</summary>
    public Task<HttpResponseMessage> GetStatusHistoryAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/orderstatuses/Histories/{id}"), cancellationToken);

    /// <summary>Gets permitted transitions from the current status.</summary>
    public Task<HttpResponseMessage> GetAvailableStatusesAsync(int statusId, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/orderstatuses/{statusId}/available"), cancellationToken);

    /// <summary>Transitions an order status idempotently.</summary>
    public Task<HttpResponseMessage> TransitionStatusAsync(
        int id,
        int statusId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/orderstatuses/Histories/{id}/{statusId}");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return SendAsync(request, cancellationToken);
    }

    /// <summary>Gets order-owned file metadata.</summary>
    public Task<HttpResponseMessage> GetFilesAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/orders/{id}/files"), cancellationToken);

    /// <summary>Links a clean FileService object to the order.</summary>
    public Task<HttpResponseMessage> CreateFileAsync(
        int id,
        string bucket,
        string objectName,
        CancellationToken cancellationToken) =>
        SendAsync(
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/orders/{id}/files?bucket={Uri.EscapeDataString(bucket)}&objectName={Uri.EscapeDataString(objectName)}"),
            cancellationToken);

    /// <summary>Deletes order-file metadata by its server-resolved identifier.</summary>
    public Task<HttpResponseMessage> DeleteFileAsync(int fileId, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/orders/files/{fileId}"), cancellationToken);

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
}
