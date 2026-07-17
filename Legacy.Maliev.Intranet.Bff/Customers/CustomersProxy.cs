using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

/// <summary>Forwards customer list requests without exposing the service credential to the browser.</summary>
public sealed class CustomersProxy(HttpClient httpClient)
{
    /// <summary>Gets the requested customer page from CustomerService.</summary>
    public Task<HttpResponseMessage> GetAsync(
        CustomerListSort sort,
        string? search,
        int index,
        int size,
        CancellationToken cancellationToken)
    {
        var path = $"/customers?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}";
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        return httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
