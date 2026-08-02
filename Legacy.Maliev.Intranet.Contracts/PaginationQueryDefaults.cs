namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>
/// Normalizes pagination values supplied by a browser query string.
/// Blazor assigns zero to an omitted non-nullable integer query parameter, so
/// callers must apply the page's intended fallback before clamping the value.
/// </summary>
public static class PaginationQueryDefaults
{
    public static int NormalizeIndex(int value) => value > 0 ? value : 1;

    public static int NormalizeSize(int value, int fallback, int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fallback, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximum, fallback);

        return value <= 0 ? fallback : Math.Clamp(value, 1, maximum);
    }
}
