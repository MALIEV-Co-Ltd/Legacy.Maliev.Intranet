using System.Text.Json.Serialization;

namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>
/// Browser-safe Google Maps configuration for authenticated employee address entry.
/// </summary>
/// <remarks>
/// Only the domain-restricted browser key is exposed. Service credentials, signing keys,
/// and server-only Google Maps credentials must never be copied into this contract.
/// </remarks>
public sealed class GoogleAddressConfigResponse
{
    /// <summary>Domain-restricted Google Maps browser API key, or an empty value when unavailable.</summary>
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Optional Google Maps map ID used for advanced markers.</summary>
    [JsonPropertyName("mapId")]
    public string? MapId { get; init; }

    /// <summary>Default map latitude.</summary>
    [JsonPropertyName("defaultLatitude")]
    public double DefaultLatitude { get; init; } = 13.7563;

    /// <summary>Default map longitude.</summary>
    [JsonPropertyName("defaultLongitude")]
    public double DefaultLongitude { get; init; } = 100.5018;

    /// <summary>Default zoom level for the manual pin map.</summary>
    [JsonPropertyName("defaultZoom")]
    public int DefaultZoom { get; init; } = 12;

    /// <summary>Region codes allowed in Google Places suggestions.</summary>
    [JsonPropertyName("includedRegionCodes")]
    public string[] IncludedRegionCodes { get; init; } = ["th"];
}
