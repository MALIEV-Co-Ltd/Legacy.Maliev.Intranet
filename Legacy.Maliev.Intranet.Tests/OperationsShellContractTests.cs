namespace Legacy.Maliev.Intranet.Tests;

public sealed class OperationsShellContractTests
{
    [Fact]
    public void BlazorShell_UsesTheDocumentedInsetSidebarComposition()
    {
        var root = FindRoot();
        var layout = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "MainLayout.razor");
        var navigation = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor");
        var railCss = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor.css");

        Assert.Contains("Width=\"var(--legacy-rail-width)\"", layout, StringComparison.Ordinal);
        Assert.Contains("Variant=\"ShadcnSidebarVariant.Inset\"", navigation, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarRail TargetId=\"legacy-navigation-rail\" />", navigation, StringComparison.Ordinal);
        Assert.Contains("images/MALIEV_BLACK.svg", navigation, StringComparison.Ordinal);
        Assert.Contains("<ShadcnCollapsible Class=\"legacy-rail-collapsible\"", navigation, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarMenuBadge", navigation, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarMenuButton Size=\"ShadcnSidebarMenuButtonSize.Large\"", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-rail-brand__mark", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("::deep .legacy-rail-link { min-height: 2.75rem; }", railCss, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 48rem)", railCss, StringComparison.Ordinal);
        Assert.Contains(".legacy-sidebar-host ::deep .legacy-rail-link { min-height: 2.75rem;", railCss, StringComparison.Ordinal);
    }

    [Fact]
    public void VisualSystem_UsesTheLibrarySansTypographyForShellAndTables()
    {
        var root = FindRoot();
        var program = Read(root, "Legacy.Maliev.Intranet.Client", "Program.cs");
        var tokens = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "design-tokens.css");
        var modules = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "module-pages.css");
        var index = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");

        Assert.DoesNotContain("options.FontFamily", program, StringComparison.Ordinal);
        Assert.Contains("--maliev-font-sans: var(--shadcn-font-sans)", tokens, StringComparison.Ordinal);
        Assert.Contains(":where(.operational-table, .shadcn-table) .mlv-mono", modules, StringComparison.Ordinal);
        Assert.Contains("font-family: inherit", modules, StringComparison.Ordinal);
        Assert.Contains("font-variant-numeric: tabular-nums", modules, StringComparison.Ordinal);
        Assert.DoesNotContain("css/ibm-plex-sans-thai.css", index, StringComparison.Ordinal);
    }

    [Fact]
    public void BlazorShell_UsesPersistentRailAuthorizedSearchAndAuthorizedQuickActions()
    {
        var root = FindRoot();
        var layout = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "MainLayout.razor");
        var topBar = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor");
        var navigation = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor");
        var search = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyGlobalSearch.razor");
        var actions = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyQuickActions.razor");

        Assert.Contains("<ShadcnSidebarProvider", layout, StringComparison.Ordinal);
        Assert.Contains("<LegacyNavigationRail Session=\"session\" OnNavigate=\"CloseNavigationAsync\" />", layout, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarInset", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"legacy-topbar-logo\"", topBar, StringComparison.Ordinal);
        Assert.DoesNotContain("IsDrawer=\"true\"", layout, StringComparison.Ordinal);
        Assert.Contains("LegacyNavigationAuthorization.IsEnabled", navigation, StringComparison.Ordinal);
        Assert.Contains("LegacyNavigationAuthorization.IsEnabled", search, StringComparison.Ordinal);
        Assert.Contains("LegacyAppNavigation.Groups", search, StringComparison.Ordinal);
        Assert.Contains("role=\"combobox\"", search, StringComparison.Ordinal);
        Assert.Contains("role=\"listbox\"", search, StringComparison.Ordinal);
        Assert.Contains("aria-activedescendant", search, StringComparison.Ordinal);
        Assert.Contains("legacy-global-search-option-", search, StringComparison.Ordinal);
        Assert.Contains("ArrowDown", search, StringComparison.Ordinal);
        Assert.Contains("ArrowUp", search, StringComparison.Ordinal);
        Assert.Contains("Escape", search, StringComparison.Ordinal);
        Assert.Contains("LegacyNavigationAuthorization.IsEnabled", actions, StringComparison.Ordinal);
        Assert.Contains("/Quotations/Create", actions, StringComparison.Ordinal);
        Assert.Contains("/Orders/Create", actions, StringComparison.Ordinal);
        Assert.Contains("<ShadcnThemeProvider", layout, StringComparison.Ordinal);
        Assert.Contains("Class=\"legacy-layout\"", layout, StringComparison.Ordinal);
        Assert.Contains("<main id=\"main-content\" class=\"legacy-main-content legacy-page-container\" tabindex=\"-1\">", layout, StringComparison.Ordinal);
        Assert.Contains("<ShadcnInput TValue=\"string\"", search, StringComparison.Ordinal);
        Assert.Contains("LucideIconCatalog.Instance.Get", search, StringComparison.Ordinal);
        Assert.DoesNotContain("<Mud", string.Concat(layout, topBar, navigation, search, actions), StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", search, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", search, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BlazorShell_IsResponsiveAccessibleAndUsesApprovedTypography()
    {
        var root = FindRoot();
        var railCss = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor.css");
        var searchCss = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyGlobalSearch.razor.css");
        var actionsCss = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyQuickActions.razor.css");
        var topBarCss = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor.css");
        var appCss = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "app.css");
        var tokens = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "design-tokens.css");
        var index = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");

        Assert.Contains("max-height: 100dvh", railCss, StringComparison.Ordinal);
        Assert.Contains("height: 2.75rem", searchCss, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 720px)", searchCss, StringComparison.Ordinal);
        Assert.DoesNotContain(".legacy-quick-actions { display: none; }", actionsCss, StringComparison.Ordinal);
        Assert.Contains("min-width: 44px", topBarCss, StringComparison.Ordinal);
        Assert.Contains(".legacy-workspace-shell", appCss, StringComparison.Ordinal);
        Assert.DoesNotContain(".legacy-navigation-backdrop", appCss, StringComparison.Ordinal);
        Assert.Contains("--legacy-rail-width: 224px", tokens, StringComparison.Ordinal);
        Assert.Contains("--maliev-font-sans: var(--shadcn-font-sans)", tokens, StringComparison.Ordinal);
        Assert.Contains("_content/Maliev.ShadcnBlazor/fonts/geist-sans-variable.woff2", index, StringComparison.Ordinal);
        Assert.Contains("_content/Maliev.ShadcnBlazor/fonts/noto-sans-thai.woff2", index, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileNavigation_UsesTheLibraryModalSurfaceAndKeepsTheDesktopTriggerInsideTheSidebar()
    {
        var root = FindRoot();
        var layout = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "MainLayout.razor");
        var topBar = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor");
        var navigation = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor");

        Assert.Contains("@bind-MobileOpen=\"_mobileNavigationOpen\"", layout, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarInset Role=\"none\" Class=\"legacy-workspace-frame\">", layout, StringComparison.Ordinal);
        Assert.Contains("id=\"legacy-mobile-navigation-toggle\"", topBar, StringComparison.Ordinal);
        Assert.Contains("TargetId=\"legacy-navigation-rail\"", topBar, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebar Id=\"legacy-navigation-rail\"", navigation, StringComparison.Ordinal);
        Assert.Contains("Collapsible=\"ShadcnSidebarCollapsible.Icon\"", navigation, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarHeader", navigation, StringComparison.Ordinal);
        Assert.Contains("id=\"legacy-sidebar-collapse\"", navigation, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarContent", navigation, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarFooter", navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveShell_PreservesNamedQuickActionsAndTouchSizedControlsWithoutOverflow()
    {
        var root = FindRoot();
        var topBarCss = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor.css");
        var railCss = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor.css");
        var actions = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyQuickActions.razor");
        var actionsCss = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyQuickActions.razor.css");
        var appCss = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "app.css");

        Assert.Contains("<nav class=\"legacy-quick-actions\"", actions, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@Text[item.Label]\"", actions, StringComparison.Ordinal);
        Assert.Contains("min-width: 0", topBarCss, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 960px)", topBarCss, StringComparison.Ordinal);
        Assert.Contains("min-width: 44px", actionsCss, StringComparison.Ordinal);
        Assert.DoesNotContain("::deep .legacy-rail-link { min-height: 2.75rem; }", railCss, StringComparison.Ordinal);
        Assert.Contains("max-width: 100%", appCss, StringComparison.Ordinal);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", railCss, StringComparison.Ordinal);
    }

    [Fact]
    public void CompatibilityHost_UsesTheSharedOperationsShellAndWorkingRouteSearch()
    {
        var root = FindRoot();
        var layout = Read(root, "Legacy.Maliev.Intranet", "Pages", "Shared", "_Layout.cshtml");
        var css = Read(root, "Legacy.Maliev.Intranet", "wwwroot", "css", "site.css");
        var script = Read(root, "Legacy.Maliev.Intranet", "wwwroot", "js", "compat-shell.js");
        var program = Read(root, "Legacy.Maliev.Intranet", "Program.cs");

        Assert.Contains("class=\"compat-rail\"", layout, StringComparison.Ordinal);
        Assert.Contains("class=\"compat-utility\"", layout, StringComparison.Ordinal);
        Assert.Contains("id=\"compat-search\"", layout, StringComparison.Ordinal);
        Assert.Contains("/Quotations/Create", layout, StringComparison.Ordinal);
        Assert.Contains("/Orders/Create", layout, StringComparison.Ordinal);
        Assert.Contains("HasPermission(\"legacy.quotations.create\")", layout, StringComparison.Ordinal);
        Assert.Contains("HasPermission(\"legacy.orders.create\")", layout, StringComparison.Ordinal);
        Assert.Contains("HasPermission(\"legacy-customer.customers.list\")", layout, StringComparison.Ordinal);
        Assert.Contains("HasPermission(\"legacy-procurement.suppliers.read\")", layout, StringComparison.Ordinal);
        Assert.Contains("@Html.AntiForgeryToken()", layout, StringComparison.Ordinal);
        Assert.Contains("antiforgery.ValidateRequestAsync(context)", program, StringComparison.Ordinal);
        Assert.Contains("User.FindAll(\"permissions\")", layout, StringComparison.Ordinal);
        Assert.Contains("~/css/ibm-plex-sans-thai.css", layout, StringComparison.Ordinal);
        Assert.Contains("width: var(--rail-width)", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 1180px)", css, StringComparison.Ordinal);
        Assert.Contains("window.location.assign(route)", script, StringComparison.Ordinal);
        Assert.Contains("event.key.toLowerCase() === 'k'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("token", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CultureBootstrap_RestoresTheSavedCultureBeforeStartup()
    {
        var root = FindRoot();
        var program = Read(root, "Legacy.Maliev.Intranet.Client", "Program.cs");
        var script = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "js", "workspace-culture.js");
        var index = Read(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "index.html");

        Assert.Contains("malievCulture.get", program, StringComparison.Ordinal);
        Assert.Contains("WorkspaceCulture.Apply(selectedCulture)", program, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem('maliev_culture'", script, StringComparison.Ordinal);
        Assert.Contains("document.documentElement.lang", script, StringComparison.Ordinal);
        Assert.Contains("js/workspace-culture.js", index, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_PlacesBrandAndCollapseControlInsideSidebarAndKeepsTopbarUtilitiesVisible()
    {
        var root = FindRoot();
        var topBar = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor");
        var topBarCss = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor.css");
        var navigation = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor");
        var railCss = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor.css");

        Assert.Contains("class=\"legacy-topbar__brand\"", topBar, StringComparison.Ordinal);
        Assert.DoesNotContain("Text[\"Light\"]", topBar, StringComparison.Ordinal);
        Assert.DoesNotContain("Text[\"Dark\"]", topBar, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"@ThemeLabel\"", topBar, StringComparison.Ordinal);
        Assert.Contains("legacy-topbar__utilities", topBar, StringComparison.Ordinal);
        Assert.Contains("display: grid", topBarCss, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(18rem, 1fr) auto auto", topBarCss, StringComparison.Ordinal);
        Assert.DoesNotContain("margin-left: calc(-1", topBarCss, StringComparison.Ordinal);
        Assert.DoesNotContain("margin-block: -", topBarCss, StringComparison.Ordinal);
        Assert.Contains("border: 1px solid var(--shadcn-border)", topBarCss, StringComparison.Ordinal);
        Assert.Contains("legacy-rail-logo legacy-logo-link", navigation, StringComparison.Ordinal);
        Assert.Contains("legacy-sidebar-collapse", navigation, StringComparison.Ordinal);
        Assert.Contains("max-height: 100dvh", railCss, StringComparison.Ordinal);
        Assert.Contains(".legacy-sidebar-host ::deep .legacy-rail-chevron[aria-expanded=\"true\"] svg", railCss, StringComparison.Ordinal);
        Assert.Contains("transform: rotate(90deg)", railCss, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedShellAndReusableComponentsContainNoMudMarkupOrImports()
    {
        var root = FindRoot();
        var paths = new[]
        {
            Path.Combine(root, "Legacy.Maliev.Intranet.Client.Shared"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Layout"),
            Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell")
        };

        var violations = paths
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".razor" or ".cs")
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("<Mud", StringComparison.Ordinal)
                    || source.Contains("@using MudBlazor", StringComparison.Ordinal)
                    || source.Contains("using MudBlazor", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void NavigationHierarchy_DeclaresEveryCreateActionAndItsPrimaryOwnerExplicitly()
    {
        var root = FindRoot();
        var model = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyAppNavigation.cs");
        var navigation = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor");
        var railCss = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor.css");

        Assert.Contains("enum LegacyNavItemKind", model, StringComparison.Ordinal);
        Assert.Contains("LegacyNavItemKind Kind = LegacyNavItemKind.Primary", model, StringComparison.Ordinal);
        Assert.Contains("string? ParentHref = null", model, StringComparison.Ordinal);

        var expectedChildren = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/customers/new"] = "/customers",
            ["/Orders/Create"] = "/sales/orders",
            ["/Quotations/Create"] = "/Quotations/Index",
            ["/accounting/new"] = "/finance/invoices",
            ["/Finances/Create"] = "/Finances/Index",
            ["/Materials/Create"] = "/mfg/materials",
            ["/purchasing/new"] = "/purchasing",
            ["/Suppliers/Create"] = "/purchasing/suppliers",
        };

        foreach (var (href, parentHref) in expectedChildren)
        {
            var escapedHref = System.Text.RegularExpressions.Regex.Escape(href);
            var escapedParent = System.Text.RegularExpressions.Regex.Escape(parentHref);
            Assert.Matches(
                $"new\\([^\\r\\n]*\"{escapedHref}\"[^\\r\\n]*Kind: LegacyNavItemKind\\.ChildAction[^\\r\\n]*ParentHref: \"{escapedParent}\"",
                model);
        }

        Assert.Contains("item.Kind == LegacyNavItemKind.ChildAction", navigation, StringComparison.Ordinal);
        Assert.Contains("legacy-rail-link--child", navigation, StringComparison.Ordinal);
        Assert.Contains(".legacy-rail-link--child", railCss, StringComparison.Ordinal);
        Assert.DoesNotContain("::deep .legacy-rail-link { min-height: 2.75rem; }", railCss, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationHierarchy_RendersChildActionsAsOwnedNestedListsAndSelectsOneMostSpecificPage()
    {
        var root = FindRoot();
        var navigation = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyNavigationRail.razor");

        Assert.Contains("<ShadcnSidebarMenu Class=\"legacy-rail-items\">", navigation, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarMenuItem Class=\"legacy-rail-item\">", navigation, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarMenuSub Class=\"legacy-rail-children\"", navigation, StringComparison.Ordinal);
        Assert.Contains("<ShadcnHoverCardTrigger Href=\"@item.Href\"", navigation, StringComparison.Ordinal);
        Assert.Contains("<ShadcnSidebarMenuSubButton", navigation, StringComparison.Ordinal);
        Assert.Contains("GetEnabledChildren(item)", navigation, StringComparison.Ordinal);
        Assert.Contains("Open=\"@IsItemOpen(item)\"", navigation, StringComparison.Ordinal);
        Assert.Contains("OpenChanged=\"@(open => SetItemOpen(item, open))\"", navigation, StringComparison.Ordinal);
        Assert.Contains("IsItemPageCurrent(item)", navigation, StringComparison.Ordinal);
        Assert.Contains("FindCurrentItem()", navigation, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-current=\"@(IsItemActive(item) ? \"page\" : null)\"", navigation, StringComparison.Ordinal);
    }

    [Fact]
    public void TopBar_DeclaresFourResponsiveGridZonesWithoutRemovingQuickCreateRoutes()
    {
        var root = FindRoot();
        var topBar = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor");
        var topBarCss = Read(root, "Legacy.Maliev.Intranet.Client", "Layout", "LegacyTopBar.razor.css");
        var quickActions = Read(root, "Legacy.Maliev.Intranet.Client", "Components", "Shell", "LegacyQuickActions.razor");

        foreach (var zone in new[] { "brand", "search", "actions", "utilities" })
            Assert.Contains($"legacy-topbar__{zone}", topBar, StringComparison.Ordinal);

        Assert.Contains("display: grid", topBarCss, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 768px)", topBarCss, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 960px)", topBarCss, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 720px)", topBarCss, StringComparison.Ordinal);
        Assert.DoesNotContain("margin-left: calc(-1", topBarCss, StringComparison.Ordinal);
        Assert.DoesNotContain("margin-block: -", topBarCss, StringComparison.Ordinal);
        Assert.Contains("/Quotations/Create", quickActions, StringComparison.Ordinal);
        Assert.Contains("/Orders/Create", quickActions, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] segments) =>
        File.ReadAllText(Path.Combine([root, .. segments]));

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
