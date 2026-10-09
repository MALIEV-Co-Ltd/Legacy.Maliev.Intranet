using Maliev.ShadcnBlazor;
using Maliev.ShadcnBlazor.Theming;
using System.Globalization;
using CustomerDocumentsInteractiveFixture;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddMalievShadcn(options => options.Theme = ShadcnThemePresets.BaseVegaNeutral.CreateTheme());
builder.Services.AddLocalization();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
var host = builder.Build();
var query = new Uri(host.Services.GetRequiredService<NavigationManager>().Uri).Query;
var culture = query.Contains("culture=th", StringComparison.Ordinal) ? "th" : "en";
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo(culture);
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(culture);
await host.RunAsync();
