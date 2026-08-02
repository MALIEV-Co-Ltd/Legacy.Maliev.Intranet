using Legacy.Maliev.Intranet.Client;
using Legacy.Maliev.Intranet.Contracts;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddMudServices();
builder.Services.AddLocalization();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<EmployeeSessionClient>();
builder.Services.AddScoped<EmployeeAuthenticationClient>();
builder.Services.AddScoped<LegacyThemeService>();
builder.Services.AddScoped<EmployeeAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(provider =>
    provider.GetRequiredService<EmployeeAuthenticationStateProvider>());

var host = builder.Build();
var js = host.Services.GetRequiredService<IJSRuntime>();
var selectedCulture = await js.InvokeAsync<string?>("malievCulture.get");
WorkspaceCulture.Apply(selectedCulture);
await host.RunAsync();
