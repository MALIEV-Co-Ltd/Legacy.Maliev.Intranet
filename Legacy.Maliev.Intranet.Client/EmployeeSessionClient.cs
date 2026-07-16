using System.Net.Http.Json;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Client;

internal sealed class EmployeeSessionClient(
    HttpClient httpClient,
    ILogger<EmployeeSessionClient> logger)
{
    public async Task<EmployeeSessionSummary?> GetAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("/bff/session", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Employee session projection returned HTTP {StatusCode}.",
                    (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<EmployeeSessionSummary>(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(exception, "Employee session projection is unavailable.");
            return null;
        }
    }
}
