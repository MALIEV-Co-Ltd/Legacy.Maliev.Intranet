# MALIEV Login Branded Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a full-viewport, responsive MALIEV branded split login that preserves existing authentication contracts while fixing logo geometry, Google outage behavior, loading feedback, spacing, contrast, touch targets, and overflow.

**Architecture:** Keep authentication logic in `Login.razor` and Google behavior in the existing interop module. Restructure only presentation into brand and authentication regions, with page-owned CSS controlling the split and `EmptyLayout` stretching its body. Use existing Shadcn tokens and localized resources; prove source contracts first, then rendered behavior against the snapshot-backed Aspire stack.

**Tech Stack:** .NET 10, Blazor WebAssembly, MudBlazor 9.7, Maliev.ShadcnBlazor, Razor localization, vanilla JavaScript, CSS custom properties, xUnit, Codex in-app browser.

## Global Constraints

- Preserve `EmployeeAuthenticationClient`, same-origin BFF calls, return URL validation, local `.test` allowance, Google nonce/exchange, Remember me, Enter submission, localization, and redirects.
- Never read, log, persist, or expose credentials, tokens, client secrets, or password hashes.
- Use existing `--shadcn-*` tokens; no second palette, raw theme-specific grays, or broad unscoped `.mud-*` overrides.
- Wide layout uses an approximately 44/56 brand/auth split; 640px and below collapses to one column.
- Use a 4px cadence: 8px related text, 12px internals, 16px form siblings, 24px groups, 32–48px major regions.
- Placeholder text reaches 4.5:1; interactive boundaries/focus reach 3:1; actionable narrow targets expose at least 44×44px.
- No layout-caused horizontal overflow at 320px or vertical overflow from nested viewport minima.
- Retain reduced-motion and forced-colors support.

---

## File Map

- `Legacy.Maliev.Intranet.Client/Pages/Login.razor`: semantic split, loading state, Google state container, support copy.
- `Legacy.Maliev.Intranet.Client/wwwroot/css/app.css`: page-owned geometry, rhythm, contrast, touch sizing, responsive behavior.
- `Legacy.Maliev.Intranet.Client/wwwroot/js/google-identity-signin.js`: loading/ready/unavailable state and host collapse.
- `Legacy.Maliev.Intranet.Client/wwwroot/images/MALIEV_WHITE.svg`: corrected wordmark geometry.
- `Legacy.Maliev.Intranet.Client/Pages/Login.resx` and `Login.th.resx`: English/Thai copy parity.
- `Legacy.Maliev.Intranet.Tests/LegacyLoginExperienceContractTests.cs`: structural, state, CSS, localization, and security regressions.
- `Legacy.Maliev.Intranet.Tests/IntranetParityContractTests.cs`: black/white SVG geometry parity.

---

### Task 1: Full-Viewport Split and Canonical Wordmark

**Files:**
- Modify: `Legacy.Maliev.Intranet.Tests/LegacyLoginExperienceContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Tests/IntranetParityContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Login.razor`
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/css/app.css`
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/images/MALIEV_WHITE.svg`

**Interfaces:**
- Consumes: `Layout.EmptyLayout`, `LegacyLanguageSelector`, `LegacyThemeService`, canonical black wordmark geometry.
- Produces: `.legacy-login-shell`, `.legacy-login-brand-panel`, `.legacy-login-auth-panel`, `.legacy-login-utilities`, and corrected white SVG geometry.

- [ ] **Step 1: Add failing layout and logo contract tests**

Add tests that read the real Razor, CSS, and SVG files:

