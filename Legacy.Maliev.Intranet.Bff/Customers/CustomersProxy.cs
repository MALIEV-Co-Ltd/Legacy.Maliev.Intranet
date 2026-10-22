using Maliev.Aspire.ServiceDefaults.Diagnostics;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Customers;

/// <summary>Forwards customer list requests without exposing the service credential to the browser.</summary>
public sealed class CustomersProxy(HttpClient httpClient)
{
    /// <summary>Gets one complete customer profile from CustomerService.</summary>
    public async Task<HttpResponseMessage> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/customers/{id}");
        return await httpClient.SendWithPrivateFailureObservationAsync(request, cancellationToken, new PrivateDependencyFailureObservation(), HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>Gets an uncached profile and revision for an opt-in editor.</summary>
    public async Task<HttpResponseMessage> GetVersionedByIdAsync(int id, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/customers/{id}/versioned");
        return await httpClient.SendWithPrivateFailureObservationAsync(request, cancellationToken, new PrivateDependencyFailureObservation(), HttpCompletionOption.ResponseHeadersRead);
    }

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
        return httpClient.SendWithPrivateFailureObservationAsync(request, cancellationToken, new PrivateDependencyFailureObservation(), HttpCompletionOption.ResponseHeadersRead);
    }
}
