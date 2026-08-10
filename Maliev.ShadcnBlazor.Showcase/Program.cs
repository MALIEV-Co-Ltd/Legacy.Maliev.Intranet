using Maliev.ShadcnBlazor;
using Maliev.ShadcnBlazor.Showcase;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddMalievShadcn();
builder.Services.AddScoped<ShowcaseState>();
await builder.Build().RunAsync();
