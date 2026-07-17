using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyAuthClientContractTests : IDisposable
{
    private const string Issuer = "https://auth.test";
    private const string Audience = "legacy-test";
    private const string KeyId = "test-signing-key";
    private readonly RSA signingKey = RSA.Create(2048);

    [Fact]
    public async Task Login_CorrectlySignedEmployeeToken_PreservesWireShapeAndProjectsClaims()
    {
        var handler = new LoginResponseHandler(CreateSignedToken());
        var client = CreateClient(handler);

        var result = await client.LoginAsync(
            "employee@maliev.com",
            "request-only-password",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("employee-id", result.Identity?.Id);
        Assert.Equal("employee@maliev.com", result.Identity?.UserName);
        Assert.Equal("employee@maliev.com", result.Identity?.Email);
        Assert.Contains("legacy-catalog.materials.read", result.Identity?.Permissions ?? []);
        Assert.Equal("/auth/v1/login", handler.RequestUri?.AbsolutePath);
        Assert.Equal("employee@maliev.com", handler.RequestJson?.GetProperty("userName").GetString());
        Assert.Equal("request-only-password", handler.RequestJson?.GetProperty("password").GetString());
        Assert.Equal(1, handler.RequestJson?.GetProperty("identityKind").GetInt32());
    }

    [Fact]
    public async Task Login_LegacyEmployeeTokenWithoutPermissions_RemainsPermissionlessAfterValidation()
    {
        var client = CreateClient(new LoginResponseHandler(CreateSignedToken(includeCatalogPermission: false)));

        var result = await client.LoginAsync(
            "employee@maliev.com",
            "request-only-password",
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Identity?.Permissions ?? []);
    }

    [Theory]
    [InlineData(TokenDefect.Unsigned)]
    [InlineData(TokenDefect.WrongSigningKey)]
    [InlineData(TokenDefect.WrongIssuer)]
    [InlineData(TokenDefect.WrongAudience)]
    [InlineData(TokenDefect.Expired)]
    [InlineData(TokenDefect.CustomerIdentity)]
    public async Task Login_UntrustedOrNonEmployeeToken_IsRejected(TokenDefect defect)
    {
        var client = CreateClient(new LoginResponseHandler(CreateToken(defect)));

        var result = await client.LoginAsync(
            "employee@maliev.com",
            "request-only-password",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Tokens);
        Assert.Null(result.Identity);
    }

    [Fact]
    public async Task Refresh_UntrustedToken_IsRejectedBeforeSessionRenewal()
    {
        var client = CreateClient(new LoginResponseHandler(CreateToken(TokenDefect.Unsigned)));

        var result = await client.RefreshAsync("server-only-refresh-token", CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(TokenEnvelopeDefect.WrongTokenType)]
    [InlineData(TokenEnvelopeDefect.EmptyAccessToken)]
    [InlineData(TokenEnvelopeDefect.EmptyRefreshToken)]
    [InlineData(TokenEnvelopeDefect.InvalidAccessLifetime)]
    [InlineData(TokenEnvelopeDefect.ExpiredRefreshToken)]
    public async Task Login_InvalidTokenEnvelope_IsRejected(TokenEnvelopeDefect defect)
    {
        var client = CreateClient(new LoginResponseHandler(CreateSignedToken(), defect));

        var result = await client.LoginAsync(
            "employee@maliev.com",
            "request-only-password",
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Tokens);
    }

    [Fact]
    public async Task Login_AuthServiceRateLimit_SurfacesBoundedRetryAfter()
    {
        var client = CreateClient(new LoginResponseHandler(
            CreateSignedToken(),
            statusCode: HttpStatusCode.TooManyRequests,
            retryAfterSeconds: 75));

        var exception = await Assert.ThrowsAsync<LegacyAuthRateLimitedException>(() => client.LoginAsync(
            "employee@maliev.com",
            "request-only-password",
            CancellationToken.None));

        Assert.Equal(75, exception.RetryAfterSeconds);
    }

    [Fact]
    public void ProductionRegistration_WithoutPublicKey_FailsClosed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = Issuer,
                ["Jwt:Audience"] = Audience,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLegacyAccessTokenValidation(configuration, validateOnStart: true);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<LegacyJwtValidationOptions>>().Value);

        Assert.Contains("Jwt:PublicKey", exception.Message, StringComparison.Ordinal);
    }

    private LegacyAuthClient CreateClient(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://auth/") },
            new LegacyAccessTokenValidator(Options.Create(new LegacyJwtValidationOptions
            {
                Issuer = Issuer,
                Audience = Audience,
                PublicKey = Convert.ToBase64String(
                    System.Text.Encoding.UTF8.GetBytes(signingKey.ExportSubjectPublicKeyInfoPem())),
                KeyId = KeyId,
            })),
            TimeProvider.System,
            NullLogger<LegacyAuthClient>.Instance);

    private string CreateToken(TokenDefect defect) => defect == TokenDefect.Unsigned
        ? CreateUnsignedToken()
        : defect == TokenDefect.WrongSigningKey
            ? CreateTokenSignedByAttacker()
        : CreateSignedToken(
            issuer: defect == TokenDefect.WrongIssuer ? "https://attacker.invalid" : Issuer,
            audience: defect == TokenDefect.WrongAudience ? "other-audience" : Audience,
            identityKind: defect == TokenDefect.CustomerIdentity ? "customer" : "employee",
            expires: defect == TokenDefect.Expired
                ? DateTime.UtcNow.AddMinutes(-1)
                : DateTime.UtcNow.AddMinutes(15));

    private string CreateSignedToken(
        string issuer = Issuer,
        string audience = Audience,
        string identityKind = "employee",
        DateTime? expires = null,
        bool includeCatalogPermission = true)
    {
        var now = DateTime.UtcNow;
        var tokenExpires = expires ?? now.AddMinutes(15);
        var notBefore = tokenExpires <= now ? now.AddMinutes(-10) : now.AddMinutes(-1);
        var key = new RsaSecurityKey(signingKey) { KeyId = KeyId };
        var token = new JwtSecurityToken(
            issuer,
            audience,
            CreateClaims(identityKind, includeCatalogPermission),
            notBefore,
            tokenExpires,
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateUnsignedToken()
    {
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            CreateClaims("employee"),
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(15));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string CreateTokenSignedByAttacker()
    {
        using var attackerKey = RSA.Create(2048);
        var token = new JwtSecurityToken(
            Issuer,
            Audience,
            CreateClaims("employee"),
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(15),
            new SigningCredentials(new RsaSecurityKey(attackerKey) { KeyId = KeyId }, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static Claim[] CreateClaims(string identityKind, bool includeCatalogPermission = true)
    {
        var claims = new List<Claim>
        {
        new Claim(JwtRegisteredClaimNames.Sub, "employee-id"),
        new Claim(JwtRegisteredClaimNames.Name, "employee@maliev.com"),
        new Claim(JwtRegisteredClaimNames.Email, "employee@maliev.com"),
        new Claim("identity_kind", identityKind),
        };
        if (includeCatalogPermission)
        {
            claims.Add(new Claim("permissions", "legacy-catalog.materials.read"));
        }

        return [.. claims];
    }

    public void Dispose() => signingKey.Dispose();

    public enum TokenDefect
    {
        Unsigned,
        WrongSigningKey,
        WrongIssuer,
        WrongAudience,
        Expired,
        CustomerIdentity,
    }

    public enum TokenEnvelopeDefect
    {
        None,
        WrongTokenType,
        EmptyAccessToken,
        EmptyRefreshToken,
        InvalidAccessLifetime,
        ExpiredRefreshToken,
    }

    private sealed class LoginResponseHandler(
        string accessToken,
        TokenEnvelopeDefect envelopeDefect = TokenEnvelopeDefect.None,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        int? retryAfterSeconds = null) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public JsonElement? RequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestJson = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var response = new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(new AuthTokenResponse(
                    envelopeDefect == TokenEnvelopeDefect.EmptyAccessToken ? string.Empty : accessToken,
                    envelopeDefect == TokenEnvelopeDefect.EmptyRefreshToken ? string.Empty : "server-only-refresh-token",
                    envelopeDefect == TokenEnvelopeDefect.WrongTokenType ? "Basic" : "Bearer",
                    envelopeDefect == TokenEnvelopeDefect.InvalidAccessLifetime ? 0 : 900,
                    envelopeDefect == TokenEnvelopeDefect.ExpiredRefreshToken
                        ? DateTimeOffset.UtcNow.AddMinutes(-1)
                        : DateTimeOffset.UtcNow.AddDays(14))),
            };
            if (retryAfterSeconds is not null)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(retryAfterSeconds.Value));
            }

            return response;
        }
    }
}