```csharp
[Fact]
public void LoginClient_OwnsTheViewportWithResponsiveBrandAndAuthenticationRegions()
{
    var root = FindRepositoryRoot();
    var login = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Login.razor"));
    var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "app.css"));

    Assert.Contains("legacy-login-shell", login, StringComparison.Ordinal);
    Assert.Contains("legacy-login-brand-panel", login, StringComparison.Ordinal);
    Assert.Contains("legacy-login-auth-panel", login, StringComparison.Ordinal);
    Assert.Contains("place-items: stretch", css, StringComparison.Ordinal);
    Assert.Contains(".legacy-login-page { width: 100%;", css, StringComparison.Ordinal);
    Assert.Contains("grid-template-columns: minmax(0, 44%) minmax(0, 56%)", css, StringComparison.Ordinal);
}

[Fact]
public void Shell_LogosShareCanonicalVisibleGeometry()
{
    var root = FindRoot();
    var black = XDocument.Load(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "images", "MALIEV_BLACK.svg"));
    var white = XDocument.Load(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "images", "MALIEV_WHITE.svg"));
    XNamespace svg = "http://www.w3.org/2000/svg";

    Assert.Equal(black.Root!.Attribute("viewBox")!.Value, white.Root!.Attribute("viewBox")!.Value);
    Assert.Equal(
        black.Descendants(svg + "g").Last().Attribute("transform")!.Value,
        white.Descendants(svg + "g").Last().Attribute("transform")!.Value);
}
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~LegacyLoginExperienceContractTests|FullyQualifiedName~IntranetParityContractTests"
```

Expected: split/stretch assertions fail against the centered layout; SVG parity fails because the white asset lacks the canonical transform.

- [ ] **Step 3: Implement the semantic split and corrected asset**

Wrap the existing authentication card with these exact opening regions. Move the current language/theme controls into `legacy-login-utilities`, then move the current card branch into `legacy-login-main` without changing its event handlers:

```razor
<div class="legacy-login-page @(ThemeService.IsDarkMode ? "dark-theme" : string.Empty)">
    <div class="legacy-login-shell">
        <aside class="legacy-login-brand-panel" aria-label="@Text["Employee gateway"]">
            <a class="legacy-login-brand" href="/" aria-label="@Text["HomeLabel"]">
                <img src="images/MALIEV_WHITE.svg" alt="MALIEV" class="legacy-login-brand-image" />
            </a>
            <div class="legacy-login-brand-copy">
                <p class="legacy-login-eyebrow">@Text["MALIEV employee workspace"]</p>
                <h1>@Text["Secure access to your work"]</h1>
                <p>@Text["Sign in with your MALIEV work account to continue."]</p>
            </div>
        </aside>
        <section class="legacy-login-auth-panel">
            <header class="legacy-login-utilities">
                <LegacyLanguageSelector />
                <MudIconButton ButtonType="ButtonType.Button" Class="legacy-login-theme-toggle"
                               Icon="@(ThemeService.IsDarkMode ? Icons.Material.Outlined.LightMode : Icons.Material.Outlined.DarkMode)"
                               Size="Size.Small" OnClick="ToggleThemeAsync"
                               aria-label="@ThemeToggleLabel" title="@ThemeToggleLabel" />
            </header>
            <main class="legacy-login-main" id="legacy-login-content">
        </section>
    </div>
</div>
```

After the existing authentication card branch, close the regions and retain one external company link in the footer:

```razor
            </main>
            <footer class="legacy-login-footer">
                <span>MALIEV CO., LTD.</span>
                <a href="https://www.maliev.com" target="_blank" rel="noreferrer">www.maliev.com</a>
            </footer>
        </section>
    </div>
</div>
```

Replace centered ownership with:

```css
.legacy-empty-layout { display: grid; min-height: 100dvh; place-items: stretch; padding: 0; }
.legacy-login-page { width: 100%; min-height: 100dvh; background: var(--shadcn-background); color: var(--shadcn-foreground); }
.legacy-login-shell { display: grid; min-height: 100dvh; grid-template-columns: minmax(0, 44%) minmax(0, 56%); }
:root { --legacy-login-brand-background: var(--shadcn-foreground); --legacy-login-brand-foreground: var(--shadcn-background); }
:root[data-maliev-theme="dark"] { --legacy-login-brand-background: var(--shadcn-card); --legacy-login-brand-foreground: var(--shadcn-card-foreground); }
.legacy-login-brand-panel { display: flex; min-width: 0; flex-direction: column; justify-content: space-between; padding: clamp(2rem, 5vw, 5rem); background: var(--legacy-login-brand-background); color: var(--legacy-login-brand-foreground); }
.legacy-login-auth-panel { display: grid; min-width: 0; grid-template-rows: auto 1fr auto; }
```

