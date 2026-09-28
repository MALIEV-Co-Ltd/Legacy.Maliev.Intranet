namespace Legacy.Maliev.Intranet.Contracts;

/// <summary>Restricts untrusted error-route status input to HTTP failure codes.</summary>
public static class ErrorPageStatus
{
    /// <summary>Returns a displayable HTTP failure code, or null for absent or invalid input.</summary>
    public static int? DisplayCode(int? code) => code is >= 400 and <= 599 ? code : null;

    /// <summary>Parses only plain, three-digit HTTP failure codes from a URL.</summary>
    public static int? DisplayCode(string? code) =>
        code is { Length: 3 } &&
        int.TryParse(code, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? DisplayCode(parsed)
            : null;
}
