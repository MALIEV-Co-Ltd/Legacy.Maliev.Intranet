# Task 3 — actions, typography, and form controls

## Scope

Implemented the scoped MudBlazor action, typography, form, picker, selection, and checkbox adapter contract. Added the deterministic `/components/mud-inventory` fixture and reduced Orders button and shared toolbar CSS to composition-only responsibilities. No production bindings, callbacks, validation, form models, Login behavior, or public component APIs changed.

## RED evidence

Command:

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --filter "FullyQualifiedName~MudAdapterContractTests"
```

Result: exit 1; 22 failed, 4 passed, 0 skipped, 26 total. The failures were the deliberately absent family/state selector and declaration contracts, including `.mud-button-filled`, `.mud-input-error`, `.mud-picker`, `.mud-checkbox`, hover/active/focus-visible/disabled/read-only/checked/indeterminate/open/dark selectors, and the input/icon control height declarations.

## Implementation

- `Maliev.ShadcnBlazor/wwwroot/css/shadcn-mudblazor.css`: scoped semantic token mappings for Mud typography, icons, links, buttons, icon buttons, inputs, selects, list items, pickers, calendars, and checkboxes. Invalid focus rings use the required light 20% and dark 40% destructive mixes. Disabled controls use opacity 0.5; read-only inputs remain focusable.
- `Maliev.ShadcnBlazor.Tests/Contracts/MudAdapterContractTests.cs`: family/declaration, state-selector, and coarse-pointer checkbox-width regression coverage.
- `Maliev.ShadcnBlazor.Showcase/Pages/MudInventory.razor`: deterministic `mud-actions`, `mud-typography`, and `mud-forms` fixture sections, including disabled, read-only, invalid, clearable, multiline, adornment, selected, indeterminate, and Thai-label examples.
- `Maliev.ShadcnBlazor.Showcase/wwwroot/css/showcase.css`: fixture layout only; removed showcase-local `.mud-*` appearance overrides.
- `Legacy.Maliev.Intranet.Client.Features.Orders/Components/Shared/PrimaryButton.razor.css` and `SecondaryButton.razor.css`: removed canonical color, radius, hover, focus, disabled, and motion styling while preserving wrapper composition and the primary busy spinner behavior.
- `Legacy.Maliev.Intranet.Client.Shared/Components/ListToolbar.razor.css`: retained grid/responsive composition and removed toolbar-owned control height and appearance rules.
- `Legacy.Maliev.Intranet.Tests/ShadcnStyleSystemContractTests.cs`: scope resolution moved the stale toolbar-owned `height: 2.75rem` source assertion forward to assert its absence and the adapter-owned `height: var(--shadcn-control-height)` contract. This edit was explicitly authorized after the focused suite initially exposed the stale assertion.

## Validation

Initial full build after adding the showcase fixture failed with 7 Razor syntax/type-inference errors in `MudInventory.razor`. The fixture was corrected to use deterministic typed values; no production code changed.

Final build:

```powershell
dotnet build .\Legacy.Maliev.Intranet.slnx -c Release
```

Result: exit 0, 0 warnings, 0 errors.

Package suite:

```powershell
dotnet test .\Maliev.ShadcnBlazor.Tests\Maliev.ShadcnBlazor.Tests.csproj -c Release --no-build --no-restore
```

Result: exit 0, 46 passed, 0 failed, 0 skipped.

Focused Intranet suite:

```powershell
dotnet test .\Legacy.Maliev.Intranet.Tests\Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~MudBlazorComponentConformanceTests|FullyQualifiedName~ListToolbar|FullyQualifiedName~LoginFormContractTests|FullyQualifiedName~AccountingUiAccessibilityContractTests"
```

Result: exit 0, 36 passed, 0 failed, 0 skipped.

Static review:

```powershell
git diff --check
git diff --cached --check
```

Result: both exit 0. A targeted search confirmed the wrapper styles contain no legacy action/panel/focus tokens or toolbar `height: 2.75rem`; no coarse-pointer `.mud-checkbox .mud-button-root` full-width rule exists.

## Self-review and concerns

The adapter remains scoped to `.shadcn-scope` and `.shadcn-overlay-scope`, consumes rather than redefines base tokens, and does not set a font family, preserving the inherited IBM Plex Thai stack. All visual state selectors requested by the task are present, and the calendar explicitly covers hover, today, selected, range, outside-month, and disabled states. No browser run was required by the brief; the deterministic fixture route and compile/test contracts are the available validation for this styling slice.

No unresolved concerns.

## Commits

- Implementation: `11af1e2` (`style: apply shadcn actions and form controls`)