Copy the black SVG’s `viewBox` and nested transform geometry into the white SVG while retaining `fill="#ffffff"`.

- [ ] **Step 4: Add responsive structural rules**

```css
@media (max-width: 959px) {
  .legacy-login-shell { grid-template-columns: minmax(15rem, 36%) minmax(0, 64%); }
}
@media (max-width: 640px) {
  .legacy-login-shell { display: flex; min-height: 100dvh; flex-direction: column; }
  .legacy-login-brand-panel { min-height: auto; padding: 1rem; }
  .legacy-login-brand-copy { display: none; }
  .legacy-login-auth-panel { flex: 1 1 auto; }
}
```

Ensure 320px uses at least 16px gutters and no horizontal scrolling.

- [ ] **Step 5: Verify GREEN, build, and commit**

Run Step 2, then:

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
git add -- Legacy.Maliev.Intranet.Tests/LegacyLoginExperienceContractTests.cs Legacy.Maliev.Intranet.Tests/IntranetParityContractTests.cs Legacy.Maliev.Intranet.Client/Pages/Login.razor Legacy.Maliev.Intranet.Client/wwwroot/css/app.css Legacy.Maliev.Intranet.Client/wwwroot/images/MALIEV_WHITE.svg
git diff --cached --check
git commit -m "fix: give employee login a full branded split layout"
```

Expected before commit: focused tests pass; build exits 0 with zero warnings/errors.

---

### Task 2: Google Availability, Stable Loading, and Localized Recovery

**Files:**
- Modify: `Legacy.Maliev.Intranet.Tests/LegacyLoginExperienceContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Login.razor`
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/js/google-identity-signin.js`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Login.resx`
- Modify: `Legacy.Maliev.Intranet.Client/Pages/Login.th.resx`

**Interfaces:**
- Consumes: `window.malievGoogleIdentity.initializeHost(ElementReference)`, `data-status-*`, `_isCheckingAuth`, Razor localization.
- Produces: `data-google-signin-state="loading|ready|unavailable"`, host hidden only when unavailable, compact live outage note, and `.legacy-login-loading`.

- [ ] **Step 1: Add failing state and localization tests**

```csharp
[Fact]
public void LoginClient_ExposesStablePreflightAndHonestGoogleAvailabilityStates()
{
    var root = FindRepositoryRoot();
    var login = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "Pages", "Login.razor"));
    var google = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "js", "google-identity-signin.js"));

    Assert.Contains("legacy-login-loading", login, StringComparison.Ordinal);
    Assert.Contains("role=\"status\" aria-live=\"polite\"", login, StringComparison.Ordinal);
    Assert.Contains("data-google-signin-state", login, StringComparison.Ordinal);
    Assert.Contains("host.hidden = state === \"unavailable\"", google, StringComparison.Ordinal);
    Assert.Contains("section.dataset.googleSigninState = state", google, StringComparison.Ordinal);
    Assert.Contains("setAvailability(host, \"ready\")", google, StringComparison.Ordinal);
    Assert.Contains("setAvailability(host, \"unavailable\")", google, StringComparison.Ordinal);
}
```

Add XML resource parity assertions for these exact keys:

```text
Employee gateway
MALIEV employee workspace
Secure access to your work
Sign in with your MALIEV work account to continue.
Checking your employee session...
Need help accessing your employee account?
Contact your MALIEV administrator if you cannot access your employee account.
```

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~LegacyLoginExperienceContractTests"
```

