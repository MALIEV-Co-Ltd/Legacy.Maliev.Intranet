using Microsoft.Extensions.Http.Resilience;

namespace Legacy.Maliev.Intranet.Bff.Accounting;

/// <summary>Single-attempt employee credential transport, default-disabled by route configuration.</summary>
public static class BillingRegistration
{
    /// <summary>Registers the fixed Accounting transport; no service-token handler or redirect follows.</summary>
    public static IServiceCollection AddBillingClient(this IServiceCollection services, IConfiguration configuration)
    {
#pragma warning disable EXTEXP0001 // Match the existing single-attempt BFF transport registration.
        services.AddHttpClient<BillingProxy>(client =>
        {
            client.BaseAddress = new Uri(configuration["Services:Accounting"] ?? "https+http://legacy-maliev-accounting-service");
            client.Timeout = TimeSpan.FromSeconds(15);
        }).RemoveAllResilienceHandlers()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
#pragma warning restore EXTEXP0001
        return services;
    }
}
