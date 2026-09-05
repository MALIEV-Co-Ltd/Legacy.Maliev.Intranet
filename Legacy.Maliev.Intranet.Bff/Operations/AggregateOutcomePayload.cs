using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Legacy.Maliev.Intranet.Bff.Operations;

/// <summary>Validates the strict aggregate-only producer wire contracts.</summary>
internal static partial class AggregateOutcomePayload
{
    private static readonly string[] QuotationAvailabilityKeys =
    [
        "TechnicalConversionAvailability",
        "QualifiedCustomerAvailability",
        "RevenueAvailability",
    ];

    /// <summary>Parses an explicit UTC timestamp without local-time inference.</summary>
    internal static bool TryUtc(string? value, out DateTime utc)
    {
        utc = default;
        return value is not null
            && ExplicitUtcPattern().IsMatch(value)
            && DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out utc);
    }

    /// <summary>Returns a detached aggregate object only when every field is allowlisted and consistent.</summary>
    internal static bool TryValidate(
        ReadOnlyMemory<byte> body,
        string source,
        DateTime fromUtc,
        DateTime toUtc,
        out JsonElement payload)
    {
        payload = default;
        try
        {
            if (body.Length > OutcomeReadbackEndpointMapper.MaximumPayloadBytes ||
                source is not ("quotation" or "invoice"))
            {
                return false;
            }

            using var document = JsonDocument.Parse(body, new JsonDocumentOptions
            {
                AllowDuplicateProperties = false,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16,
            });
            var root = document.RootElement;
            var quotation = source == "quotation";
            var rootKeys = quotation
                ? new[] { "FromUtc", "ToUtc", "Days" }.Concat(QuotationAvailabilityKeys).ToArray()
                : ["FromUtc", "ToUtc", "Days"];
            if (!HasExactKeys(root, rootKeys) ||
                !TryUtc(GetString(root, "FromUtc"), out var actualFrom) || actualFrom != fromUtc ||
                !TryUtc(GetString(root, "ToUtc"), out var actualTo) || actualTo != toUtc ||
                !root.TryGetProperty("Days", out var days) || days.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            if (quotation && QuotationAvailabilityKeys.Any(key => GetString(root, key) != "unavailable"))
            {
                return false;
            }

            DateTime? previousDay = null;
            foreach (var day in days.EnumerateArray())
            {
                if (!ValidateDay(day, quotation, fromUtc, toUtc, ref previousDay))
                {
                    return false;
                }
            }

            payload = root.Clone();
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or
            FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    private static bool ValidateDay(
        JsonElement day,
        bool quotation,
        DateTime fromUtc,
        DateTime toUtc,
        ref DateTime? previousDay)
    {
        var stages = quotation ? new[] { "PersistedQuotation", "AcceptedQuotation" } : ["PaidInvoice"];
        var amountKey = quotation ? "AcceptedQuotedAmountsByCurrency" : "PaidInvoiceAmountsByCurrency";
        var expectedKeys = new[] { "DayUtc", amountKey }
            .Concat(stages.SelectMany(stage => new[]
            {
                stage + "Count",
                "SourceAttributed" + stage + "Count",
                "Unattributed" + stage + "Count",
            }))
            .ToArray();
        var label = GetString(day, "DayUtc");
        if (!HasExactKeys(day, expectedKeys) || label is null || !DayPattern().IsMatch(label) ||
            !TryUtc(label.TrimEnd('Z') + "Z", out var date) ||
            date >= toUtc || date.AddDays(1) <= fromUtc ||
            previousDay.HasValue && previousDay.Value >= date)
        {
            return false;
        }

        previousDay = date;
        foreach (var stage in stages)
        {
            if (!TryCount(day, stage + "Count", out var total) ||
                !TryCount(day, "SourceAttributed" + stage + "Count", out var attributed) ||
                !TryCount(day, "Unattributed" + stage + "Count", out var unattributed) ||
                total != attributed + unattributed)
            {
                return false;
            }
        }

        return day.TryGetProperty(amountKey, out var amounts) &&
            amounts.ValueKind == JsonValueKind.Array &&
            ValidateAmounts(day, amounts, quotation);
    }

    private static bool ValidateAmounts(JsonElement day, JsonElement amounts, bool quotation)
    {
        string? previousCode = null;
        int? previousId = null;
        long represented = 0;
        foreach (var amount in amounts.EnumerateArray())
        {
            var expectedKeys = quotation
                ? new[] { "CurrencyId", "QuotedAmount", "AcceptedQuotationCount" }
                : ["Currency", "PaidInvoiceTotal", "PaidInvoiceCount"];
            if (!HasExactKeys(amount, expectedKeys))
            {
                return false;
            }

            if (quotation)
            {
                if (!TryCount(amount, "CurrencyId", out var currencyId) ||
                    previousId.HasValue && previousId.Value >= currencyId)
                {
                    return false;
                }

                previousId = currencyId;
            }
            else
            {
                var code = GetString(amount, "Currency");
                if (code is null || !CurrencyPattern().IsMatch(code) ||
                    previousCode is not null && string.CompareOrdinal(previousCode, code) >= 0)
                {
                    return false;
                }

                previousCode = code;
            }

            var valueKey = quotation ? "QuotedAmount" : "PaidInvoiceTotal";
            var countKey = quotation ? "AcceptedQuotationCount" : "PaidInvoiceCount";
            if (!amount.TryGetProperty(valueKey, out var value) ||
                value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out _) ||
                !TryCount(amount, countKey, out var count))
            {
                return false;
            }

            represented += count;
        }

        var totalKey = quotation ? "AcceptedQuotationCount" : "PaidInvoiceCount";
        return TryCount(day, totalKey, out var total) &&
            (quotation ? represented <= total : represented == total);
    }

    private static bool HasExactKeys(JsonElement value, IReadOnlyCollection<string> expected)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        return names.Length == expected.Count &&
            names.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal));
    }

    private static string? GetString(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool TryCount(JsonElement value, string name, out int count)
    {
        count = default;
        return value.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out count) && count >= 0;
    }

    [GeneratedRegex("^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}(?:\\.\\d{1,7})?Z$", RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitUtcPattern();

    [GeneratedRegex("^\\d{4}-\\d{2}-\\d{2}T00:00:00Z?$", RegexOptions.CultureInvariant)]
    private static partial Regex DayPattern();

    [GeneratedRegex("^(?:[A-Z]{3}|UNSPECIFIED)$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();
}