Expected: loading/state/resource assertions fail because the contracts do not exist.

- [ ] **Step 3: Implement explicit Google availability state**

```javascript
function setAvailability(host, state) {
    const section = host.closest("[data-google-signin-section]");
    if (!section) return;
    section.dataset.googleSigninState = state;
    host.hidden = state === "unavailable";
}
```

Call loading before initialization, ready after Google renders, and unavailable in the catch path. Keep `setStatus` as the live-message owner. Add `data-google-signin-section` and initial `data-google-signin-state="loading"` to Razor.

- [ ] **Step 4: Implement stable preflight and recovery guidance**

```razor
<section class="legacy-login-card legacy-login-loading" role="status" aria-live="polite" aria-busy="true">
    <MudProgressCircular Size="Size.Small" Color="Color.Primary" Indeterminate="true" />
    <span>@Text["Checking your employee session..."]</span>
</section>
```

Render that section as the true branch of `_isCheckingAuth`; change the current `@if (!_isCheckingAuth)` card branch into its `else` branch so only one card footprint is present. Keep the Google initialization guard. Add natural English and Thai values for every tested key. Repository search found no factual internal reset/help URL, so render the localized plain-text guidance `Contact your MALIEV administrator if you cannot access your employee account.` without inventing a link. Keep one external company link and remove the duplicate.

- [ ] **Step 5: Verify GREEN, build, and commit**

Run Step 2, then:

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
git add -- Legacy.Maliev.Intranet.Tests/LegacyLoginExperienceContractTests.cs Legacy.Maliev.Intranet.Client/Pages/Login.razor Legacy.Maliev.Intranet.Client/wwwroot/js/google-identity-signin.js Legacy.Maliev.Intranet.Client/Pages/Login.resx Legacy.Maliev.Intranet.Client/Pages/Login.th.resx
git diff --cached --check
git commit -m "fix: make employee login availability states explicit"
```

Expected before commit: focused tests pass; build exits 0 with zero warnings/errors.

---

### Task 3: Spacing, Contrast, Touch Targets, and Focus

**Files:**
- Modify: `Legacy.Maliev.Intranet.Tests/LegacyLoginExperienceContractTests.cs`
- Modify: `Legacy.Maliev.Intranet.Client/wwwroot/css/app.css`

**Interfaces:**
- Consumes: Task 1/2 classes and semantic Shadcn tokens.
- Produces: page-scoped 8/12/16/24/32 rhythm, 44px targets, divider/link affordances, stronger placeholder/input/disabled presentation, and zoom-safe geometry.

- [ ] **Step 1: Add failing visual-contract tests**

```csharp
[Fact]
public void LoginClient_UsesAccessibleContrastSpacingAndTouchContracts()
{
    var root = FindRepositoryRoot();
    var css = File.ReadAllText(Path.Combine(root, "Legacy.Maliev.Intranet.Client", "wwwroot", "css", "app.css"));

    Assert.Contains(".legacy-login-form { display: grid; gap: 1rem; }", css, StringComparison.Ordinal);
    Assert.Contains(".legacy-login-footer-note { margin: 1rem 0 0;", css, StringComparison.Ordinal);
    Assert.Contains("background: var(--shadcn-border)", css, StringComparison.Ordinal);
    Assert.Contains("min-height: 2.75rem", css, StringComparison.Ordinal);
    Assert.Contains("text-decoration: underline", css, StringComparison.Ordinal);
    Assert.Contains("color: var(--shadcn-muted-foreground)", css, StringComparison.Ordinal);
    Assert.DoesNotContain("opacity: .5", ExtractLoginCss(css), StringComparison.Ordinal);
}
```

Implement `ExtractLoginCss` as a deterministic substring from `.legacy-login-page` through the final login-specific forced-colors rule.

- [ ] **Step 2: Run focused tests and verify RED**

Run Task 2’s focused command. Expected: spacing, divider, link, and opacity assertions fail.

- [ ] **Step 3: Implement page-owned rhythm and contrast**

```css
.legacy-login-form { display: grid; gap: 1rem; }
.legacy-login-divider { gap: .75rem; margin: 1.5rem 0; color: var(--shadcn-muted-foreground); }
.legacy-login-divider span { height: 1px; background: var(--shadcn-border); }
.legacy-login-footer-note { margin: 1rem 0 0; color: var(--shadcn-muted-foreground); }
.legacy-login-footer a, .legacy-login-footer-note a { text-decoration: underline; text-underline-offset: .2em; }
.legacy-login-page .legacy-login-input input::placeholder { color: var(--shadcn-muted-foreground); opacity: 1; }
.legacy-login-page .legacy-login-input .mud-input-outlined-border { border-color: var(--shadcn-input); }
.legacy-login-page .legacy-login-primary:disabled { background: var(--shadcn-muted); color: var(--shadcn-muted-foreground); opacity: 1; }
```

Preserve package-owned focus rings. At narrow widths, give brand, language, theme, inputs, primary action, and support/footer links `min-height:2.75rem`, with optical logo sizing inside the larger target.

- [ ] **Step 4: Verify GREEN, scan, build, and commit**

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --filter "FullyQualifiedName~LegacyLoginExperienceContractTests"
node C:\Users\natth\.agents\skills\impeccable\scripts\detect.mjs --json Legacy.Maliev.Intranet.Client\Pages\Login.razor
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
git add -- Legacy.Maliev.Intranet.Tests/LegacyLoginExperienceContractTests.cs Legacy.Maliev.Intranet.Client/wwwroot/css/app.css
git diff --cached --check
git commit -m "fix: clarify employee login spacing and contrast"
```

