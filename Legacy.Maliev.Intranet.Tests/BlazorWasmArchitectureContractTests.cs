namespace Legacy.Maliev.Intranet.Tests;

public sealed class BlazorWasmArchitectureContractTests
{
    [Fact]
    public void Solution_ContainsStandaloneClientSameOriginBffAndNarrowContractsProjects()
    {
        var root = FindRoot();
        var solution = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.slnx"));

        Assert.Contains("Legacy.Maliev.Intranet.Client/Legacy.Maliev.Intranet.Client.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Bff/Legacy.Maliev.Intranet.Bff.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet.Contracts/Legacy.Maliev.Intranet.Contracts.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("Legacy.Maliev.Intranet/Legacy.Maliev.Intranet.csproj", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_IsNet10BlazorWebAssemblyWithMudBlazorAndNoServerSecrets()
    {
        var root = FindRoot();
        var clientRoot = Path.Combine(root, "Legacy.Maliev.Intranet.Client");
        var project = File.ReadAllText(Path.Combine(clientRoot, "Legacy.Maliev.Intranet.Client.csproj"));
        var source = ReadSourceTree(clientRoot);

        Assert.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", project, StringComparison.Ordinal);
        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", project, StringComparison.Ordinal);
        Assert.Contains("PackageReference Include=\"MudBlazor\"", project, StringComparison.Ordinal);
        Assert.Contains("..\\Legacy.Maliev.Intranet.Contracts\\Legacy.Maliev.Intranet.Contracts.csproj", project, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshToken", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ClientSecret", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ServiceAuthentication", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Services:", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Client_LoginUsesMudBlazorAndOnlyTheSameOriginCsrfProtectedBff()
    {
        var root = FindRoot();
        var clientRoot = Path.Combine(root, "Legacy.Maliev.Intranet.Client");
        var loginPath = Path.Combine(clientRoot, "Pages", "Login.razor");
        var authenticationClientPath = Path.Combine(clientRoot, "EmployeeAuthenticationClient.cs");

        Assert.True(File.Exists(loginPath), "The standalone WASM login route is missing.");
        Assert.True(File.Exists(authenticationClientPath), "The same-origin authentication client is missing.");
        var login = File.ReadAllText(loginPath);
        var authenticationClient = File.ReadAllText(authenticationClientPath);

        Assert.Contains("@page \"/Login\"", login, StringComparison.Ordinal);
        Assert.Contains("<PageTitle>", login, StringComparison.Ordinal);
        Assert.Contains("<MudForm", login, StringComparison.Ordinal);
        Assert.Contains("InputType.Email", login, StringComparison.Ordinal);
        Assert.Contains("InputType.Password", login, StringComparison.Ordinal);
        Assert.Contains("Required=\"true\"", login, StringComparison.Ordinal);
        Assert.Contains("MudProgressLinear", login, StringComparison.Ordinal);
        Assert.Contains("MudAlert", login, StringComparison.Ordinal);
        Assert.Contains("Password = string.Empty", login, StringComparison.Ordinal);
        Assert.Contains("forceLoad: true", login, StringComparison.Ordinal);
        Assert.Contains("GetAsync", authenticationClient, StringComparison.Ordinal);
        Assert.Contains("/bff/login", authenticationClient, StringComparison.Ordinal);
        Assert.Contains("X-CSRF-TOKEN", authenticationClient, StringComparison.Ordinal);
        Assert.Contains("EmployeeSignInRequest", authenticationClient, StringComparison.Ordinal);
        Assert.Contains("EmployeeSignInResponse", authenticationClient, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", authenticationClient, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", authenticationClient, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", authenticationClient, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionStorage", authenticationClient, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccessToken", authenticationClient, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", authenticationClient, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Client_DashboardRouteRequiresEmployeeSessionAndReturnsAnonymousUsersToLogin()
    {
        var root = FindRoot();
        var dashboardPath = Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Dashboard.razor");

        Assert.True(File.Exists(dashboardPath), "The approved post-login Dashboard route is missing.");
        var dashboard = File.ReadAllText(dashboardPath);
        Assert.Contains("@page \"/Dashboard\"", dashboard, StringComparison.Ordinal);
        Assert.Contains("@attribute [Authorize]", dashboard, StringComparison.Ordinal);
        Assert.Contains("<PageTitle>", dashboard, StringComparison.Ordinal);
        Assert.Contains("HtmlTag=\"h1\"", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", dashboard, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", dashboard, StringComparison.OrdinalIgnoreCase);

        var app = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "App.razor"));
        var redirect = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "LoginRedirect.razor"));
        Assert.Contains("CascadingAuthenticationState", app, StringComparison.Ordinal);
        Assert.Contains("AuthorizeRouteView", app, StringComparison.Ordinal);
        Assert.Contains("<NotAuthorized>", app, StringComparison.Ordinal);
        Assert.Contains("<LoginRedirect", app, StringComparison.Ordinal);
        Assert.Contains("/Login?returnUrl=", redirect, StringComparison.Ordinal);
    }

    [Fact]
    public void Bff_OwnsCookieCsrfAndServerAuthorizationWithoutDomainBusinessLogic()
    {
        var root = FindRoot();
        var bffRoot = Path.Combine(root, "Legacy.Maliev.Intranet.Bff");
        var project = File.ReadAllText(Path.Combine(bffRoot, "Legacy.Maliev.Intranet.Bff.csproj"));
        var program = File.ReadAllText(Path.Combine(bffRoot, "Program.cs"));

        Assert.Contains("Microsoft.NET.Sdk.Web", project, StringComparison.Ordinal);
        Assert.Contains("..\\Legacy.Maliev.Intranet.Contracts\\Legacy.Maliev.Intranet.Contracts.csproj", project, StringComparison.Ordinal);
        Assert.Contains("CookieAuthenticationDefaults.AuthenticationScheme", program, StringComparison.Ordinal);
        Assert.Contains("Cookie.HttpOnly = true", program, StringComparison.Ordinal);
        Assert.Contains("Cookie.SecurePolicy = LegacyCookieSecurity.ResolveSecurePolicy(builder.Environment.EnvironmentName)", program, StringComparison.Ordinal);
        Assert.Contains("AddAntiforgery", program, StringComparison.Ordinal);
        Assert.Contains("RequireAuthenticatedUser", program, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization", program, StringComparison.Ordinal);
        Assert.Contains("AddEndpointFilter<AntiforgeryValidationFilter>()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", program, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateOrder", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Calculate", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Contracts_ExposeOnlyBrowserSafeSessionAndRequestOnlySignInContracts()
    {
        var root = FindRoot();
        var contractsRoot = Path.Combine(root, "Legacy.Maliev.Intranet.Contracts");
        var source = ReadSourceTree(contractsRoot);

        Assert.Contains("EmployeeSessionSummary", source, StringComparison.Ordinal);
        Assert.Contains("EmployeeSignInRequest", source, StringComparison.Ordinal);
        Assert.Contains("EmployeeSignInResponse", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ClientSecret", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SessionTicket", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSourceTree(string directory) => string.Join(
        Environment.NewLine,
        Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => Path.GetExtension(path) is ".cs" or ".razor" or ".json")
            .Select(File.ReadAllText));

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
