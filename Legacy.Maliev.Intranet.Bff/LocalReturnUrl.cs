namespace Legacy.Maliev.Intranet.Bff;

internal static class LocalReturnUrl
{
    private const string DefaultPath = "/Dashboard";

    public static string Normalize(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || returnUrl[0] != '/')
        {
            return DefaultPath;
        }

        if ((returnUrl.Length > 1 && returnUrl[1] is '/' or '\\') ||
            returnUrl.Contains('\r', StringComparison.Ordinal) ||
            returnUrl.Contains('\n', StringComparison.Ordinal))
        {
            return DefaultPath;
        }

        return returnUrl;
    }
}
