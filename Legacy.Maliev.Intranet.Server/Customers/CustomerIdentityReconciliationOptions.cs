using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Legacy.Maliev.Intranet.Customers;

/// <summary>Opt-in settings for AuthService's operation-keyed identity creation.</summary>
public sealed class CustomerIdentityReconciliationOptions
{
    /// <summary>Configuration section for the staged AuthService contract.</summary>
    public const string SectionName = "CustomerIdentityReconciliation";

    /// <summary>Enables the service-only operation-keyed AuthService route.</summary>
    public bool Enabled { get; set; }

    /// <summary>A dedicated, protected 256-bit base64 key that must remain stable across retries and replicas.</summary>
    public string? BootstrapKeyBase64 { get; set; }

    /// <summary>Rejects missing or malformed protected configuration before any profile write.</summary>
    public void Validate()
    {
        if (!Enabled)
        {
            return;
        }

        var key = DecodeKey();
        CryptographicOperations.ZeroMemory(key);
    }

    /// <summary>Derives the same high-entropy temporary password for one customer-create operation.</summary>
    public string DeriveBootstrapSecret(Guid operationId, int customerId)
    {
        if (!Enabled || operationId == Guid.Empty || customerId <= 0)
        {
            throw new InvalidOperationException("Customer identity reconciliation is not configured.");
        }

        var key = DecodeKey();

        try
        {
            var context = Encoding.UTF8.GetBytes(
                $"legacy-maliev/customer-identity-bootstrap/v1\n{operationId:D}\n{customerId.ToString(CultureInfo.InvariantCulture)}");
            var digest = HMACSHA256.HashData(key, context);
            try
            {
                // The fixed prefix satisfies each password character class; the PRF carries 256 bits of entropy.
                return "Aa1!" + Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
            finally
            {
                CryptographicOperations.ZeroMemory(digest);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] DecodeKey()
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(BootstrapKeyBase64 ?? string.Empty);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Customer identity reconciliation key is invalid.", exception);
        }

        if (key.Length == 32)
        {
            return key;
        }

        CryptographicOperations.ZeroMemory(key);
        throw new InvalidOperationException("Customer identity reconciliation key must be 256 bits.");
    }
}
