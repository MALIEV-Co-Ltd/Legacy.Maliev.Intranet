using System.Globalization;
using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Forwards allowlisted Invoice View operations using server-held service credentials.</summary>
public sealed class InvoiceDetailProxy(HttpClient httpClient)
{
    /// <summary>Gets one invoice.</summary>
    public Task<HttpResponseMessage> GetAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new(HttpMethod.Get, $"/invoices/{id}"), cancellationToken);

    /// <summary>Gets owned invoice line items.</summary>
    public Task<HttpResponseMessage> GetOrderItemsAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new(HttpMethod.Get, $"/invoices/{id}/orderitems"), cancellationToken);

    /// <summary>Gets owned invoice-file metadata.</summary>
    public Task<HttpResponseMessage> GetInvoiceFilesAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new(HttpMethod.Get, $"/invoices/{id}/files"), cancellationToken);

    /// <summary>Gets owned receipt-file metadata.</summary>
    public Task<HttpResponseMessage> GetReceiptFilesAsync(int receiptId, CancellationToken cancellationToken) =>
        SendAsync(new(HttpMethod.Get, $"/receipts/{receiptId}/files"), cancellationToken);

    /// <summary>Updates only legacy-editable fields while preserving the full invoice.</summary>
    public Task<HttpResponseMessage> UpdateAsync(int id, InvoiceDetail current, InvoiceUpdateRequest input, CancellationToken cancellationToken)
    {
        var body = current with
        {
            IsPaid = input.IsPaid,
            PaymentDate = input.PaymentDate,
            InternalComment = input.InternalComment,
            ModifiedDate = input.ModifiedDate,
        };
        var request = new HttpRequestMessage(HttpMethod.Put, $"/invoices/{id}") { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Unmodified-Since", input.ModifiedDate.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return SendAsync(request, cancellationToken);
    }

    /// <summary>Deletes one invoice.</summary>
    public Task<HttpResponseMessage> DeleteInvoiceAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new(HttpMethod.Delete, $"/invoices/{id}"), cancellationToken);

    /// <summary>Deletes one owned invoice line item.</summary>
    public Task<HttpResponseMessage> DeleteOrderItemAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new(HttpMethod.Delete, $"/invoices/orderitems/{id}"), cancellationToken);

    /// <summary>Deletes one owned invoice-file metadata record.</summary>
    public Task<HttpResponseMessage> DeleteInvoiceFileAsync(int id, CancellationToken cancellationToken) =>
        SendAsync(new(HttpMethod.Delete, $"/invoices/files/{id}"), cancellationToken);

    /// <summary>Creates or reconciles one receipt using Accounting-owned authority lookups.</summary>
    public Task<HttpResponseMessage> CreateReceiptAsync(
        int id,
        CreateInvoiceReceiptRequest input,
        int employeeId,
        Guid operationId,
        CancellationToken cancellationToken) =>
        SendReceiptAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/invoices/{id}/receipt") { Content = JsonContent.Create(input) },
            employeeId,
            operationId,
            cancellationToken);

    /// <summary>Removes one receipt idempotently.</summary>
    public Task<HttpResponseMessage> RemoveReceiptAsync(int id, Guid operationId, CancellationToken cancellationToken) =>
        SendReceiptAsync(new(HttpMethod.Delete, $"/invoices/{id}/receipt"), null, operationId, cancellationToken);

    /// <summary>Explicitly emails one existing receipt.</summary>
    public Task<HttpResponseMessage> EmailReceiptAsync(
        int id,
        int employeeId,
        Guid operationId,
        CancellationToken cancellationToken) =>
        SendReceiptAsync(new(HttpMethod.Post, $"/invoices/{id}/receipt/email"), employeeId, operationId, cancellationToken);

    private Task<HttpResponseMessage> SendReceiptAsync(
        HttpRequestMessage request,
        int? employeeId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        request.Headers.Add("Idempotency-Key", operationId.ToString("D"));
        if (employeeId is not null)
        {
            request.Headers.Add("X-Legacy-Employee-Id", employeeId.Value.ToString(CultureInfo.InvariantCulture));
        }

        return SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
    }
}
