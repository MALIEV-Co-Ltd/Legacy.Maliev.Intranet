using System.Net.Http.Json;
using System.Text.Json;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Staged switch for employee-bound invoice creation; disabled until both grants and rollout are approved.</summary>
public sealed class InvoiceDelegationOptions
{
    /// <summary>Requires an AuthService delegation for every invoice-create request when enabled.</summary>
    public bool Enabled { get; set; }
}

/// <summary>Exchanges only a server-held employee access token for an Accounting-specific delegation.</summary>
public interface IInvoiceDelegationClient
{
    /// <summary>Returns a bounded delegation JWT, or null when AuthService cannot issue one.</summary>
    Task<string?> IssueAsync(string employeeAccessToken, int quotationId, Guid operationId, CancellationToken cancellationToken);
}

/// <summary>Calls AuthService with the Intranet service bearer supplied by the HTTP authentication handler.</summary>
public sealed class InvoiceDelegationClient(HttpClient httpClient) : IInvoiceDelegationClient
{
    private const int MaximumResponseBytes = 20 * 1024;

    /// <inheritdoc />
    public async Task<string?> IssueAsync(string employeeAccessToken, int quotationId, Guid operationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(employeeAccessToken) || employeeAccessToken.Length > 16384 ||
            quotationId <= 0 || operationId == Guid.Empty) return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/v1/exchange/invoice-create")
        {
            Content = JsonContent.Create(new InvoiceDelegationRequest(employeeAccessToken, quotationId, operationId.ToString("D"))),
        };
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > MaximumResponseBytes)
            return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[2048];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (output.Length + read > MaximumResponseBytes) return null;
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        try
        {
            var token = JsonSerializer.Deserialize<InvoiceDelegationResponse>(output.ToArray(), JsonSerializerOptions.Web);
            return token is { TokenType: "Bearer", ExpiresIn: > 0 and <= 120 } &&
                IsCompactToken(token.AccessToken) ? token.AccessToken : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsCompactToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16384 || token.Count(value => value == '.') != 2)
            return false;
        return token.All(value => char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.');
    }

    private sealed record InvoiceDelegationRequest(string EmployeeAccessToken, int QuotationId, string OperationId);
    private sealed record InvoiceDelegationResponse(string AccessToken, string TokenType, int ExpiresIn);
}
