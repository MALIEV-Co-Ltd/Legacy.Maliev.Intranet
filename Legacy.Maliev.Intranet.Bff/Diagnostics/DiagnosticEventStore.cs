using System.Text.RegularExpressions;
using Legacy.Maliev.Intranet.Contracts;

namespace Legacy.Maliev.Intranet.Bff.Diagnostics;

/// <summary>Maintains a small in-memory feed of redacted BFF failures for legacy debugging.</summary>
public sealed partial class DiagnosticEventStore(TimeProvider timeProvider)
{
    private const int Capacity = 200;
    private readonly Lock sync = new();
    private readonly List<DiagnosticEventItem> events = [];
    private long nextId;

    /// <summary>Records a failed HTTP response without retaining exception text, request data, or identity data.</summary>
    public void RecordResponseFailure(int statusCode, string? path, string? correlationId) => Record(
        "Error",
        $"HTTP_{Math.Clamp(statusCode, 500, 599)}",
        path,
        correlationId);

    /// <summary>Records an unhandled request failure without retaining exception or request content.</summary>
    public void RecordUnhandledFailure(string? path, string? correlationId) => Record(
        "Critical",
        "UNHANDLED_REQUEST_FAILURE",
        path,
        correlationId);

    private void Record(string level, string code, string? path, string? correlationId)
    {
        var item = new DiagnosticEventItem(
            Interlocked.Increment(ref nextId),
            level,
            code,
            "Legacy.Maliev.Intranet.Bff.Request",
            RedactPath(path),
            NormalizeOpaqueToken(correlationId),
            timeProvider.GetUtcNow());

        lock (sync)
        {
            events.Add(item);
            if (events.Count > Capacity)
            {
                events.RemoveRange(0, events.Count - Capacity);
            }
        }
    }

    /// <summary>Returns a bounded, filtered snapshot of the redacted feed.</summary>
    public DiagnosticEventPage Query(DiagnosticEventSort sort, string? search, int index, int size)
    {
        index = Math.Max(1, index);
        size = Math.Clamp(size, 1, 100);
        DiagnosticEventItem[] snapshot;
        lock (sync)
        {
            snapshot = [.. events];
        }

        IEnumerable<DiagnosticEventItem> query = snapshot;
        var normalizedSearch = search?.Trim();
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            normalizedSearch = normalizedSearch[..Math.Min(normalizedSearch.Length, 100)];
            query = query.Where(item =>
                item.Code.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                item.Path.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase) ||
                item.CorrelationId.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase));
        }

        query = sort switch
        {
            DiagnosticEventSort.LogId_Ascending => query.OrderBy(item => item.Id),
            DiagnosticEventSort.LogTimestamp_Ascending => query.OrderBy(item => item.Timestamp),
            DiagnosticEventSort.LogTimestamp_Descending => query.OrderByDescending(item => item.Timestamp),
            DiagnosticEventSort.LogLevel_Ascending => query.OrderBy(item => item.Level).ThenByDescending(item => item.Timestamp),
            DiagnosticEventSort.LogLevel_Descending => query.OrderByDescending(item => item.Level).ThenByDescending(item => item.Timestamp),
            _ => query.OrderByDescending(item => item.Id),
        };

        var totalRecords = query.Count();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalRecords / (double)size));
        var items = query.Skip((index - 1) * size).Take(size).ToArray();
        return new DiagnosticEventPage(items, index, totalPages, totalRecords, index < totalPages, index > 1);
    }

    private static string RedactPath(string? path)
    {
        var withoutQuery = (path ?? "/").Split('?', '#')[0];
        var redacted = SensitivePathSegment().Replace(withoutQuery, "/{id}");
        redacted = SensitiveTextSegment().Replace(redacted, "/{redacted}");
        return redacted[..Math.Min(redacted.Length, 256)];
    }

    private static string NormalizeOpaqueToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || SafeTokenCharacter().IsMatch(value))
        {
            return "unavailable";
        }

        return value;
    }

    [GeneratedRegex(@"/(?:\d+|[0-9a-fA-F]{8}-[0-9a-fA-F-]{27,36})(?=/|$)", RegexOptions.CultureInvariant)]
    private static partial Regex SensitivePathSegment();

    [GeneratedRegex(@"/(?:[^/?#]*[@=][^/?#]*|(?:token|secret|password|email|username)(?=/|$))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveTextSegment();

    [GeneratedRegex(@"[^A-Za-z0-9._:-]", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTokenCharacter();
}
