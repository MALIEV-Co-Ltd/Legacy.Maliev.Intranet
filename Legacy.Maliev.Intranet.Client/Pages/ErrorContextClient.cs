using System.Text.Json;

namespace Legacy.Maliev.Intranet.Client.Pages;

internal static class ErrorContextClient
{
    private const int MaximumResponseBytes = 512;

    internal static async Task<string?> GetPageReferenceAsync(HttpClient http)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/error-context");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[MaximumResponseBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length &&
               (read = await stream.ReadAsync(buffer.AsMemory(total))) > 0)
        {
            total += read;
        }

        if (total == buffer.Length)
        {
            return null;
        }

        using var document = JsonDocument.Parse(buffer.AsMemory(0, total));
        if (!document.RootElement.TryGetProperty("pageReference", out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var reference = value.GetString();
        return reference is { Length: > 0 and <= 128 } && reference.All(c => !char.IsControl(c))
            ? reference
            : null;
    }
}
