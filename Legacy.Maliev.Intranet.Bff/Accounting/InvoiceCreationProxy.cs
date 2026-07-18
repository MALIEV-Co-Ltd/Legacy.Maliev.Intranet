using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Thin same-origin proxy for Accounting-owned quotation-to-invoice creation.</summary>
public sealed class InvoiceCreationProxy(HttpClient httpClient)
{
    /// <summary>Gets authoritative invoice defaults for a quotation.</summary>
    public Task<HttpResponseMessage> PreviewAsync(int quotationId, CancellationToken cancellationToken) =>
        SendAsync(new(HttpMethod.Get, $"/invoices/from-quotation/{quotationId}/preview"), cancellationToken);

    /// <summary>Forwards editable intent with its stable operation identity.</summary>
    public Task<HttpResponseMessage> CreateAsync(int quotationId, CreateInvoiceFromQuotationRequest input, Guid operationId, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/invoices/from-quotation/{quotationId}") { Content = JsonContent.Create(input) };
        request.Headers.Add("Idempotency-Key", operationId.ToString("D"));
        return SendAsync(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request) return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
