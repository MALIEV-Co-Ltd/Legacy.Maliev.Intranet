# Shadcn Blazor Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the reusable package and validation foundation that every Shadcn Blazor component family will extend.

**Architecture:** Add a MudBlazor-backed Razor Class Library with a pinned upstream reference manifest, semantic token sheet, centralized MudTheme factory, opt-in provider scope, deterministic showcase, and Playwright smoke harness. Keep the library independent of Intranet production code; the current solution hosts it only as a build, test, and packaging workspace.

**Tech Stack:** .NET 10, Blazor Razor Class Library, MudBlazor 9.7.0, bUnit 2.9.0, xUnit 2.9.3, Microsoft.Playwright.Xunit 1.61.0, Chromium, PowerShell 7, Shadcn Base/Vega/Neutral commit `6261bd89f72d794aea491482cc2acfd8dc3d63e2`.

## Global Constraints

- Preserve the approved design at `docs/superpowers/specs/2026-08-10-shadcn-blazor-component-library-design.md` and roadmap at `docs/superpowers/plans/2026-08-10-shadcn-blazor-implementation-roadmap.md`.
- Use Shadcn commit `6261bd89f72d794aea491482cc2acfd8dc3d63e2`, registry `apps/v4/registry/bases/base/ui`, and `apps/v4/registry/styles/style-vega.css` as immutable inputs.
- Keep MudBlazor at 9.7.0 and the existing ASP.NET Core packages at 10.0.3; this slice is not a dependency-upgrade lane.
- Target `net10.0`; nullable, implicit usings, latest C#, and warnings-as-errors are inherited from `Directory.Build.props`.
- Do not reference `Legacy.Maliev.Intranet.*` from `Maliev.ShadcnBlazor`.
- Do not edit Intranet production pages, layouts, CSS, DTOs, services, authentication, resources, or business behavior in this slice.
- Use semantic package tokens; no component-facing raw presentation colors outside the token declaration and MudTheme mapping.
- Run build first, focused tests second, affected suite third, and browser smoke checks last before each commit.
- Stage only files owned by the current task. Preserve all unrelated work and do not push.

---

## File and responsibility map

### Package

- `Maliev.ShadcnBlazor/Maliev.ShadcnBlazor.csproj` — packable RCL, MudBlazor dependency, static assets, package metadata.
- `Maliev.ShadcnBlazor/_Imports.razor` — common framework, package, and MudBlazor Razor imports.
- `Maliev.ShadcnBlazor/Reference/shadcn-reference.json` — pinned repository, style, 64-component source identities, and blob SHAs.
- `Maliev.ShadcnBlazor/Theming/ShadcnCss.cs` — shared scope class constants.
- `Maliev.ShadcnBlazor/Theming/ShadcnDirection.cs` — LTR/RTL public enum.
- `Maliev.ShadcnBlazor/Theming/ShadcnOptions.cs` — package font and default theme options.
- `Maliev.ShadcnBlazor/Theming/ShadcnThemeFactory.cs` — canonical MudTheme mapping.
- `Maliev.ShadcnBlazor/Theming/ShadcnContext.cs` — cascaded current theme/direction state.
- `Maliev.ShadcnBlazor/ServiceCollectionExtensions.cs` — single `AddMalievShadcn` registration seam.
- `Maliev.ShadcnBlazor/Components/ShadcnThemeProvider.razor` — theme, portal providers, direction, scope, and consumer content root.
- `Maliev.ShadcnBlazor/wwwroot/css/shadcn-base.css` — Base/Vega/Neutral semantic tokens, scope rules, media contracts.
- `Maliev.ShadcnBlazor/README.md` — package setup and foundation fixture usage.
- `Maliev.ShadcnBlazor/licenses/shadcn-ui-LICENSE.md` — pinned upstream MIT license.
- `Maliev.ShadcnBlazor/licenses/MudBlazor-LICENSE` — MudBlazor 9.7.0 MIT license.

### Machine-readable delivery evidence

- `docs/shadcn-component-ledger.json` — all 64 component names, plan owner, classification, and evidence status.
- `scripts/verify-shadcn-reference.ps1` — online comparison of manifest blob SHAs to the pinned GitHub commit.

### Component tests

- `Maliev.ShadcnBlazor.Tests/Maliev.ShadcnBlazor.Tests.csproj` — xUnit/bUnit project.
- `Maliev.ShadcnBlazor.Tests/Contracts/ReferenceManifestTests.cs` — immutable source and catalog coverage.
- `Maliev.ShadcnBlazor.Tests/Contracts/TokenContractTests.cs` — canonical token and media-query contract.
- `Maliev.ShadcnBlazor.Tests/Theming/ShadcnThemeFactoryTests.cs` — exact palette and consumer font mapping.
- `Maliev.ShadcnBlazor.Tests/Theming/ServiceCollectionExtensionsTests.cs` — registration and popover portal scope.
- `Maliev.ShadcnBlazor.Tests/Components/ShadcnThemeProviderTests.cs` — rendered scope, providers, theme, and direction.

### Showcase

- `Maliev.ShadcnBlazor.Showcase/Maliev.ShadcnBlazor.Showcase.csproj` — standalone WASM fixture host.
- `Maliev.ShadcnBlazor.Showcase/Program.cs` — package and showcase-state registration.
- `Maliev.ShadcnBlazor.Showcase/App.razor` — router.
- `Maliev.ShadcnBlazor.Showcase/_Imports.razor` — Razor imports.
- `Maliev.ShadcnBlazor.Showcase/Layout/MainLayout.razor` — provider root and deterministic theme/direction controls.
- `Maliev.ShadcnBlazor.Showcase/ShowcaseState.cs` — light/dark and LTR/RTL fixture state.
- `Maliev.ShadcnBlazor.Showcase/Pages/Home.razor` — host identity.
- `Maliev.ShadcnBlazor.Showcase/Pages/Foundation.razor` — token, typography, radius, and provider fixture.
- `Maliev.ShadcnBlazor.Showcase/wwwroot/index.html` — MudBlazor and RCL assets in required order.

### Browser validation

- `Maliev.ShadcnBlazor.BrowserTests/Maliev.ShadcnBlazor.BrowserTests.csproj` — Playwright xUnit project.
- `Maliev.ShadcnBlazor.BrowserTests/Infrastructure/ShowcaseServerFixture.cs` — isolated built WASM host process on a free loopback port.
- `Maliev.ShadcnBlazor.BrowserTests/Infrastructure/PlaywrightFixture.cs` — pinned Chromium lifetime.
- `Maliev.ShadcnBlazor.BrowserTests/Infrastructure/BrowserCollection.cs` — shared fixtures.
- `Maliev.ShadcnBlazor.BrowserTests/FoundationSmokeTests.cs` — page identity, token, theme, direction, console, and screenshot proof.
- `scripts/install-shadcn-browser.ps1` — builds the browser-test project and installs its pinned Chromium.

---

### Task 1: Scaffold the package and lock the reference catalog

**Files:**
- Create: `Maliev.ShadcnBlazor/Maliev.ShadcnBlazor.csproj`
- Create: `Maliev.ShadcnBlazor/_Imports.razor`
- Create: `Maliev.ShadcnBlazor/Reference/shadcn-reference.json`
- Create: `Maliev.ShadcnBlazor.Tests/Maliev.ShadcnBlazor.Tests.csproj`
- Create: `Maliev.ShadcnBlazor.Tests/Contracts/ReferenceManifestTests.cs`
- Create: `docs/shadcn-component-ledger.json`
- Modify: `Legacy.Maliev.Intranet.slnx`

**Interfaces:**
- Consumes: The approved design's 64 names and pinned upstream identifiers.
- Produces: Packable project `Maliev.ShadcnBlazor`, test project `Maliev.ShadcnBlazor.Tests`, manifest schema `shadcn-reference/v1`, and ledger schema `shadcn-component-ledger/v1` used by every later plan.

- [ ] **Step 1: Create the RCL and test project files**

Create `Maliev.ShadcnBlazor/Maliev.ShadcnBlazor.csproj` exactly as follows:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>true</IsPackable>
    <PackageId>Maliev.ShadcnBlazor</PackageId>
    <Description>Reusable Shadcn Base UI components for Blazor backed by MudBlazor.</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.Intranet.git</RepositoryUrl>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="MudBlazor" Version="9.7.0" />
  </ItemGroup>
  <ItemGroup>
    <None Update="Reference\shadcn-reference.json" Pack="true" PackagePath="reference\" />
  </ItemGroup>
</Project>
```

Create `Maliev.ShadcnBlazor/_Imports.razor`:

```razor
@using Maliev.ShadcnBlazor
@using Maliev.ShadcnBlazor.Theming
@using Microsoft.AspNetCore.Components
@using MudBlazor
```

Create `Maliev.ShadcnBlazor.Tests/Maliev.ShadcnBlazor.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="bunit" Version="2.9.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj" />
  </ItemGroup>
</Project>
```

Add both projects to the solution:

```powershell
dotnet solution .\Legacy.Maliev.Intranet.slnx add `
  .\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj `
  .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj
```

- [ ] **Step 2: Write the failing catalog contract**

Create `Maliev.ShadcnBlazor.Tests/Contracts/ReferenceManifestTests.cs`:

```csharp
using System.Text.Json;

namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class ReferenceManifestTests
{
    private const string ExpectedCommit = "6261bd89f72d794aea491482cc2acfd8dc3d63e2";

    [Fact]
    public void ManifestPinsTheApprovedSourceAndAllRequestedComponents()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(FindFile(
            "Maliev.ShadcnBlazor", "Reference", "shadcn-reference.json")));
        var root = manifest.RootElement;
        Assert.Equal("shadcn-reference/v1", root.GetProperty("schema").GetString());
        Assert.Equal(ExpectedCommit, root.GetProperty("commit").GetString());
        Assert.Equal("base", root.GetProperty("primitive").GetString());
        Assert.Equal("vega", root.GetProperty("style").GetString());
        Assert.Equal("neutral", root.GetProperty("theme").GetString());

        var components = root.GetProperty("components").EnumerateArray().ToArray();
        Assert.Equal(64, components.Length);
        Assert.Equal(64, components.Select(x => x.GetProperty("name").GetString()).Distinct().Count());
        Assert.Equal(61, components.Count(x => x.GetProperty("sourceKind").GetString() == "registry-file"));
        Assert.Equal(3, components.Count(x => x.GetProperty("sourceKind").GetString() == "composition"));
        Assert.All(components.Where(x => x.GetProperty("sourceKind").GetString() == "registry-file"), component =>
            Assert.Matches("^[0-9a-f]{40}$", component.GetProperty("blobSha").GetString()!));
    }

    [Fact]
    public void LedgerHasOnePlannedEntryForEveryManifestComponent()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(FindFile(
            "Maliev.ShadcnBlazor", "Reference", "shadcn-reference.json")));
        using var ledger = JsonDocument.Parse(File.ReadAllText(FindFile("docs", "shadcn-component-ledger.json")));

        var expected = manifest.RootElement.GetProperty("components").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()).Order().ToArray();
        var actual = ledger.RootElement.GetProperty("components").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()).Order().ToArray();

        Assert.Equal("shadcn-component-ledger/v1", ledger.RootElement.GetProperty("schema").GetString());
        Assert.Equal(expected, actual);
        Assert.All(ledger.RootElement.GetProperty("components").EnumerateArray(), entry =>
            Assert.Equal("planned", entry.GetProperty("status").GetString()));
    }

    private static string FindFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException(), Path.Combine(segments));
    }
}
```

- [ ] **Step 3: Run the test to verify the missing-manifest failure**

Run:

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release `
  --filter FullyQualifiedName~ReferenceManifestTests
```

Expected: FAIL because `Reference/shadcn-reference.json` and `docs/shadcn-component-ledger.json` do not exist.

- [ ] **Step 4: Create the pinned manifest**

Create `Maliev.ShadcnBlazor/Reference/shadcn-reference.json` with this top-level shape and exact component entries:

```json
{
  "schema": "shadcn-reference/v1",
  "repository": "https://github.com/shadcn-ui/ui",
  "commit": "6261bd89f72d794aea491482cc2acfd8dc3d63e2",
  "primitive": "base",
  "style": "vega",
  "theme": "neutral",
  "registryRoot": "apps/v4/registry/bases/base/ui",
  "styleSource": {
    "path": "apps/v4/registry/styles/style-vega.css",
    "blobSha": "5621c5d5d76c015cec864f60e0d2e49c2765d938"
  },
  "components": [
    { "name": "Accordion", "slug": "accordion", "sourceKind": "registry-file", "blobSha": "d0080d19c888c68cf1bb933b5a9dda0476ed19e3" },
    { "name": "Alert", "slug": "alert", "sourceKind": "registry-file", "blobSha": "f1b66301005fd52dc5b428bd5549ebcee0cb4aff" },
    { "name": "Alert Dialog", "slug": "alert-dialog", "sourceKind": "registry-file", "blobSha": "74b7390d74f99f32bdb60327d7ce9e99005099f6" },
    { "name": "Aspect Ratio", "slug": "aspect-ratio", "sourceKind": "registry-file", "blobSha": "d005931ea4968369a5c02afa038f4b4826fcc2c7" },
    { "name": "Attachment", "slug": "attachment", "sourceKind": "registry-file", "blobSha": "bf86fcd201f0d395dd2574e2bd715dc04269dcba" },
    { "name": "Avatar", "slug": "avatar", "sourceKind": "registry-file", "blobSha": "39c33e3ff1f035291378089eef864b5e89735d87" },
    { "name": "Badge", "slug": "badge", "sourceKind": "registry-file", "blobSha": "c67b787699e8d96dec72fbdff3404257a8ab01e0" },
    { "name": "Breadcrumb", "slug": "breadcrumb", "sourceKind": "registry-file", "blobSha": "e2e8da335a39156944fc3887bb84c0f62ceabbc2" },
    { "name": "Bubble", "slug": "bubble", "sourceKind": "registry-file", "blobSha": "237d23381766f739422142769cc3314dd474b208" },
    { "name": "Button", "slug": "button", "sourceKind": "registry-file", "blobSha": "fa343173a022bb2dd18247daa2c34e6e7b37c38c" },
    { "name": "Button Group", "slug": "button-group", "sourceKind": "registry-file", "blobSha": "c1f1aeaa23c9458d55c280e2d78d4fd79291878a" },
    { "name": "Calendar", "slug": "calendar", "sourceKind": "registry-file", "blobSha": "bae95f6b61aa0e26c894a0b83114c984be8fc5bf" },
    { "name": "Card", "slug": "card", "sourceKind": "registry-file", "blobSha": "bed029b599af855dd76e1ec9f83d5d7d01be700a" },
    { "name": "Carousel", "slug": "carousel", "sourceKind": "registry-file", "blobSha": "cbdbc3c1ed36ef7fd28d8173b3f95b420aee993f" },
    { "name": "Chart", "slug": "chart", "sourceKind": "registry-file", "blobSha": "86905c59c704f6f482aca9732cddcd589f691e0f" },
    { "name": "Checkbox", "slug": "checkbox", "sourceKind": "registry-file", "blobSha": "3bcf55ab5ef6db4053f23129cd2445a6d9d3332d" },
    { "name": "Collapsible", "slug": "collapsible", "sourceKind": "registry-file", "blobSha": "488fb33af5107a94791ef3235ee58861e3ed6855" },
    { "name": "Combobox", "slug": "combobox", "sourceKind": "registry-file", "blobSha": "75cd8118eb8a6cc37dfdf3aadb61dde464908be6" },
    { "name": "Command", "slug": "command", "sourceKind": "registry-file", "blobSha": "8ce76c45ad1333eec8e90af7bebc251c8ddf31f2" },
    { "name": "Context Menu", "slug": "context-menu", "sourceKind": "registry-file", "blobSha": "71260e78b242ec696d8dcb734a173eeae508681e" },
    { "name": "Data Table", "slug": "data-table", "sourceKind": "composition", "sources": ["table", "pagination"] },
    { "name": "Date Picker", "slug": "date-picker", "sourceKind": "composition", "sources": ["calendar", "popover", "button"] },
    { "name": "Dialog", "slug": "dialog", "sourceKind": "registry-file", "blobSha": "6c88a072af3f4b7f3ee217fa95648606d8e13a5f" },
    { "name": "Direction", "slug": "direction", "sourceKind": "registry-file", "blobSha": "d8cf134614ac38b4fbc6c902e6e2cf770ba389c1" },
    { "name": "Drawer", "slug": "drawer", "sourceKind": "registry-file", "blobSha": "505f7326fe93c13311db634aec3d2262e6be9b23" },
    { "name": "Dropdown Menu", "slug": "dropdown-menu", "sourceKind": "registry-file", "blobSha": "92f0ad66503447d5fb654b3e7ece3cdc3eb0e100" },
    { "name": "Empty", "slug": "empty", "sourceKind": "registry-file", "blobSha": "38ff021d6b1f5edd3cceadd6812aa27edbdc2885" },
    { "name": "Field", "slug": "field", "sourceKind": "registry-file", "blobSha": "8ab9ef35e41a06c7fc1c29df7417155d60024562" },
    { "name": "Hover Card", "slug": "hover-card", "sourceKind": "registry-file", "blobSha": "0b6fb3cc4c611623b12cebc14f164fcfac9a3848" },
    { "name": "Input", "slug": "input", "sourceKind": "registry-file", "blobSha": "d45a0b058ba9f59a11334ca6fcbf02a81506ad66" },
    { "name": "Input Group", "slug": "input-group", "sourceKind": "registry-file", "blobSha": "3aea74ebd07b8e0cbc44a4b30f8db26e77912446" },
    { "name": "Input OTP", "slug": "input-otp", "sourceKind": "registry-file", "blobSha": "334527068ea34a08f877718def8bc69f0147bb3b" },
    { "name": "Item", "slug": "item", "sourceKind": "registry-file", "blobSha": "6230a4915831eabd49f5586ed35233b4d28a135a" },
    { "name": "Kbd", "slug": "kbd", "sourceKind": "registry-file", "blobSha": "2eef65d8ef06a27e3456d6808b342a94a1650457" },
    { "name": "Label", "slug": "label", "sourceKind": "registry-file", "blobSha": "a439e097d65313152827715dd37c2f2c8d7b3900" },
    { "name": "Marker", "slug": "marker", "sourceKind": "registry-file", "blobSha": "fa507ddf2c885e1f182caef9e9bbc0042918f5e0" },
    { "name": "Menubar", "slug": "menubar", "sourceKind": "registry-file", "blobSha": "d2a26f09c402e7ec401a16628676d72fbef6db1b" },
    { "name": "Message", "slug": "message", "sourceKind": "registry-file", "blobSha": "86ca73af5e8432c82d77d97dc65123e6794892e1" },
    { "name": "Message Scroller", "slug": "message-scroller", "sourceKind": "registry-file", "blobSha": "c8518b0cd078518bf58d32033dac1c87df70ce42" },
    { "name": "Native Select", "slug": "native-select", "sourceKind": "registry-file", "blobSha": "47f8ce63266f7bfaf6e17bfe04fcb234acec2664" },
    { "name": "Navigation Menu", "slug": "navigation-menu", "sourceKind": "registry-file", "blobSha": "e3d12d0d44106fb3295ff4873bb53f5f488925a6" },
    { "name": "Pagination", "slug": "pagination", "sourceKind": "registry-file", "blobSha": "016dec358462d61e5fbee7ef82d59ca8dcb2f211" },
    { "name": "Popover", "slug": "popover", "sourceKind": "registry-file", "blobSha": "7ce182012730deae1094684396a33f4959a57cd2" },
    { "name": "Progress", "slug": "progress", "sourceKind": "registry-file", "blobSha": "3df1ca586c6000b963b1b6b315bae81b666d5367" },
    { "name": "Questionnaire", "slug": "questionnaire", "sourceKind": "registry-file", "blobSha": "5b36d35336ba6a3f205e7ef4c94cfe4896ea2267" },
    { "name": "Radio Group", "slug": "radio-group", "sourceKind": "registry-file", "blobSha": "dc7acc81b5ffdc1d0e6e2f256972218ce3ad9ce5" },
    { "name": "Resizable", "slug": "resizable", "sourceKind": "registry-file", "blobSha": "0e6a967d5680207cabce35579498bef20a61a477" },
    { "name": "Scroll Area", "slug": "scroll-area", "sourceKind": "registry-file", "blobSha": "7d251e056d6ed274f687d680065c16e94e55b864" },
    { "name": "Select", "slug": "select", "sourceKind": "registry-file", "blobSha": "35ca37f35bd66ef830374e90eb1e447895ebf99c" },
    { "name": "Separator", "slug": "separator", "sourceKind": "registry-file", "blobSha": "cf212eb4ded8ed527c9491737db44c4d2dc7b2e5" },
    { "name": "Sheet", "slug": "sheet", "sourceKind": "registry-file", "blobSha": "c8850e23ad47f1b46ed8fd94df9febac3d870a88" },
    { "name": "Sidebar", "slug": "sidebar", "sourceKind": "registry-file", "blobSha": "cfeb87df4245f3f7aac9f0b90da0c4c97d537d48" },
    { "name": "Skeleton", "slug": "skeleton", "sourceKind": "registry-file", "blobSha": "0f76bfb60fc4d7d7f7cd950ae4c1d9d523984208" },
    { "name": "Slider", "slug": "slider", "sourceKind": "registry-file", "blobSha": "42018e6fd69a7d03827b18d6a72be6439aacb046" },
    { "name": "Spinner", "slug": "spinner", "sourceKind": "registry-file", "blobSha": "e2b6067051e443125d8e83552c53b2667e56c276" },
    { "name": "Switch", "slug": "switch", "sourceKind": "registry-file", "blobSha": "6afc57f76e848724e5b72babebc5da8e16ce0504" },
    { "name": "Table", "slug": "table", "sourceKind": "registry-file", "blobSha": "8167a7bc1a3eecbd0b649a01232694d7b9f1b851" },
    { "name": "Tabs", "slug": "tabs", "sourceKind": "registry-file", "blobSha": "a9f20615cc4fbd8fafc64ae6494b79a4cf59074a" },
    { "name": "Textarea", "slug": "textarea", "sourceKind": "registry-file", "blobSha": "e703bf57c63904f2f9c5f0354f8c1277150f03af" },
    { "name": "Toast", "slug": "toast", "sourceKind": "registry-file", "blobSha": "bee9db04bf52bec472c4d0febfb959cc39a1e7b9" },
    { "name": "Toggle", "slug": "toggle", "sourceKind": "registry-file", "blobSha": "dc7c644e9e869c2e787097eda3ea04568ba1691e" },
    { "name": "Toggle Group", "slug": "toggle-group", "sourceKind": "registry-file", "blobSha": "7735cbdf018af9028ddf1ede01d681a20f00febe" },
    { "name": "Tooltip", "slug": "tooltip", "sourceKind": "registry-file", "blobSha": "f145a091aca9815cca73b4f0f68cae316390fb3a" },
    { "name": "Typography", "slug": "typography", "sourceKind": "composition", "sources": ["https://ui.shadcn.com/docs/typeset"] }
  ]
}
```

- [ ] **Step 5: Create the 64-entry ledger from the manifest**

Create `docs/shadcn-component-ledger.json` by projecting the manifest in PowerShell, assigning plan owner and approved classification without hand-copying names:

```powershell
$manifest = Get-Content -Raw '.\Maliev.ShadcnBlazor\Reference\shadcn-reference.json' | ConvertFrom-Json
$planByName = @{}
@('Direction','Aspect Ratio','Typography','Label','Field','Item','Kbd','Separator','Empty') | ForEach-Object { $planByName[$_] = 2 }
@('Button','Button Group','Toggle','Toggle Group','Checkbox','Radio Group','Switch','Slider') | ForEach-Object { $planByName[$_] = 3 }
@('Input','Textarea','Input Group','Input OTP','Native Select','Select','Combobox','Calendar','Date Picker') | ForEach-Object { $planByName[$_] = 4 }
@('Alert','Progress','Spinner','Skeleton','Toast','Avatar','Badge','Card','Carousel') | ForEach-Object { $planByName[$_] = 5 }
@('Accordion','Collapsible','Resizable','Scroll Area','Breadcrumb','Pagination','Tabs','Navigation Menu','Sidebar') | ForEach-Object { $planByName[$_] = 6 }
@('Dialog','Alert Dialog','Drawer','Sheet','Popover','Hover Card','Tooltip','Dropdown Menu','Context Menu','Menubar','Command') | ForEach-Object { $planByName[$_] = 7 }
@('Table','Data Table','Chart') | ForEach-Object { $planByName[$_] = 8 }
@('Attachment','Bubble','Marker','Message','Message Scroller','Questionnaire') | ForEach-Object { $planByName[$_] = 9 }

