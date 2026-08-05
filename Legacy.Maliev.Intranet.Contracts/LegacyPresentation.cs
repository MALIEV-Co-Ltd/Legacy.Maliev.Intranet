using System.Globalization;
using System.Net.Http.Json;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>
/// Presentation-only rules shared by the independently loaded employee feature assemblies.
/// Stored values and API contracts remain unchanged; conversion happens only when rendering.
/// </summary>
public static class LegacyPresentation
{
    private static readonly TimeZoneInfo BangkokTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");

    /// <summary>Maximum duration for an employee-facing read before a recoverable error is shown.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Creates the per-request timeout used by feature pages.</summary>
    public static CancellationTokenSource CreateRequestTimeout() => new(RequestTimeout);

    /// <summary>Executes a UI read with a deterministic terminal timeout.</summary>
    public static async Task<HttpResponseMessage> GetForPresentationAsync(
        this HttpClient client,
        string requestUri,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var timeoutSource = new CancellationTokenSource(timeout ?? RequestTimeout);
        return await client.GetAsync(requestUri, timeoutSource.Token).ConfigureAwait(false);
    }

    /// <summary>Executes a UI read with both caller cancellation and a deterministic terminal timeout.</summary>
    public static async Task<HttpResponseMessage> GetForPresentationAsync(
        this HttpClient client,
        string requestUri,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var timeoutSource = new CancellationTokenSource(timeout ?? RequestTimeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        return await client.GetAsync(requestUri, linkedSource.Token).ConfigureAwait(false);
    }

    /// <summary>Executes and deserializes a UI read within the same terminal timeout.</summary>
    public static async Task<T?> GetFromJsonForPresentationAsync<T>(
        this HttpClient client,
        string requestUri,
        TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var timeoutSource = new CancellationTokenSource(timeout ?? RequestTimeout);
        using var response = await client.GetAsync(requestUri, timeoutSource.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content
            .ReadFromJsonAsync<T>(cancellationToken: timeoutSource.Token)
            .ConfigureAwait(false);
    }

    /// <summary>Formats a stored UTC instant in Asia/Bangkok using the active UI culture.</summary>
    public static string FormatUtcDateTime(DateTime? value, CultureInfo culture, string fallback)
    {
        if (value is null)
        {
            return fallback;
        }

        var utc = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
        };

        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), BangkokTimeZone)
            .ToString("dd MMM yyyy, HH:mm", culture);
    }

    /// <summary>Formats a stored UTC instant as its Asia/Bangkok calendar date.</summary>
    public static string FormatUtcDate(DateTime? value, CultureInfo culture, string fallback)
    {
        if (value is null)
        {
            return fallback;
        }

        var utc = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
        };

        return TimeZoneInfo.ConvertTime(new DateTimeOffset(utc), BangkokTimeZone)
            .ToString("d", culture);
    }

    /// <summary>Formats an offset-aware instant in Asia/Bangkok using the active UI culture.</summary>
    public static string FormatUtcDateTime(DateTimeOffset value, CultureInfo culture) =>
        TimeZoneInfo.ConvertTime(value, BangkokTimeZone)
            .ToString("dd MMM yyyy, HH:mm", culture);

    /// <summary>Formats a date-only business value without applying a timezone shift.</summary>
    public static string FormatCalendarDate(DateTime? value, CultureInfo culture, string fallback) =>
        value?.ToString("d", culture) ?? fallback;
}
