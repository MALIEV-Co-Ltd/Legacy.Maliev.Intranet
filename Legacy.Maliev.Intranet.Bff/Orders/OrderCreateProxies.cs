using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Orders;

/// <summary>Forwards idempotent create and rollback operations to OrderService.</summary>
public sealed class OrderCreateProxy(HttpClient httpClient)
{
    /// <summary>Creates an order with server-owned defaults.</summary>
    public Task<HttpResponseMessage> CreateAsync(OrderCreateRequest input, string idempotencyKey, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/Orders")
        {
            Content = JsonContent.Create(new
            {
                input.CustomerId,
                EmployeeId = (int?)null,
                input.Name,
                input.Description,
                input.ProcessId,
                input.MaterialId,
                input.SurfaceFinishId,
                input.ColorId,
                input.Quantity,
                Manufactured = 0,
                UnitPrice = (decimal?)null,
                DiscountPercent = (decimal?)null,
                CurrencyId = (int?)null,
                LeadTime = (int?)null,
                PromisedDate = (DateTime?)null,
                FinishedDate = (DateTime?)null,
                Comment = (string?)null,
                input.AllowSocialMedia,
                AllowCancellation = true,
                AllowPayment = false,
                TrackingNumber = (string?)null,
            }),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return SendAsync(request, cancellationToken);
    }

    /// <summary>Deletes a partially created order during compensation.</summary>
    public Task<HttpResponseMessage> DeleteAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/Orders/{id}"), cancellationToken);

    /// <summary>Creates the initial New status with the same workflow identity.</summary>
    public async Task<HttpResponseMessage> CreateInitialStatusAsync(int id, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var statusResponse = await SendAsync(new HttpRequestMessage(HttpMethod.Get, "/OrderStatuses/New"), cancellationToken);
        statusResponse.EnsureSuccessStatusCode();
        var status = await statusResponse.Content.ReadFromJsonAsync<OrderStatusItem>(cancellationToken)
            ?? throw new InvalidDataException("OrderService returned an empty New status.");
        if (await HasStatusAsync(id, status.Id, cancellationToken))
        {
            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/orderstatuses/Histories/{id}/{status.Id}");
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        try
        {
            var response = await SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode || !await HasStatusAsync(id, status.Id, cancellationToken)) return response;
            response.Dispose();
            return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
        }
        catch (Exception exception) when (exception is HttpRequestException or Polly.Timeout.TimeoutRejectedException)
        {
            try
            {
                if (await HasStatusAsync(id, status.Id, cancellationToken))
                {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
                }
            }
            catch (Exception reconciliation) when (reconciliation is HttpRequestException or Polly.Timeout.TimeoutRejectedException)
            {
                throw new Legacy.Maliev.Intranet.Server.Orders.OrderCreationOutcomeUnknownException(
                    "The initial order-status outcome could not be reconciled.",
                    reconciliation);
            }

            throw new Legacy.Maliev.Intranet.Server.Orders.OrderCreationOutcomeUnknownException(
                "The initial order-status outcome is not yet visible.",
                exception);
        }
    }

    private async Task<bool> HasStatusAsync(int id, int statusId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/orderstatuses/Histories/{id}"), cancellationToken);
        response.EnsureSuccessStatusCode();
        var history = await response.Content.ReadFromJsonAsync<List<OrderStatusHistoryItem>>(cancellationToken) ?? [];
        return history.Any(item => item.OrderStatusId == statusId);
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
}

/// <summary>Sends an optional order-created confirmation through NotificationService.</summary>
public sealed class OrderNotificationProxy(HttpClient httpClient)
{
    /// <summary>Sends provider-independent notification content after the creation saga commits.</summary>
    public Task<HttpResponseMessage> SendCreatedAsync(string email, int orderId, CancellationToken cancellationToken) =>
        httpClient.PostAsJsonAsync(
            "/notifications/v1/email/NoReply",
            new
            {
                To = email,
                Subject = $"Manufacturing Order #{orderId}",
                Body = "<p>Hello,</p><p>Your order has been created successfully. We will review it and provide a detailed quotation as soon as possible.</p><p>This message was automatically generated. Please do not reply.</p>",
                ReplyTo = (string?)null,
                Cc = (IReadOnlyList<string>?)null,
                Bcc = new[] { "mail-tracking@maliev.com" },
            },
            cancellationToken);
}
