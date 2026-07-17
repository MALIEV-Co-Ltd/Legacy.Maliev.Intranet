using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Employees;

/// <summary>Forwards employee list requests without exposing service credentials to the browser.</summary>
public sealed class EmployeesProxy(HttpClient httpClient)
{
    /// <summary>Gets the requested employee page from EmployeeService.</summary>
    public async Task<HttpResponseMessage> GetAsync(
        EmployeeListSort sort,
        string? search,
        int index,
        int size,
        CancellationToken cancellationToken)
    {
        var path = $"/employees?sort={sort}&search={Uri.EscapeDataString(search ?? string.Empty)}&index={index}&size={size}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
