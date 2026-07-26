using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace Legacy.Maliev.Intranet.Bff;

/// <summary>Protects the short-lived nonce state used by the same-origin Google sign-in flow.</summary>
internal static class GoogleIdentityBffFlow
{
    public const string CookieName = "Legacy.Maliev.Intranet.GoogleIdentity";
    public const string ProtectionPurpose = "Legacy.Maliev.Intranet.GoogleIdentity.Flow.v1";
    public const string Application = "intranet";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Protect(IDataProtectionProvider provider, GoogleIdentityFlowState state) =>
        provider.CreateProtector(ProtectionPurpose).Protect(JsonSerializer.Serialize(state, JsonOptions));

    public static bool TryUnprotect(
        IDataProtectionProvider provider,
        string protectedState,
        out GoogleIdentityFlowState state)
    {
        try
        {
            var json = provider.CreateProtector(ProtectionPurpose).Unprotect(protectedState);
            var parsed = JsonSerializer.Deserialize<GoogleIdentityFlowState>(json, JsonOptions);
            if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Nonce))
            {
                state = parsed;
                return true;
            }
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            // Tampered or malformed state is treated as an expired flow.
        }

        state = new GoogleIdentityFlowState(string.Empty, "/Dashboard", DateTimeOffset.MinValue);
        return false;
    }

    public static bool FixedTimeEquals(string expected, string actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
