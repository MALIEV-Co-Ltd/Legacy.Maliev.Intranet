using System.Globalization;
using System.Net.Http.Headers;

namespace Legacy.Maliev.Intranet.Bff.Operations;

/// <summary>Forwards aggregate-only outcome reads with the current employee session token.</summary>
public sealed class AggregateOutcomeProxy(IHttpClientFactory clients)
{
    /// <summary>The named client for AccountingService aggregate reads.</summary>
    public const string AccountingClient = "aggregate-outcome-accounting";

    /// <summary>The named client for QuotationService aggregate reads.</summary>
    public const string QuotationClient = "aggregate-outcome-quotation";

    /// <summary>Gets one bounded aggregate response from an allowlisted producer route.</summary>
    public async Task<HttpResponseMessage> GetAsync(
        string source,
        DateTime fromUtc,
        DateTime toUtc,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var clientName = source == "quotation" ? QuotationClient : AccountingClient;
        var path = source == "quotation" ? "/quotations/outcomes/readback" : "/invoices/outcomes/readback";
        path += "?fromUtc=" + Uri.EscapeDataString(fromUtc.ToString("O", CultureInfo.InvariantCulture))
            + "&toUtc=" + Uri.EscapeDataString(toUtc.ToString("O", CultureInfo.InvariantCulture));
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await clients.CreateClient(clientName).SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }
}
