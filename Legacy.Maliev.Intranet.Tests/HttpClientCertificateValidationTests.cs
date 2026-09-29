extern alias Bff;

using System.Net.Http;
using Legacy.Maliev.Intranet.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using BffProgram = Bff::Program;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class HttpClientCertificateValidationTests
{
    [Fact]
    public void BffAuthClients_UsePlatformCertificateValidationWithoutCertificateLoggingCallback()
    {
        using var factory = new WebApplicationFactory<BffProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("Services:Auth", "https://auth.test/");
            builder.UseSetting("ConnectionStrings:redis", "localhost:6379");
        });

        AssertPlatformValidation(factory.Services, Microsoft.Extensions.Options.Options.DefaultName);
        AssertPlatformValidation(factory.Services, "service-auth");
        AssertPlatformValidation(factory.Services, typeof(ILegacyAuthClient).FullName!);
        AssertPlatformValidation(factory.Services, typeof(IGoogleIdentityAuthClient).FullName!);
    }

    [Fact]
    public void RazorAuthClients_UsePlatformCertificateValidationWithoutCertificateLoggingCallback()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            TestJwtConfiguration.Configure(builder);
            builder.UseSetting("ConnectionStrings:redis", "localhost:6379");
            builder.UseSetting("Services:Auth", "https://auth.test/");
            builder.UseSetting("Services:Catalog", "https://catalog.test/");
            builder.UseSetting("Services:Customer", "https://customer.test/");
            builder.UseSetting("Services:Employee", "https://employee.test/");
            builder.UseSetting("Services:Procurement", "https://procurement.test/");
            builder.UseSetting("Services:Document", "https://document.test/");
            builder.UseSetting("Services:File", "https://file.test/");
            builder.UseSetting("Services:Order", "https://order.test/");
            builder.UseSetting("Services:Notification", "https://notification.test/");
        });

        AssertPlatformValidation(factory.Services, Microsoft.Extensions.Options.Options.DefaultName);
        AssertPlatformValidation(factory.Services, "service-auth");
        AssertPlatformValidation(factory.Services, typeof(ILegacyAuthClient).FullName!);
    }

    private static void AssertPlatformValidation(IServiceProvider services, string clientName)
    {
        var handler = services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = Assert.IsAssignableFrom<HttpMessageHandler>(delegating.InnerHandler);
        }

        switch (handler)
        {
            case SocketsHttpHandler sockets:
                Assert.Null(sockets.SslOptions.RemoteCertificateValidationCallback);
                break;
            case HttpClientHandler client:
                Assert.Null(client.ServerCertificateCustomValidationCallback);
                break;
            default:
                Assert.Fail($"Unexpected HTTP primary handler for {clientName}: {handler.GetType().FullName}");
                break;
        }
    }
}