$custom = @('Aspect Ratio','Attachment','Bubble','Command','Empty','Field','Input Group','Input OTP','Item','Kbd','Label','Marker','Message','Message Scroller','Native Select','Questionnaire','Resizable','Scroll Area','Typography')
$composition = @('Alert Dialog','Breadcrumb','Button Group','Card','Chart','Combobox','Context Menu','Data Table','Date Picker','Dropdown Menu','Empty','Hover Card','Menubar','Navigation Menu','Pagination','Sheet','Sidebar','Table','Toast')
$entries = foreach ($component in $manifest.components) {
    $classification = if ($component.name -in $custom) { 'custom' } elseif ($component.name -in $composition) { 'composition' } else { 'adapter' }
    [ordered]@{
        name = $component.name
        slug = $component.slug
        plan = $planByName[$component.name]
        classification = $classification
        status = 'planned'
        evidence = [ordered]@{ api=$false; componentTests=$false; accessibility=$false; interaction=$false; computedStyle=$false; visual=$false; intranet=$false }
        deviations = @()
    }
}
$ledger = [ordered]@{ schema='shadcn-component-ledger/v1'; referenceCommit=$manifest.commit; components=$entries } |
    ConvertTo-Json -Depth 8
$ledger
```

Use the printed JSON as the complete content of `docs/shadcn-component-ledger.json` and create it with `apply_patch` so the tracked change remains explicit.

- [ ] **Step 6: Run the focused contract and solution build**

Run:

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore `
  --filter FullyQualifiedName~ReferenceManifestTests