Expected: tests pass, detector returns `[]` or every finding is resolved/documented, build exits 0 with zero warnings/errors.

---

### Task 4: Browser Evidence and Completion Gate

**Files:**
- Modify only browser-exposed defects in files already owned by Tasks 1–3.
- Never add screenshots, credentials, logs, snapshots, profiles, or temporary artifacts to Git.

**Interfaces:**
- Consumes: snapshot-backed Aspire login URL and completed login surface.
- Produces: fresh build/test/browser evidence and a clean validated worktree.

- [ ] **Step 1: Run final Release build, focused tests, and full affected suite**

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~LegacyLoginExperienceContractTests|FullyQualifiedName~IntranetParityContractTests|FullyQualifiedName~ShadcnStyleSystemContractTests"
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --logger "console;verbosity=minimal"
```

Expected: build 0 warnings/errors; focused and full suites have zero failures/skips. If shell duration is shorter than the suite, use one isolated redirected runner and bounded polling; never leave duplicate test runners.

- [ ] **Step 2: Inspect wide light/dark states at 1440×900**

Verify full viewport width, approximate 44/56 split, card max 28rem, no old 32px layout overflow, valid black/white logos, clear hierarchy, visible placeholder/boundary/disabled/link/focus states, and no empty Google host when unavailable.

- [ ] **Step 3: Inspect narrow/localized/keyboard/error/zoom states**

At 390×844, 320×700, and 200% zoom, verify one-column order, no horizontal overflow, Thai wrapping, 44px targets, email/password/Remember me/Change, loading, disabled, focus-visible, invalid/error, and recovery guidance. Store screenshots only in the system temporary directory and read them back. Never enter or inspect the user’s password.

- [ ] **Step 4: Run final detector, diff, process, and status checks**

```powershell
node C:\Users\natth\.agents\skills\impeccable\scripts\detect.mjs --json Legacy.Maliev.Intranet.Client\Pages\Login.razor
git diff --check
git status --short --branch
```

Stop only task-owned processes unless the user asks to keep the dev stack running. If browser evidence requires code changes, repeat Steps 1–4 and commit only those corrections as `fix: finish employee login responsive states`; otherwise do not create an empty commit.
