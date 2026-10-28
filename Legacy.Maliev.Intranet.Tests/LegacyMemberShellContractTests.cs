using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Legacy.Maliev.Intranet.Tests;

public sealed class LegacyMemberShellContractTests
{
    [Fact]
    public void MemberShell_UsesProfileMenuForLegacyProfileAndSignOut()
    {
        var root = FindRoot();
        var rail = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Components",
            "Shell",
            "LegacyNavigationRail.razor"));

        Assert.Contains("legacy-profile-menu", rail, StringComparison.Ordinal);
        Assert.Contains("legacy-profile", rail, StringComparison.Ordinal);
        Assert.Contains("aria-haspopup=\"dialog\"", rail, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", rail, StringComparison.Ordinal);
        Assert.Contains("legacy-profile-preferences", rail, StringComparison.Ordinal);
        Assert.Contains("<LegacyLanguageSelector />", rail, StringComparison.Ordinal);
        Assert.Contains("Href=\"/hr/profile\"", rail, StringComparison.Ordinal);
        Assert.Contains("Role=\"LegacyLinkRole.Navigation\"", rail, StringComparison.Ordinal);
        Assert.Contains("Sign out", rail, StringComparison.Ordinal);
        Assert.DoesNotContain("signup", rail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MemberShell_ProfileMenuIsKeyboardAndResponsiveSafe()
    {
        var root = FindRoot();
        var rail = File.ReadAllText(Path.Combine(
            root,
            "Legacy.Maliev.Intranet.Client",
            "Components",
            "Shell",
            "LegacyNavigationRail.razor"));
        var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor.css"));

        Assert.Contains("aria-expanded=\"@_profileMenuOpen\"", rail, StringComparison.Ordinal);
        Assert.Contains("Escape", rail, StringComparison.Ordinal);
        Assert.Contains("legacy-profile-popover", css, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-signout-button", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 48rem)", css, StringComparison.Ordinal);
        Assert.Contains("border-radius: 9999px", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task CompatibilityValidation_RendersCompiledPartialAndOneOrderedLocalDependencySet(string environment)
    {
        // Render only compiled shared views with synthetic content: never execute a purchase-order page or handler.
        await using var factory = new IntranetFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var hostEnvironment = services.GetRequiredService<IWebHostEnvironment>();
        var originalEnvironment = hostEnvironment.EnvironmentName;
        try
        {
            // This changes EnvironmentTagHelper rendering only; startup remains the existing isolated Testing factory.
            hostEnvironment.EnvironmentName = environment;
            var http = new DefaultHttpContext { RequestServices = services };
            http.Request.Scheme = "https";
            http.Request.Host = new HostString("localhost");
            http.Request.Path = "/";
            var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
            var engine = services.GetRequiredService<ICompositeViewEngine>();
            var partial = engine.GetView(null, "/Pages/Shared/_ValidationScriptsPartial.cshtml", isMainPage: false);
            Assert.True(partial.Success, "The compiled compatibility validation partial is missing.");
            var partialView = Assert.IsAssignableFrom<IView>(partial.View);

            using var partialOutput = new StringWriter();
            var partialContext = new ViewContext(action, partialView,
                new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
                new TempDataDictionary(http, services.GetRequiredService<ITempDataProvider>()),
                partialOutput, new HtmlHelperOptions());
            await partialView.RenderAsync(partialContext);
            Assert.DoesNotContain("<script", partialOutput.ToString(), StringComparison.OrdinalIgnoreCase);

            var layout = engine.GetView(null, "/Pages/Shared/_Layout.cshtml", isMainPage: false);
            Assert.True(layout.Success, "The compiled compatibility layout is missing.");
            var razor = Assert.IsType<RazorView>(layout.View);
            razor.RazorPage.BodyContent = new HtmlString("<p id=\"synthetic-content\">Synthetic validation consumer</p>");
            var page = Assert.IsAssignableFrom<RazorPageBase>(razor.RazorPage);
            razor.RazorPage.PreviousSectionWriters = new Dictionary<string, RenderAsyncDelegate>
            {
                ["Scripts"] = () =>
                {
                    page.WriteLiteral("<script id=\"synthetic-page-script\"></script>");
                    return Task.CompletedTask;
                },
            };
            using var layoutOutput = new StringWriter();
            var layoutContext = new ViewContext(action, razor,
                new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
                new TempDataDictionary(http, services.GetRequiredService<ITempDataProvider>()),
                layoutOutput, new HtmlHelperOptions());
            await razor.RenderAsync(layoutContext);
            var html = layoutOutput.ToString();
            var scripts = Regex.Matches(html, "<script[^>]+src=\"(?<src>[^\"]+)\"", RegexOptions.IgnoreCase)
                .Select(match => WebUtility.HtmlDecode(match.Groups["src"].Value).Split('?')[0]).ToArray();
            var validators = new[]
            {
                "/lib/compat-validation/jquery-3.7.1.min.js",
                "/lib/compat-validation/jquery.validate-1.22.1.min.js",
                "/lib/compat-validation/jquery.validate.unobtrusive-3.2.12.min.js",
            };
            Assert.Equal([.. validators, "/js/compat-shell.js"], scripts);
            Assert.Contains("id=\"synthetic-content\"", html, StringComparison.Ordinal);
            var pageScript = html.IndexOf("id=\"synthetic-page-script\"", StringComparison.Ordinal);
            Assert.True(pageScript >= 0);
            Assert.All(validators, asset => Assert.True(html.IndexOf(asset, StringComparison.Ordinal) < pageScript));

            var hashes = new[]
            {
                "fc9a93dd241f6b045cbff0481cf4e1901becd0e12fb45166a8f17f95823f0b1a",
                "d48b8fee4fe0f9df7cfebe7e0f52365f6c3ec61055249fac853ec3d645d7898b",
                "1fc39d86f356a710875ddf4eb30f11ef23ac5a3f4240a183325ba361506c60cc",
            };
            using var client = factory.CreateClient();
            for (var index = 0; index < validators.Length; index++)
            {
                using var response = await client.GetAsync(validators[index]);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Contains(response.Content.Headers.ContentType?.MediaType,
                    new[] { "text/javascript", "application/javascript" });
                var bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(hashes[index], Convert.ToHexStringLower(SHA256.HashData(bytes)));
                var expected = await File.ReadAllBytesAsync(Path.Combine(hostEnvironment.WebRootPath,
                    validators[index].TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
                Assert.Equal(expected, bytes);
            }
        }
        finally
        {
            hostEnvironment.EnvironmentName = originalEnvironment;
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