```

Expected: build succeeds with 0 warnings and 0 errors; 2 focused tests pass.

- [ ] **Step 7: Commit the scaffold and immutable catalog**

```powershell
git add -- Legacy.Maliev.Intranet.slnx Maliev.ShadcnBlazor Maliev.ShadcnBlazor.Tests docs/shadcn-component-ledger.json
git commit -m "build: scaffold shadcn blazor package"
```

### Task 2: Add the pinned semantic token layer

**Files:**
- Create: `Maliev.ShadcnBlazor/wwwroot/css/shadcn-base.css`
- Create: `Maliev.ShadcnBlazor/Theming/ShadcnCss.cs`
- Create: `Maliev.ShadcnBlazor.Tests/Contracts/TokenContractTests.cs`

**Interfaces:**
- Consumes: Package static-web-asset path and pinned Base/Vega/Neutral source.
- Produces: CSS import `_content/Maliev.ShadcnBlazor/css/shadcn-base.css`; constants `ShadcnCss.ScopeClass`, `ShadcnCss.OverlayScopeClass`, and `ShadcnCss.StylesheetPath`.

- [ ] **Step 1: Write the failing token contract**

Create `Maliev.ShadcnBlazor.Tests/Contracts/TokenContractTests.cs`:

```csharp
namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class TokenContractTests
{
    [Fact]
    public void CssDefinesPinnedNeutralTokensAndAccessibilityMedia()
    {
        var css = File.ReadAllText(FindCss());
        string[] required =
        [
            "--shadcn-background: oklch(1 0 0)",
            "--shadcn-foreground: oklch(0.145 0 0)",
            "--shadcn-primary: oklch(0.205 0 0)",
            "--shadcn-primary-foreground: oklch(0.985 0 0)",
            "--shadcn-border: oklch(0.922 0 0)",
            "--shadcn-input: oklch(0.922 0 0)",
            "--shadcn-ring: oklch(0.708 0 0)",
            "--shadcn-radius: 0.625rem",
            "--shadcn-control-height: 2.25rem",
            "[data-shadcn-theme=\"dark\"]",
            "@media (prefers-reduced-motion: reduce)",
            "@media (forced-colors: active)",
            "@media (pointer: coarse)"
        ];
        Assert.All(required, value => Assert.Contains(value, css, StringComparison.Ordinal));
        Assert.DoesNotContain("--mud-", css, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindCss()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "Maliev.ShadcnBlazor", "wwwroot", "css", "shadcn-base.css");
    }
}
```

- [ ] **Step 2: Run the test to verify the missing-CSS failure**

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release `
  --filter FullyQualifiedName~TokenContractTests
```

Expected: FAIL with `DirectoryNotFoundException` or `FileNotFoundException` for `shadcn-base.css`.

- [ ] **Step 3: Add the CSS constants**

Create `Maliev.ShadcnBlazor/Theming/ShadcnCss.cs`:

```csharp
namespace Maliev.ShadcnBlazor.Theming;

public static class ShadcnCss
{
    public const string ScopeClass = "shadcn-scope";
    public const string OverlayScopeClass = "shadcn-overlay-scope";
    public const string StylesheetPath = "_content/Maliev.ShadcnBlazor/css/shadcn-base.css";
}
```

- [ ] **Step 4: Add the scoped Base/Vega/Neutral tokens**

Create `Maliev.ShadcnBlazor/wwwroot/css/shadcn-base.css` with this complete foundation:

```css
:root {
    --shadcn-font-sans: ui-sans-serif, system-ui, sans-serif;
    --shadcn-background: oklch(1 0 0);
    --shadcn-foreground: oklch(0.145 0 0);
    --shadcn-card: oklch(1 0 0);
    --shadcn-card-foreground: oklch(0.145 0 0);
    --shadcn-popover: oklch(1 0 0);
    --shadcn-popover-foreground: oklch(0.145 0 0);
    --shadcn-primary: oklch(0.205 0 0);
    --shadcn-primary-foreground: oklch(0.985 0 0);
    --shadcn-secondary: oklch(0.97 0 0);
    --shadcn-secondary-foreground: oklch(0.205 0 0);
    --shadcn-muted: oklch(0.97 0 0);
    --shadcn-muted-foreground: oklch(0.556 0 0);
    --shadcn-accent: oklch(0.97 0 0);
    --shadcn-accent-foreground: oklch(0.205 0 0);
    --shadcn-destructive: oklch(0.577 0.245 27.325);
    --shadcn-destructive-foreground: oklch(0.985 0 0);
    --shadcn-border: oklch(0.922 0 0);
    --shadcn-input: oklch(0.922 0 0);
    --shadcn-ring: oklch(0.708 0 0);
    --shadcn-chart-1: oklch(0.646 0.222 41.116);
    --shadcn-chart-2: oklch(0.6 0.118 184.704);
    --shadcn-chart-3: oklch(0.398 0.07 227.392);
    --shadcn-chart-4: oklch(0.828 0.189 84.429);
    --shadcn-chart-5: oklch(0.769 0.188 70.08);
    --shadcn-sidebar: oklch(0.985 0 0);
    --shadcn-sidebar-foreground: oklch(0.145 0 0);
    --shadcn-sidebar-primary: oklch(0.205 0 0);
    --shadcn-sidebar-primary-foreground: oklch(0.985 0 0);
    --shadcn-sidebar-accent: oklch(0.97 0 0);
    --shadcn-sidebar-accent-foreground: oklch(0.205 0 0);
    --shadcn-sidebar-border: oklch(0.922 0 0);
    --shadcn-sidebar-ring: oklch(0.708 0 0);
    --shadcn-radius: 0.625rem;
    --shadcn-radius-sm: calc(var(--shadcn-radius) * 0.6);
    --shadcn-radius-md: calc(var(--shadcn-radius) * 0.8);
    --shadcn-radius-lg: var(--shadcn-radius);
    --shadcn-radius-xl: calc(var(--shadcn-radius) * 1.4);
    --shadcn-radius-2xl: calc(var(--shadcn-radius) * 1.8);
    --shadcn-radius-3xl: calc(var(--shadcn-radius) * 2.2);
    --shadcn-radius-4xl: calc(var(--shadcn-radius) * 2.6);
    --shadcn-control-height: 2.25rem;
    --shadcn-control-height-sm: 2rem;
    --shadcn-control-height-lg: 2.5rem;
    --shadcn-shadow-xs: 0 1px 2px rgb(0 0 0 / 0.05);
    --shadcn-shadow-sm: 0 1px 3px rgb(0 0 0 / 0.1), 0 1px 2px -1px rgb(0 0 0 / 0.1);
    --shadcn-shadow-md: 0 4px 6px -1px rgb(0 0 0 / 0.1), 0 2px 4px -2px rgb(0 0 0 / 0.1);
}

[data-shadcn-theme="dark"],
.shadcn-overlay-scope[data-shadcn-theme="dark"] {
    --shadcn-background: oklch(0.145 0 0);
    --shadcn-foreground: oklch(0.985 0 0);
    --shadcn-card: oklch(0.205 0 0);
    --shadcn-card-foreground: oklch(0.985 0 0);
    --shadcn-popover: oklch(0.205 0 0);
    --shadcn-popover-foreground: oklch(0.985 0 0);
    --shadcn-primary: oklch(0.922 0 0);
    --shadcn-primary-foreground: oklch(0.205 0 0);
    --shadcn-secondary: oklch(0.269 0 0);
    --shadcn-secondary-foreground: oklch(0.985 0 0);
    --shadcn-muted: oklch(0.269 0 0);
    --shadcn-muted-foreground: oklch(0.708 0 0);
    --shadcn-accent: oklch(0.269 0 0);
    --shadcn-accent-foreground: oklch(0.985 0 0);
    --shadcn-destructive: oklch(0.704 0.191 22.216);
    --shadcn-destructive-foreground: oklch(0.985 0 0);
    --shadcn-border: oklch(1 0 0 / 10%);
    --shadcn-input: oklch(1 0 0 / 15%);
    --shadcn-ring: oklch(0.556 0 0);
    --shadcn-chart-1: oklch(0.488 0.243 264.376);
    --shadcn-chart-2: oklch(0.696 0.17 162.48);
    --shadcn-chart-3: oklch(0.769 0.188 70.08);
    --shadcn-chart-4: oklch(0.627 0.265 303.9);
    --shadcn-chart-5: oklch(0.645 0.246 16.439);
    --shadcn-sidebar: oklch(0.205 0 0);
    --shadcn-sidebar-foreground: oklch(0.985 0 0);
    --shadcn-sidebar-primary: oklch(0.488 0.243 264.376);
    --shadcn-sidebar-primary-foreground: oklch(0.985 0 0);
    --shadcn-sidebar-accent: oklch(0.269 0 0);
    --shadcn-sidebar-accent-foreground: oklch(0.985 0 0);
    --shadcn-sidebar-border: oklch(1 0 0 / 10%);
    --shadcn-sidebar-ring: oklch(0.556 0 0);
    --shadcn-shadow-xs: 0 1px 2px rgb(0 0 0 / 0.24);
    --shadcn-shadow-sm: 0 1px 2px rgb(0 0 0 / 0.28), 0 0 0 1px rgb(255 255 255 / 0.06);
    --shadcn-shadow-md: 0 8px 24px rgb(0 0 0 / 0.36), 0 0 0 1px rgb(255 255 255 / 0.08);
}

.shadcn-scope,
.shadcn-overlay-scope {
    box-sizing: border-box;
    font-family: var(--shadcn-font-sans);
    color: var(--shadcn-foreground);
}

.shadcn-scope *,
.shadcn-scope *::before,
.shadcn-scope *::after,
.shadcn-overlay-scope,
.shadcn-overlay-scope *,
.shadcn-overlay-scope *::before,
.shadcn-overlay-scope *::after {
    box-sizing: border-box;
}

@media (pointer: coarse) {
    .shadcn-scope :where(button, [role="button"], input, select, textarea),
    .shadcn-overlay-scope :where(button, [role="button"], input, select, textarea) {
        min-height: 2.75rem;
    }
}

@media (prefers-reduced-motion: reduce) {
    .shadcn-scope *,
    .shadcn-overlay-scope * {
        scroll-behavior: auto !important;
        animation-duration: 0.01ms !important;
        animation-iteration-count: 1 !important;
        transition-duration: 0.01ms !important;
    }
}

