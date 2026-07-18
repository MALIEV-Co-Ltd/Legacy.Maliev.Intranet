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
            WithholdingTax = input.WithholdingTax,
            Outstanding = input.Outstanding,
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

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
    }
}
