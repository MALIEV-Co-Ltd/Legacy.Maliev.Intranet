using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Address;

/// <summary>
/// Builds the browser-only Google Maps configuration returned by the Intranet BFF.
/// </summary>
public static class GoogleMapsEndpointMapper
{
    private const double DefaultLatitude = 13.7563;
    private const double DefaultLongitude = 100.5018;
    private const int DefaultZoom = 12;

    /// <summary>
    /// Reads only browser-safe Google Maps settings. The server-only embed key is deliberately
    /// ignored so a missing browser key fails closed instead of leaking a different credential.
    /// </summary>
    public static GoogleAddressConfigResponse GetBrowserConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("GoogleMaps");
        var regionCodes = section.GetSection("IncludedRegionCodes")
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new GoogleAddressConfigResponse
        {
            ApiKey = section["BrowserApiKey"]?.Trim() ?? string.Empty,
            MapId = NullIfWhiteSpace(section["MapId"]),
            DefaultLatitude = ReadDouble(section["DefaultLatitude"], DefaultLatitude),
            DefaultLongitude = ReadDouble(section["DefaultLongitude"], DefaultLongitude),
            DefaultZoom = ReadInt(section["DefaultZoom"], DefaultZoom),
            IncludedRegionCodes = regionCodes.Length == 0 ? ["th"] : regionCodes,
        };
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static double ReadDouble(string? value, double fallback) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            && double.IsFinite(parsed)
            ? parsed
            : fallback;

    private static int ReadInt(string? value, int fallback) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            && parsed is >= 1 and <= 21
            ? parsed
            : fallback;
}
