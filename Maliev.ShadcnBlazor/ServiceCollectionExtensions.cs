using Maliev.ShadcnBlazor.Theming;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Maliev.ShadcnBlazor;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMalievShadcn(
        this IServiceCollection services,
        Action<ShadcnOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<ShadcnOptions>();
        if (configure is not null)
            services.Configure(configure);
        services.AddMudServices(configuration =>
            configuration.PopoverOptions.ContainerClass = ShadcnCss.OverlayScopeClass);
        return services;
    }
}