@media (forced-colors: active) {
    .shadcn-scope :focus-visible,
    .shadcn-overlay-scope :focus-visible {
        outline: 2px solid Highlight;
        outline-offset: 2px;
    }
}
```

- [ ] **Step 5: Run focused and project tests**

```powershell
dotnet build .\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj -c Release
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
```

Expected: build has 0 warnings/errors; all package tests pass.

- [ ] **Step 6: Commit the token layer**

```powershell
git add -- Maliev.ShadcnBlazor/wwwroot/css/shadcn-base.css Maliev.ShadcnBlazor/Theming/ShadcnCss.cs Maliev.ShadcnBlazor.Tests/Contracts/TokenContractTests.cs
git commit -m "style: add pinned shadcn semantic tokens"
```

### Task 3: Add options, MudTheme mapping, and service registration

**Files:**
- Create: `Maliev.ShadcnBlazor/Theming/ShadcnDirection.cs`
- Create: `Maliev.ShadcnBlazor/Theming/ShadcnOptions.cs`
- Create: `Maliev.ShadcnBlazor/Theming/ShadcnThemeFactory.cs`
- Create: `Maliev.ShadcnBlazor/ServiceCollectionExtensions.cs`
- Create: `Maliev.ShadcnBlazor.Tests/Theming/ShadcnThemeFactoryTests.cs`
- Create: `Maliev.ShadcnBlazor.Tests/Theming/ServiceCollectionExtensionsTests.cs`

**Interfaces:**
- Consumes: `ShadcnCss.OverlayScopeClass`, MudBlazor 9.7.0.
- Produces: `ShadcnDirection`, `ShadcnOptions`, `ShadcnThemeFactory.Create(ShadcnOptions)`, and `IServiceCollection.AddMalievShadcn(Action<ShadcnOptions>?)`.

- [ ] **Step 1: Write failing theme and registration tests**

Create `Maliev.ShadcnBlazor.Tests/Theming/ShadcnThemeFactoryTests.cs`:

```csharp
using Maliev.ShadcnBlazor.Theming;
using MudBlazor.Utilities;

namespace Maliev.ShadcnBlazor.Tests.Theming;

public sealed class ShadcnThemeFactoryTests
{
    [Fact]
    public void CreateMapsPinnedNeutralPalettesAndConsumerFont()
    {
        var theme = ShadcnThemeFactory.Create(new ShadcnOptions
        {
            FontFamily = "IBM Plex Sans Thai, sans-serif"
        });

        Assert.Equal(new MudColor("#171717"), theme.PaletteLight.Primary);
        Assert.Equal(new MudColor("#ffffff"), theme.PaletteLight.Background);
        Assert.Equal(new MudColor("#e4e4e7"), theme.PaletteDark.Primary);
        Assert.Equal(new MudColor("#252525"), theme.PaletteDark.Background);
        Assert.Equal("IBM Plex Sans Thai, sans-serif", theme.Typography.Default.FontFamily.Single());
        Assert.Equal("IBM Plex Sans Thai, sans-serif", theme.Typography.Button.FontFamily.Single());
    }
}
```

Create `Maliev.ShadcnBlazor.Tests/Theming/ServiceCollectionExtensionsTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify missing-type failures**

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release
```

Expected: compilation FAIL because `ShadcnOptions`, `ShadcnThemeFactory`, and `AddMalievShadcn` are undefined.

- [ ] **Step 3: Add the public options and direction enum**

Create `Maliev.ShadcnBlazor/Theming/ShadcnDirection.cs`:

```csharp
namespace Maliev.ShadcnBlazor.Theming;

public enum ShadcnDirection
{
    LeftToRight,
    RightToLeft
}
```

Create `Maliev.ShadcnBlazor/Theming/ShadcnOptions.cs`:

```csharp
namespace Maliev.ShadcnBlazor.Theming;

public sealed class ShadcnOptions
{
    public string FontFamily { get; set; } = "ui-sans-serif, system-ui, sans-serif";
    public bool DefaultDarkMode { get; set; }
    public ShadcnDirection DefaultDirection { get; set; } = ShadcnDirection.LeftToRight;
}
```

- [ ] **Step 4: Add the canonical MudTheme factory**

Create `Maliev.ShadcnBlazor/Theming/ShadcnThemeFactory.cs` with one `Create` method. Use the exact palette shown below and set Default, H1-H6, Body1, Body2, Button, Caption, and Subtitle1/2 `FontFamily` to one array containing `options.FontFamily`:

```csharp
using MudBlazor;

namespace Maliev.ShadcnBlazor.Theming;

public static class ShadcnThemeFactory
{
    public static MudTheme Create(ShadcnOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var fonts = new[] { options.FontFamily };
        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = "#171717", PrimaryDarken = "#0a0a0a", PrimaryLighten = "#404040",
                Secondary = "#f5f5f5", SecondaryDarken = "#e5e5e5", SecondaryLighten = "#fafafa",
                Background = "#ffffff", Surface = "#ffffff", TextPrimary = "#171717",
                TextSecondary = "#737373", AppbarBackground = "#ffffff", AppbarText = "#171717",
                DrawerBackground = "#ffffff", DrawerText = "#171717"
            },
            PaletteDark = new PaletteDark
            {
                Primary = "#e4e4e7", PrimaryDarken = "#d4d4d8", PrimaryLighten = "#fafafa",
                Secondary = "#3f3f46", SecondaryDarken = "#27272a", SecondaryLighten = "#52525b",
                Background = "#252525", Surface = "#333333", TextPrimary = "#fafafa",
                TextSecondary = "#a3a3a3", AppbarBackground = "#333333", AppbarText = "#fafafa",
                DrawerBackground = "#333333", DrawerText = "#fafafa"
            },
            Typography = new Typography
            {
                Default = new DefaultTypography { FontFamily = fonts },
                H1 = new H1Typography { FontFamily = fonts },
                H2 = new H2Typography { FontFamily = fonts },
                H3 = new H3Typography { FontFamily = fonts },
                H4 = new H4Typography { FontFamily = fonts },
                H5 = new H5Typography { FontFamily = fonts },
                H6 = new H6Typography { FontFamily = fonts },
                Body1 = new Body1Typography { FontFamily = fonts },
                Body2 = new Body2Typography { FontFamily = fonts },
                Button = new ButtonTypography { FontFamily = fonts },
                Caption = new CaptionTypography { FontFamily = fonts },
                Subtitle1 = new Subtitle1Typography { FontFamily = fonts },
                Subtitle2 = new Subtitle2Typography { FontFamily = fonts }
            }
        };
    }
}
```

- [ ] **Step 5: Add the single registration seam**

Create `Maliev.ShadcnBlazor/ServiceCollectionExtensions.cs`:

```csharp
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
```

- [ ] **Step 6: Run focused and package suites**

```powershell
dotnet build .\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj -c Release
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
```

Expected: build has 0 warnings/errors; all package tests pass.

- [ ] **Step 7: Commit theme registration**

```powershell
git add -- Maliev.ShadcnBlazor/Theming Maliev.ShadcnBlazor/ServiceCollectionExtensions.cs Maliev.ShadcnBlazor.Tests/Theming
git commit -m "feat: add shadcn theme registration"
```

### Task 4: Add the opt-in provider and portal scope

**Files:**
- Create: `Maliev.ShadcnBlazor/Theming/ShadcnContext.cs`
- Create: `Maliev.ShadcnBlazor/Components/ShadcnThemeProvider.razor`
- Create: `Maliev.ShadcnBlazor.Tests/Components/ShadcnThemeProviderTests.cs`

**Interfaces:**
- Consumes: `ShadcnOptions`, `ShadcnThemeFactory`, `ShadcnCss`, Mud provider services.
- Produces: `<ShadcnThemeProvider IsDarkMode Direction Class AdditionalAttributes>`, cascading `ShadcnContext(bool IsDarkMode, ShadcnDirection Direction)`, and provider-backed portal scope.

- [ ] **Step 1: Write the failing provider tests**

Create `Maliev.ShadcnBlazor.Tests/Components/ShadcnThemeProviderTests.cs`:

```csharp
using Bunit;
using Maliev.ShadcnBlazor.Components;
using Maliev.ShadcnBlazor.Theming;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Maliev.ShadcnBlazor.Tests.Components;

public sealed class ShadcnThemeProviderTests : BunitContext
{
    public ShadcnThemeProviderTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMalievShadcn();
    }

    [Fact]
    public void RendersScopedDarkRtlRootAndAllMudProviders()
    {
        var cut = Render<ShadcnThemeProvider>(parameters => parameters
            .Add(x => x.IsDarkMode, true)
            .Add(x => x.Direction, ShadcnDirection.RightToLeft)
            .Add(x => x.Class, "consumer-shell")
            .AddChildContent("content"));

        var root = cut.Find("[data-shadcn-scope]");
        Assert.Contains(ShadcnCss.ScopeClass, root.ClassList);
        Assert.Contains("consumer-shell", root.ClassList);
        Assert.Equal("dark", root.GetAttribute("data-shadcn-theme"));
        Assert.Equal("rtl", root.GetAttribute("dir"));
        Assert.Equal("content", root.TextContent);
        Assert.True(cut.FindComponent<MudThemeProvider>().Instance.IsDarkMode);
        Assert.Equal(ShadcnCss.OverlayScopeClass,
            cut.FindComponent<MudDialogProvider>().Instance.BackgroundClass);
        cut.FindComponent<MudPopoverProvider>();
        Assert.True(cut.FindComponent<MudSnackbarProvider>().Instance.RightToLeft);
    }

    [Fact]
    public void CascadesTheCurrentThemeAndDirection()
    {
        ShadcnContext? observed = null;
        var cut = Render<ShadcnThemeProvider>(parameters => parameters
            .Add(x => x.Direction, ShadcnDirection.LeftToRight)
            .AddChildContent<CaptureContext>(child => child.Add(x => x.OnCaptured, value => observed = value)));
        Assert.Equal(new ShadcnContext(false, ShadcnDirection.LeftToRight), observed);
    }

    private sealed class CaptureContext : ComponentBase
    {
        [CascadingParameter] public ShadcnContext Context { get; set; }
        [Parameter] public Action<ShadcnContext>? OnCaptured { get; set; }
        protected override void OnParametersSet() => OnCaptured?.Invoke(Context);
    }
}
```

- [ ] **Step 2: Run the test to verify missing-component failures**

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release `
  --filter FullyQualifiedName~ShadcnThemeProviderTests
