using System.Text.Json;

namespace Legacy.Maliev.Intranet.Client.Features.Quotations.Models;

/// <summary>Validates persisted version-one finder context without server dependencies.</summary>
public static class ServiceFinderMetadataEnvelope
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedAnswers = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["files"] = ["files-3d", "files-2d", "files-image", "files-none", "files-real-part"],
        ["service"] = ["service-machining", "service-3d", "service-molding", "service-unsure"],
        ["material"] = ["material-metal", "material-standard-plastic", "material-resin", "material-plastic", "material-silicone", "material-unsure"],
        ["quantity"] = ["quantity-1-10", "quantity-11-100", "quantity-101-1000", "quantity-over-1000"],
        ["end-use"] = ["use-prototype", "use-industrial", "use-replacement", "use-consumer"],
        ["performance"] = ["performance-strength", "performance-appearance", "performance-flexibility", "performance-temperature", "performance-unsure"],
        ["environment"] = ["environment-indoor", "environment-outdoor", "environment-wet", "environment-heat-chemical", "environment-unsure"],
    };
    private static readonly string[] AllowedServices = ["custom", "cnc", "printing", "scanning", "design", "silicone", "injection"];

    /// <summary>Reads only supported, unambiguous stable-ID context; other values remain ordinary notes.</summary>
    public static bool TryRead(string? rawValue, out ServiceFinderMetadata? metadata)
    {
        metadata = null;
        if (string.IsNullOrWhiteSpace(rawValue)) return false;
        try
        {
            using var document = JsonDocument.Parse(rawValue);
            var root = document.RootElement;
            if (!HasUniqueProperties(root)
                || !root.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.String || source.GetString() != "service_finder"
                || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1
                || !root.TryGetProperty("answers", out var answers) || !HasUniqueProperties(answers)) return false;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in AllowedAnswers)
            {
                if (!answers.TryGetProperty(entry.Key, out var answer))
                {
                    if (entry.Key is "performance" or "environment") continue;
                    return false;
                }
                if (answer.ValueKind != JsonValueKind.String || !entry.Value.Contains(answer.GetString(), StringComparer.Ordinal)) return false;
                values.Add(entry.Key, answer.GetString()!);
            }
            if (!TryReadServices(root, "recommended_service_ids", out var recommendations)
                || !TryReadServices(root, "finder_path", out var path)
                || path.Any(id => !recommendations.Contains(id, StringComparer.Ordinal))) return false;
            // A malformed reserved note must remain visible as ordinary text, not be silently discarded.
            if (root.TryGetProperty("operator_comment", out var note) && note.ValueKind != JsonValueKind.String) return false;
            var comment = note.ValueKind == JsonValueKind.String ? note.GetString()! : string.Empty;
            metadata = new(values.AsReadOnly(), recommendations.AsReadOnly(), path.AsReadOnly(), comment);
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Changes only the operator note, retaining all validated context and extension properties.</summary>
    public static string? MergeOperatorComment(string? existingValue, string? operatorComment)
    {
        var normalized = operatorComment?.Trim();
        if (!TryRead(existingValue, out _)) return normalized;
        using var document = JsonDocument.Parse(existingValue!);
        var envelope = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name != "operator_comment") envelope.Add(property.Name, property.Value.Clone());
        }
        if (!string.IsNullOrEmpty(normalized)) envelope.Add("operator_comment", normalized);
        return JsonSerializer.Serialize(envelope);
    }

    private static bool HasUniqueProperties(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = new HashSet<string>(StringComparer.Ordinal);
        return value.EnumerateObject().All(property => names.Add(property.Name));
    }

    private static bool TryReadServices(JsonElement root, string key, out List<string> values)
    {
        values = [];
        if (!root.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > 4) return false;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return false;
            var id = item.GetString()!;
            if (!AllowedServices.Contains(id, StringComparer.Ordinal) || values.Contains(id, StringComparer.Ordinal)) return false;
            values.Add(id);
        }
        return true;
    }
}

/// <summary>Validated stable IDs for localized presentation and the editable staff note.</summary>
public sealed record ServiceFinderMetadata(IReadOnlyDictionary<string, string> Answers, IReadOnlyList<string> RecommendedServiceIds, IReadOnlyList<string> FinderPath, string OperatorComment);
