using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Legacy.Maliev.Intranet.Auth;

/// <summary>Server-only public-key settings for validating AuthService access tokens.</summary>
public sealed class LegacyJwtValidationOptions
{
    /// <summary>Gets the AuthService configuration section shared with the token issuer.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Gets or sets the required token issuer.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Gets or sets the required token audience.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>Gets or sets the Base64-encoded RSA public-key PEM used by the existing Aspire/GitOps contract.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>Gets or sets an RSA public-key PEM when the runtime projects PEM directly.</summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>Gets or sets the stable AuthService signing-key identifier.</summary>
    public string KeyId { get; set; } = string.Empty;
}

/// <summary>Validates AuthService access tokens before their claims enter an employee session.</summary>
public interface ILegacyAccessTokenValidator
{
    /// <summary>Validates the complete JWT trust boundary and returns employee-only identity claims.</summary>
    bool TryValidateEmployee(string accessToken, out EmployeeIdentity? identity);
}

/// <summary>Validates RS256 AuthService tokens using server-only issuer, audience and public-key configuration.</summary>
public sealed class LegacyAccessTokenValidator : ILegacyAccessTokenValidator, IDisposable
{
    private readonly JwtSecurityTokenHandler handler = new() { MapInboundClaims = false };
    private readonly RSA rsa = RSA.Create();
    private readonly TokenValidationParameters validationParameters;

    /// <summary>Initializes the validator from validated server runtime configuration.</summary>
    public LegacyAccessTokenValidator(IOptions<LegacyJwtValidationOptions> options)
    {
        var jwt = options.Value;
        rsa.ImportFromPem(ResolvePublicKeyPem(jwt));
        var signingKey = new RsaSecurityKey(rsa);
        if (!string.IsNullOrWhiteSpace(jwt.KeyId))
        {
            signingKey.KeyId = jwt.KeyId;
        }

        validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    /// <inheritdoc />
    public bool TryValidateEmployee(string accessToken, out EmployeeIdentity? identity)
    {
        identity = null;
        try
        {
            var principal = handler.ValidateToken(accessToken, validationParameters, out _);
            if (!string.Equals(
                    principal.FindFirst("identity_kind")?.Value,
                    "employee",
                    StringComparison.Ordinal))
            {
                return false;
            }

            var id = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var name = principal.FindFirst(JwtRegisteredClaimNames.Name)?.Value;
            var email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var permissions = principal.FindAll("permissions")
                .Select(claim => claim.Value)
                .Where(permission => !string.IsNullOrWhiteSpace(permission))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            identity = new EmployeeIdentity(id, name, email, permissions);
            return true;
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose() => rsa.Dispose();

    internal static bool IsPublicKeyValid(LegacyJwtValidationOptions options)
    {
        try
        {
            using var candidate = RSA.Create();
            candidate.ImportFromPem(ResolvePublicKeyPem(options));
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }
    }

    private static string ResolvePublicKeyPem(LegacyJwtValidationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PublicKeyPem))
        {
            return options.PublicKeyPem;
        }

        if (string.IsNullOrWhiteSpace(options.PublicKey))
        {
            throw new CryptographicException("A JWT public key is required.");
        }

        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(options.PublicKey));
    }
}

/// <summary>Registers fail-closed server-side AuthService JWT validation.</summary>
public static class LegacyJwtValidationServiceCollectionExtensions
{
    /// <summary>Adds AuthService token validation and optionally validates configuration during startup.</summary>
    public static IServiceCollection AddLegacyAccessTokenValidation(
        this IServiceCollection services,
        IConfiguration configuration,
        bool validateOnStart)
    {
        var options = services.AddOptions<LegacyJwtValidationOptions>()
            .Bind(configuration.GetSection(LegacyJwtValidationOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                LegacyAccessTokenValidator.IsPublicKeyValid,
                "Jwt:PublicKey must contain a Base64 PEM, or Jwt:PublicKeyPem must contain an RSA public key PEM.");
        if (validateOnStart)
        {
            options.ValidateOnStart();
        }

        services.AddSingleton<ILegacyAccessTokenValidator, LegacyAccessTokenValidator>();
        return services;
    }
}