```

Expected: compilation FAIL because `ShadcnThemeProvider` and `ShadcnContext` do not exist.

- [ ] **Step 3: Add the context record**

Create `Maliev.ShadcnBlazor/Theming/ShadcnContext.cs`:

```csharp
namespace Maliev.ShadcnBlazor.Theming;

public readonly record struct ShadcnContext(bool IsDarkMode, ShadcnDirection Direction);
```

- [ ] **Step 4: Implement the provider**

Create `Maliev.ShadcnBlazor/Components/ShadcnThemeProvider.razor`:

```razor
@using Microsoft.Extensions.Options
@inject IOptions<ShadcnOptions> ConfiguredOptions

<MudThemeProvider Theme="@_theme" IsDarkMode="@IsDarkMode" />
<div class="@RootClass"
     data-shadcn-scope
     data-shadcn-theme="@(IsDarkMode ? "dark" : "light")"
     dir="@(Direction == ShadcnDirection.RightToLeft ? "rtl" : "ltr")"
     @attributes="AdditionalAttributes">
    <MudPopoverProvider />
    <MudDialogProvider BackgroundClass="@ShadcnCss.OverlayScopeClass" />
    <MudSnackbarProvider RightToLeft="@(Direction == ShadcnDirection.RightToLeft)" />
    <CascadingValue Value="@Context">
        @ChildContent
    </CascadingValue>
</div>

