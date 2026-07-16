using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class CookieSecurityContractTests
{
    [Theory]
    [InlineData("Development", CookieSecurePolicy.SameAsRequest)]
    [InlineData("Testing", CookieSecurePolicy.SameAsRequest)]
    [InlineData("Production", CookieSecurePolicy.Always)]
    public void SessionCookie_UsesEnvironmentAppropriateTransportSecurity(
        string environment,
        CookieSecurePolicy expected)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:redis", "localhost:6379");
            builder.UseSetting("Services:Auth", "http://auth/");
            builder.UseSetting("Services:Catalog", "http://catalog/");
            builder.UseSetting("Services:Customer", "http://customer/");
            builder.UseSetting("Services:Employee", "http://employee/");
            builder.UseSetting("Services:Procurement", "http://procurement/");
            builder.UseSetting("Services:Document", "http://document/");
            builder.UseSetting("Services:File", "http://file/");
            builder.UseSetting("Services:Order", "http://order/");
            builder.UseSetting("Services:Notification", "http://notification/");
        });

        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal(expected, options.Cookie.SecurePolicy);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
    }
}
