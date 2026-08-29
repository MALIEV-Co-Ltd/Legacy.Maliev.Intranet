using System.Globalization;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Bff.Quotations;

/// <summary>Forwards employee quotation decisions using only the BFF's machine identity.</summary>
public sealed class QuotationDecisionProxy(HttpClient httpClient)
{
    /// <summary>Sends one optimistic-concurrency protected decision without automatic write retries.</summary>
    public async Task<HttpResponseMessage> DecideAsync(
        int id,
        bool accepted,
        DateTime? expectedModifiedDate,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/quotations/{id}/decision")
        {
            Content = JsonContent.Create(new QuotationDecisionServiceRequest(accepted, EmployeeInitiated: true)),
        };
        if (expectedModifiedDate is { } expected)
        {
            request.Headers.TryAddWithoutValidation(
                "X-Expected-Modified-Date",
                expected.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        }

        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private sealed record QuotationDecisionServiceRequest(bool Accepted, bool EmployeeInitiated);
}