@code {
    private MudTheme _theme = null!;

    [Parameter] public bool IsDarkMode { get; set; }
    [Parameter] public ShadcnDirection Direction { get; set; } = ShadcnDirection.LeftToRight;
    [Parameter] public string? Class { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private ShadcnContext Context => new(IsDarkMode, Direction);
    private string RootClass => string.IsNullOrWhiteSpace(Class)
        ? ShadcnCss.ScopeClass
        : $"{ShadcnCss.ScopeClass} {Class}";

    protected override void OnInitialized() =>
        _theme = ShadcnThemeFactory.Create(ConfiguredOptions.Value);
}
```

- [ ] **Step 5: Run focused and package suites**

```powershell
dotnet build .\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj -c Release
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
```

Expected: build has 0 warnings/errors; all package tests pass.

- [ ] **Step 6: Commit the provider**

```powershell
git add -- Maliev.ShadcnBlazor/Components/ShadcnThemeProvider.razor Maliev.ShadcnBlazor/Theming/ShadcnContext.cs Maliev.ShadcnBlazor.Tests/Components/ShadcnThemeProviderTests.cs
git commit -m "feat: add scoped shadcn provider"
```

### Task 5: Build the deterministic showcase shell

**Files:**
- Create: `Maliev.ShadcnBlazor.Showcase/Maliev.ShadcnBlazor.Showcase.csproj`
- Create: `Maliev.ShadcnBlazor.Showcase/Program.cs`
- Create: `Maliev.ShadcnBlazor.Showcase/App.razor`
- Create: `Maliev.ShadcnBlazor.Showcase/_Imports.razor`
- Create: `Maliev.ShadcnBlazor.Showcase/ShowcaseState.cs`
- Create: `Maliev.ShadcnBlazor.Showcase/Layout/MainLayout.razor`
- Create: `Maliev.ShadcnBlazor.Showcase/Pages/Home.razor`
- Create: `Maliev.ShadcnBlazor.Showcase/Pages/Foundation.razor`
- Create: `Maliev.ShadcnBlazor.Showcase/wwwroot/index.html`
- Modify: `Legacy.Maliev.Intranet.slnx`

**Interfaces:**
- Consumes: `AddMalievShadcn`, `ShadcnThemeProvider`, static token asset.
- Produces: deterministic routes `/` and `/components/foundation`; test IDs `showcase-title`, `theme-toggle`, `direction-toggle`, `foundation-fixture`, and `token-*`.

- [ ] **Step 1: Create the standalone WASM project and add it to the solution**

Create `Maliev.ShadcnBlazor.Showcase/Maliev.ShadcnBlazor.Showcase.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.3" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="10.0.3" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj" />
  </ItemGroup>
</Project>
```

```powershell
dotnet solution .\Legacy.Maliev.Intranet.slnx add .\Maliev.ShadcnBlazor.Showcase\Maliev.ShadcnBlazor.Showcase.csproj
```

- [ ] **Step 2: Add program, imports, router, and state**

Create `Program.cs`:

```csharp
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
```

Create `_Imports.razor`:

```razor
@using Maliev.ShadcnBlazor
@using Maliev.ShadcnBlazor.Components
@using Maliev.ShadcnBlazor.Showcase
@using Maliev.ShadcnBlazor.Showcase.Layout
@using Maliev.ShadcnBlazor.Theming
@using Microsoft.AspNetCore.Components.Routing
@using MudBlazor
```

Create `App.razor`:

```razor
<Router AppAssembly="@typeof(Program).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
    </Found>
    <NotFound><LayoutView Layout="@typeof(MainLayout)"><p>Fixture not found.</p></LayoutView></NotFound>
</Router>
```

Create `ShowcaseState.cs`:

```csharp
using Maliev.ShadcnBlazor.Theming;

namespace Maliev.ShadcnBlazor.Showcase;

public sealed class ShowcaseState
{
    public bool IsDarkMode { get; private set; }
    public ShadcnDirection Direction { get; private set; } = ShadcnDirection.LeftToRight;
    public event EventHandler? Changed;
    public void ToggleTheme() { IsDarkMode = !IsDarkMode; Changed?.Invoke(this, EventArgs.Empty); }
    public void ToggleDirection()
    {
        Direction = Direction == ShadcnDirection.LeftToRight
            ? ShadcnDirection.RightToLeft : ShadcnDirection.LeftToRight;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
```

- [ ] **Step 3: Add the provider layout and deterministic controls**

Create `Layout/MainLayout.razor`:

```razor
@inherits LayoutComponentBase
@inject ShowcaseState State
@implements IDisposable

<ShadcnThemeProvider IsDarkMode="@State.IsDarkMode" Direction="@State.Direction" Class="showcase-root">
    <header>
        <strong data-testid="showcase-title">Maliev Shadcn Blazor</strong>
        <MudButton data-testid="theme-toggle" Variant="Variant.Outlined" OnClick="State.ToggleTheme">Toggle theme</MudButton>
        <MudButton data-testid="direction-toggle" Variant="Variant.Outlined" OnClick="State.ToggleDirection">Toggle direction</MudButton>
        <MudLink Href="/components/foundation">Foundation</MudLink>
    </header>
    <main>@Body</main>
</ShadcnThemeProvider>

@code {
    protected override void OnInitialized() => State.Changed += OnChanged;
    private void OnChanged(object? sender, EventArgs args) => _ = InvokeAsync(StateHasChanged);
    public void Dispose() => State.Changed -= OnChanged;
}
```

Create `Pages/Home.razor`:

```razor
@page "/"
<PageTitle>Shadcn Blazor Showcase</PageTitle>
<h1>Shadcn Blazor Showcase</h1>
<p>Deterministic component and state fixtures.</p>
```

Create `Pages/Foundation.razor`:

```razor
@page "/components/foundation"
<PageTitle>Foundation Fixture</PageTitle>
<section data-testid="foundation-fixture">
    <h1>Foundation</h1>
    <div data-testid="token-background" style="background:var(--shadcn-background);color:var(--shadcn-foreground)">Background</div>
    <div data-testid="token-primary" style="background:var(--shadcn-primary);color:var(--shadcn-primary-foreground)">Primary</div>
    <div data-testid="token-muted" style="background:var(--shadcn-muted);color:var(--shadcn-muted-foreground)">Muted</div>
    <div data-testid="token-destructive" style="background:var(--shadcn-destructive);color:var(--shadcn-destructive-foreground)">Destructive</div>
    <div data-testid="radius-md" style="width:9rem;height:3rem;border:1px solid var(--shadcn-border);border-radius:var(--shadcn-radius-md)">Radius</div>
</section>
```

- [ ] **Step 4: Add the exact stylesheet/script order**

Create `wwwroot/index.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Shadcn Blazor Showcase</title>
    <base href="/" />
    <link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
    <link href="_content/Maliev.ShadcnBlazor/css/shadcn-base.css" rel="stylesheet" />
</head>
<body>
    <div id="app">Loading showcase…</div>
    <div id="blazor-error-ui" hidden role="alert">The showcase encountered an unexpected error.</div>
    <script src="_content/MudBlazor/MudBlazor.min.js"></script>
    <script src="_framework/blazor.webassembly.js"></script>
</body>
</html>
```

Do not add external fonts, analytics, authentication, or Intranet assets.

- [ ] **Step 5: Build and run the showcase smoke route**

```powershell
dotnet build .\Maliev.ShadcnBlazor.Showcase\Maliev.ShadcnBlazor.Showcase.csproj -c Release
dotnet run --project .\Maliev.ShadcnBlazor.Showcase\Maliev.ShadcnBlazor.Showcase.csproj -c Release --no-build --urls http://127.0.0.1:5199
```

Expected: build has 0 warnings/errors; `/components/foundation` renders the five deterministic token fixtures without console errors. Stop the host after inspection.

- [ ] **Step 6: Commit the showcase shell**

```powershell
git add -- Legacy.Maliev.Intranet.slnx Maliev.ShadcnBlazor.Showcase
git commit -m "feat: add shadcn component showcase"
```

### Task 6: Add browser smoke and computed-style validation

**Files:**
- Create: `Maliev.ShadcnBlazor.BrowserTests/Maliev.ShadcnBlazor.BrowserTests.csproj`
- Create: `Maliev.ShadcnBlazor.BrowserTests/Infrastructure/ShowcaseServerFixture.cs`
- Create: `Maliev.ShadcnBlazor.BrowserTests/Infrastructure/PlaywrightFixture.cs`
- Create: `Maliev.ShadcnBlazor.BrowserTests/Infrastructure/BrowserCollection.cs`
- Create: `Maliev.ShadcnBlazor.BrowserTests/FoundationSmokeTests.cs`
- Create: `scripts/install-shadcn-browser.ps1`
- Modify: `Legacy.Maliev.Intranet.slnx`

**Interfaces:**
- Consumes: Built showcase route and deterministic test IDs.
- Produces: collection `Shadcn browser`, `ShowcaseServerFixture.BaseUri`, `PlaywrightFixture.Browser`, and a repeatable Chromium foundation check.

- [ ] **Step 1: Create the browser-test project and installer**

Create `Maliev.ShadcnBlazor.BrowserTests/Maliev.ShadcnBlazor.BrowserTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><IsPackable>false</IsPackable></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Microsoft.Playwright.Xunit" Version="1.61.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>
  <ItemGroup><Using Include="Xunit" /></ItemGroup>
</Project>
```

Create `scripts/install-shadcn-browser.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj'
dotnet build $project -c Release
$installer = Join-Path $root 'Maliev.ShadcnBlazor.BrowserTests\bin\Release\net10.0\playwright.ps1'
& pwsh $installer install chromium
if ($LASTEXITCODE -ne 0) { throw "Playwright Chromium installation failed with exit code $LASTEXITCODE." }
```

Add the project:

```powershell
dotnet solution .\Legacy.Maliev.Intranet.slnx add .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj
```

- [ ] **Step 2: Add the isolated showcase server fixture**

Create `Infrastructure/ShowcaseServerFixture.cs` with this complete lifecycle:

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

public sealed class ShowcaseServerFixture : IAsyncLifetime
{
    private Process? _process;
    public Uri BaseUri { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        BaseUri = new Uri($"http://127.0.0.1:{port}");

        var root = FindRoot();
        var project = Path.Combine(root, "Maliev.ShadcnBlazor.Showcase", "Maliev.ShadcnBlazor.Showcase.csproj");
        _process = Process.Start(new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            Arguments = $"run --project \"{project}\" -c Release --no-build --urls {BaseUri}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start the showcase host.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
                throw new InvalidOperationException(await _process.StandardError.ReadToEndAsync());
            try
            {
                using var response = await http.GetAsync(BaseUri);
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(250);
        }
        throw new TimeoutException($"Showcase did not become ready at {BaseUri}.");
    }

    public async Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }
        _process?.Dispose();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
```

- [ ] **Step 3: Add Playwright lifetime and collection**

Create `Infrastructure/PlaywrightFixture.cs`:

```csharp
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

public sealed class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    public IBrowser Browser { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }
    public async Task DisposeAsync()
    {
        await Browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
```

Create `Infrastructure/BrowserCollection.cs`:

```csharp
namespace Maliev.ShadcnBlazor.BrowserTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<ShowcaseServerFixture>, ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "Shadcn browser";
}
```

- [ ] **Step 4: Write the browser smoke test**

Create `FoundationSmokeTests.cs`:

```csharp
using Maliev.ShadcnBlazor.BrowserTests.Infrastructure;
using Microsoft.Playwright;

namespace Maliev.ShadcnBlazor.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class FoundationSmokeTests(ShowcaseServerFixture server, PlaywrightFixture playwright)
{
    [Fact]
    public async Task FoundationFixtureHasHealthyConsoleAndSwitchesThemeAndDirection()
    {
        var errors = new List<string>();
        await using var context = await playwright.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            DeviceScaleFactor = 1
        });
        var page = await context.NewPageAsync();
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync(new Uri(server.BaseUri, "/components/foundation").ToString());
        await page.GetByTestId("foundation-fixture").WaitForAsync();
        Assert.Equal("Foundation Fixture", await page.TitleAsync());
        Assert.Empty(errors);

        var root = page.Locator("[data-shadcn-scope]");
        Assert.Equal("light", await root.GetAttributeAsync("data-shadcn-theme"));
        Assert.Equal("ltr", await root.GetAttributeAsync("dir"));
        var background = await page.GetByTestId("token-background").EvaluateAsync<string>(
            "element => getComputedStyle(element).backgroundColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", background);

        await page.GetByTestId("theme-toggle").ClickAsync();
        await Assertions.Expect(root).ToHaveAttributeAsync("data-shadcn-theme", "dark");
        await page.GetByTestId("direction-toggle").ClickAsync();
        await Assertions.Expect(root).ToHaveAttributeAsync("dir", "rtl");

        var evidence = Path.Combine(Path.GetTempPath(), "maliev-shadcn-foundation.png");
        await page.ScreenshotAsync(new() { Path = evidence, FullPage = false });
        Assert.True(File.Exists(evidence));
    }
}
```

- [ ] **Step 5: Install Chromium, build first, and run browser smoke**

```powershell
pwsh .\scripts\install-shadcn-browser.ps1
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj `
  -c Release --no-build --no-restore
```

Expected: build has 0 warnings/errors; one browser test passes; the temporary screenshot exists outside the repository; no browser process remains.

- [ ] **Step 6: Commit the browser harness**

```powershell
git add -- Legacy.Maliev.Intranet.slnx Maliev.ShadcnBlazor.BrowserTests scripts/install-shadcn-browser.ps1
git commit -m "test: add shadcn browser validation harness"
```

### Task 7: Add online upstream verification

**Files:**
- Create: `scripts/verify-shadcn-reference.ps1`
- Create: `Maliev.ShadcnBlazor.Tests/Contracts/ReferenceVerifierScriptTests.cs`

**Interfaces:**
- Consumes: `shadcn-reference/v1` manifest.
- Produces: `pwsh scripts/verify-shadcn-reference.ps1`, which exits zero only when all 61 registry files and the Vega style blob match the pinned commit.

- [ ] **Step 1: Write the failing script contract**

Create `ReferenceVerifierScriptTests.cs`:

```csharp
namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class ReferenceVerifierScriptTests
{
    [Fact]
    public void VerifierUsesPinnedCommitAndFailsOnMismatch()
    {
        var script = File.ReadAllText(FindScript());
        Assert.Contains("shadcn-reference.json", script, StringComparison.Ordinal);
        Assert.Contains("api.github.com/repos/shadcn-ui/ui/contents", script, StringComparison.Ordinal);
        Assert.Contains("throw", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ref=main", script, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "scripts", "verify-shadcn-reference.ps1");
    }
}
```

- [ ] **Step 2: Run the test to verify missing-script failure**

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release `
  --filter FullyQualifiedName~ReferenceVerifierScriptTests
```

Expected: FAIL because `scripts/verify-shadcn-reference.ps1` does not exist.

- [ ] **Step 3: Implement the online verifier**

Create `scripts/verify-shadcn-reference.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root 'Maliev.ShadcnBlazor\Reference\shadcn-reference.json'
$manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
$headers = @{ 'User-Agent' = 'Maliev-Shadcn-reference-verifier' }
$apiRoot = 'https://api.github.com/repos/shadcn-ui/ui/contents'
$failures = [System.Collections.Generic.List[string]]::new()

$encodedRegistryRoot = ($manifest.registryRoot -split '/' | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/'
$registry = Invoke-RestMethod -Headers $headers -Uri "$apiRoot/$encodedRegistryRoot`?ref=$($manifest.commit)"
$registryByName = @{}
foreach ($item in $registry) { $registryByName[$item.name] = $item }

foreach ($component in $manifest.components | Where-Object sourceKind -eq 'registry-file') {
    $name = "$($component.slug).tsx"
    $actual = $registryByName[$name]
    if ($null -eq $actual) {
        $failures.Add("$($component.name): $name is absent from the pinned Base registry")
        continue
    }
    if ($actual.sha -ne $component.blobSha) {
        $failures.Add("$($component.name): expected $($component.blobSha), received $($actual.sha)")
    }
}

$stylePath = $manifest.styleSource.path
$encodedStylePath = ($stylePath -split '/' | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/'
$actualStyle = Invoke-RestMethod -Headers $headers -Uri "$apiRoot/$encodedStylePath`?ref=$($manifest.commit)"
if ($actualStyle.sha -ne $manifest.styleSource.blobSha) {
    $failures.Add("Vega style: expected $($manifest.styleSource.blobSha), received $($actualStyle.sha)")
}

if ($failures.Count -gt 0) { throw "Pinned Shadcn reference mismatch:`n$($failures -join "`n")" }
Write-Host "Verified 61 Base registry files and Vega style at $($manifest.commit)."
```

- [ ] **Step 4: Run offline contract, online verification, and package suite**

```powershell
dotnet build .\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj -c Release
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
pwsh .\scripts\verify-shadcn-reference.ps1
```

Expected: build has 0 warnings/errors; all package tests pass; verifier prints `Verified 61 Base registry files and Vega style` with the pinned commit.

- [ ] **Step 5: Commit source verification**

```powershell
git add -- scripts/verify-shadcn-reference.ps1 Maliev.ShadcnBlazor.Tests/Contracts/ReferenceVerifierScriptTests.cs
git commit -m "build: verify pinned shadcn reference"
```

### Task 8: Finish package metadata, licenses, and consumer documentation

**Files:**
- Create: `Maliev.ShadcnBlazor/README.md`
- Create: `Maliev.ShadcnBlazor/licenses/shadcn-ui-LICENSE.md`
- Create: `Maliev.ShadcnBlazor/licenses/MudBlazor-LICENSE`
- Create: `Maliev.ShadcnBlazor.Tests/Contracts/PackageContractTests.cs`
- Modify: `Maliev.ShadcnBlazor/Maliev.ShadcnBlazor.csproj`

**Interfaces:**
- Consumes: final foundation registration and asset APIs.
- Produces: inspectable NuGet package containing README, licenses, CSS, reference manifest, and library assembly.

- [ ] **Step 1: Write the failing package contract**

Create `PackageContractTests.cs`:

```csharp
using System.IO.Compression;

namespace Maliev.ShadcnBlazor.Tests.Contracts;

public sealed class PackageContractTests
{
    [Fact]
    public void NupkgContainsReadmeLicensesTokensAndReferenceManifest()
    {
        var output = Path.Combine(FindRoot(), ".artifacts", "packages");
        var package = Directory.GetFiles(output, "Maliev.ShadcnBlazor.*.nupkg", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .First();
        using var archive = ZipFile.OpenRead(package);
        var names = archive.Entries.Select(x => x.FullName).ToArray();
        Assert.Contains("README.md", names);
        Assert.Contains("licenses/shadcn-ui-LICENSE.md", names);
        Assert.Contains("licenses/MudBlazor-LICENSE", names);
        Assert.Contains("reference/shadcn-reference.json", names);
        Assert.Contains("staticwebassets/css/shadcn-base.css", names);
        Assert.Contains(names, x => x.EndsWith("lib/net10.0/Maliev.ShadcnBlazor.dll", StringComparison.Ordinal));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Intranet.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
```

- [ ] **Step 2: Pack and run the test to verify missing metadata failure**

```powershell
dotnet pack .\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj -c Release -o .\.artifacts\packages
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release `
  --filter FullyQualifiedName~PackageContractTests
```

Expected: FAIL because README and license entries are absent.

- [ ] **Step 3: Add exact upstream licenses**

Retrieve the license texts from the pinned sources, inspect them, then add them with `apply_patch`:

```powershell
$shadcnLicense = Invoke-RestMethod `
  'https://raw.githubusercontent.com/shadcn-ui/ui/6261bd89f72d794aea491482cc2acfd8dc3d63e2/LICENSE.md'
$mudBlazorLicense = Invoke-RestMethod `
  'https://raw.githubusercontent.com/MudBlazor/MudBlazor/v9.7.0/LICENSE'
$shadcnLicense
$mudBlazorLicense
```

Create both tracked license files with `apply_patch` using the printed texts. Their content must be byte-for-byte equivalent to the retrieved texts after normalizing line endings to LF.

- [ ] **Step 4: Add the consumer README**

Create `Maliev.ShadcnBlazor/README.md` with these executable setup instructions:

````markdown
# Maliev.ShadcnBlazor

Reusable Shadcn Base/Vega/Neutral components for .NET 10 Blazor, backed by MudBlazor 9.7.0.

## Register

```csharp
builder.Services.AddMalievShadcn(options =>
    options.FontFamily = "IBM Plex Sans Thai, sans-serif");
```

## Load assets

```html
<link href="_content/MudBlazor/MudBlazor.min.css" rel="stylesheet" />
<link href="_content/Maliev.ShadcnBlazor/css/shadcn-base.css" rel="stylesheet" />
<script src="_content/MudBlazor/MudBlazor.min.js"></script>
```

## Provide theme and portals

```razor
<ShadcnThemeProvider IsDarkMode="@isDarkMode" Direction="ShadcnDirection.LeftToRight">
    @Body
</ShadcnThemeProvider>
```

Do not also render MudThemeProvider, MudPopoverProvider, MudDialogProvider, or MudSnackbarProvider in the same application root.
````

- [ ] **Step 5: Include README and licenses in the package**

Add to the RCL property group:

```xml
<PackageReadmeFile>README.md</PackageReadmeFile>
```

Add to its pack item group:

```xml
<None Update="README.md" Pack="true" PackagePath="\" />
<None Update="licenses\shadcn-ui-LICENSE.md" Pack="true" PackagePath="licenses\" />
<None Update="licenses\MudBlazor-LICENSE" Pack="true" PackagePath="licenses\" />
```

- [ ] **Step 6: Build, pack, inspect, and run package tests**

Delete only the package output directory after verifying it resolves under `.artifacts`, then regenerate it:

```powershell
$output = (Resolve-Path '.\.artifacts').Path + '\packages'
if (-not $output.StartsWith((Resolve-Path '.').Path, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package output path.' }
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet pack .\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj -c Release --no-build -o $output
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
```

Expected: solution build has 0 warnings/errors; package is created; all package tests including `PackageContractTests` pass.

- [ ] **Step 7: Commit package documentation and metadata**

```powershell
git add -- Maliev.ShadcnBlazor/Maliev.ShadcnBlazor.csproj Maliev.ShadcnBlazor/README.md Maliev.ShadcnBlazor/licenses Maliev.ShadcnBlazor.Tests/Contracts/PackageContractTests.cs
git commit -m "docs: document shadcn blazor package"
```

### Task 9: Run the Slice 1 completion gate

**Files:**
- Modify only if validation exposes a foundation defect: files owned by Tasks 1-8.

**Interfaces:**
- Consumes: All Slice 1 outputs.
- Produces: Fresh build, test, source-verification, browser, pack, and repository-boundary evidence suitable for starting Plan 2.

- [ ] **Step 1: Verify source and diff hygiene**

```powershell
git status --short
git diff --check
pwsh .\scripts\verify-shadcn-reference.ps1
```

Expected: worktree is clean; diff check emits nothing; all 61 files plus the Vega style are verified.

- [ ] **Step 2: Build the full affected solution first**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
```

Expected: 0 warnings and 0 errors.

- [ ] **Step 3: Run focused package and browser suites**

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
dotnet test .\Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj -c Release --no-build --no-restore
```

Expected: every package test passes; the Chromium foundation fixture passes with no console errors and produces screenshot evidence outside the repository.

- [ ] **Step 4: Run the affected Intranet suite**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore
```

Expected: all existing Intranet tests pass. A new package project in the solution must not change current behavior.

- [ ] **Step 5: Pack and inspect the final artifact**

```powershell
dotnet pack .\Maliev.ShadcnBlazor\Maliev.ShadcnBlazor.csproj -c Release --no-build -o .\.artifacts\packages
tar -tf (Get-ChildItem .\.artifacts\packages\Maliev.ShadcnBlazor.*.nupkg | Select-Object -First 1).FullName
```

Expected: package contains `lib/net10.0/Maliev.ShadcnBlazor.dll`, `staticwebassets/css/shadcn-base.css`, README, both licenses, and the reference manifest; it contains no Intranet assembly, DTO, resource, secret, temporary screenshot, or browser binary.

- [ ] **Step 6: Record final evidence and keep the branch clean**

Capture the exact command results and pass counts in the task handoff. Run:

```powershell
git status --short
git log --oneline -8
```

Expected: worktree is clean; the Slice 1 commits are visible in task order. Do not create an empty summary commit and do not push.

## Slice 1 completion criteria

Slice 1 is complete only when:

- the pinned manifest contains 64 unique components, 61 registry files, and 3 compositions;
- the online verifier proves every registry/style blob at the pinned commit;
- the RCL, tests, showcase, and browser projects are in the solution and build with zero warnings/errors;
- package tests prove tokens, theme mapping, registration, provider scope, catalog, licenses, and package contents;
- Chromium proves the foundation fixture renders, switches theme and direction, has healthy console output, and captures evidence;
- the existing complete Intranet test project passes without production changes;
- the NuGet package contains only the intended reusable assets and metadata;
- all commits are scoped, the worktree is clean, and nothing has been pushed.
