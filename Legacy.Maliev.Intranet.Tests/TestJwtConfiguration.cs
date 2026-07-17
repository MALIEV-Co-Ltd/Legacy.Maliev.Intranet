using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;

namespace Legacy.Maliev.Intranet.Tests;

internal static class TestJwtConfiguration
{
    private static readonly string PublicKeyPem = CreatePublicKeyPem();

    public static void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Issuer", "https://auth.test");
        builder.UseSetting("Jwt:Audience", "legacy-test");
        builder.UseSetting("Jwt:PublicKeyPem", PublicKeyPem);
        builder.UseSetting("Jwt:KeyId", "test-signing-key");
        builder.UseSetting("Services:Order", "http://order/");
    }

    private static string CreatePublicKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportSubjectPublicKeyInfoPem();
    }
}
