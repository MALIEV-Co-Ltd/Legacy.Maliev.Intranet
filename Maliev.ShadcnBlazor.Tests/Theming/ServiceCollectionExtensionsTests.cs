using Maliev.ShadcnBlazor.Theming;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MudBlazor;

namespace Maliev.ShadcnBlazor.Tests.Theming;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMalievShadcnRegistersOptionsAndPopoverScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMalievShadcn(options => options.FontFamily = "Test Sans");
        using var provider = services.BuildServiceProvider();

        Assert.Equal("Test Sans", provider.GetRequiredService<IOptions<ShadcnOptions>>().Value.FontFamily);
        Assert.Equal(ShadcnCss.OverlayScopeClass,
            provider.GetRequiredService<IOptions<PopoverOptions>>().Value.ContainerClass);
    }
}
