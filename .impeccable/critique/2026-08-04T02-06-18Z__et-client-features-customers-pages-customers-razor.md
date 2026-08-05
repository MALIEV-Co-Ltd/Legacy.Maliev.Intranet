---
target: Legacy.Maliev.Intranet customer search and filter control
total_score: 25
p0_count: 0
p1_count: 1
timestamp: 2026-08-04T02-06-18Z
slug: et-client-features-customers-pages-customers-razor
---
## Design Health Score

| # | Heuristic | Score | Key Issue |
|---|---|---:|---|
| 1 | Visibility of System Status | 3 | Loading is announced, but pending versus applied filter state is invisible. |
| 2 | Match System / Real World | 3 | Familiar controls, but sort and page size are framed as filters. |
| 3 | User Control and Freedom | 2 | There is no clear/reset action. |
| 4 | Consistency and Standards | 3 | Standard MudBlazor vocabulary and restrained styling. |
| 5 | Error Prevention | 2 | Refresh can reload applied query state while unapplied inputs remain visible. |
| 6 | Recognition Rather Than Recall | 3 | Labels and icons are recognizable; Apply versus Refresh must be inferred. |
| 7 | Flexibility and Efficiency | 2 | No explicit Enter-to-apply path and routine sort/size changes require Apply. |
| 8 | Aesthetic and Minimalist Design | 3 | Clean, but the generic all-white control card lacks meaningful grouping. |
| 9 | Error Recovery | 2 | Empty results do not offer clearing/reset guidance. |
| 10 | Help and Documentation | 2 | No inline explanation of applied state or Refresh semantics. |
| **Total** |  | **25/40** | **Serviceable, but interaction semantics need clarification** |

## Anti-Patterns Verdict

**LLM assessment:** Low AI-slop risk. The toolbar uses credible product controls and restrained color, but it still looks framework-default: one generic white form row treats search, ordering, display density, and reload as if they were one operation.

**Deterministic scan:** `detect.mjs --json` returned `[]` with exit code 0 for `Customers.razor`. No rules, locations, suppressions, or false positives were reported. This is expected: the important defects are interaction-model and state-feedback issues, not detectable markup anti-patterns.

**Visual overlays:** No reliable overlay exists. The fresh-tab browser connection stalled before mutable injection, so no live accessibility tree, computed contrast, focus-order, touch-target, or overflow measurements are claimed.

## Overall Impression

The control is visually tidy and structurally responsive. Its biggest weakness is not spacing; it is that the UI does not expose the difference between edited controls, applied URL/query state, and a data refresh. That ambiguity can make the visible toolbar disagree with the table results.

## What's Working

- Search gets the largest desktop column, matching task frequency.
- Labels, standard field types, familiar icons, loading/error states, and deliberate 1080/720/420px breakpoints provide a sound foundation.
- Apply is visually primary and Refresh is secondary, so basic action hierarchy is present.

## Priority Issues

### [P1] Visible controls can disagree with refreshed results

**Why it matters:** `ReloadAsync` reloads the applied query properties while leaving edited `searchInput`, `sortInput`, and `sizeInput` visible. An employee can believe a new filter is active when the table actually reflects the previous URL state.

**Fix:** Track a dirty state. Disable Refresh while edits are pending, or make Refresh first restore the applied values. Show a subtle “changes not applied” state and disable Apply while no changes exist.

**Suggested command:** `$impeccable clarify`

### [P2] Search, sorting, density, and refresh are conflated

**Why it matters:** Sort order and rows per page are display controls, not filters. Refresh is a data action. Treating all five elements as one filter group increases interpretation time.

**Fix:** Keep one compact toolbar, but group search and Apply together; group Sort and Rows per page as view controls; separate Refresh with spacing or a divider. Preserve the current URL query contract.

**Suggested command:** `$impeccable layout`

### [P2] No fast way to return to the default result set

**Why it matters:** Clearing requires manually resetting several fields and applying again, which is slow and error-prone for daily CRM work.

**Fix:** Add a quiet Clear action only when applied or pending values differ from defaults. Reset search, sort, page size, and page index atomically.

**Suggested command:** `$impeccable clarify`

### [P2] Request feedback is weaker than the data task

**Why it matters:** Loading replaces the table with a thin bar and Apply remains enabled. Employees lose context and can trigger competing requests.

**Fix:** Keep the table shell, mark the result region busy, use row skeletons or a restrained loading overlay, and disable conflicting actions until navigation/load completes.

**Suggested command:** `$impeccable harden`

## Persona Red Flags

**Alex (Power User):** Sort and page-size changes require a separate Apply click; no explicit Enter-to-apply handler exists; there is no clear/reset shortcut. Repetitive list triage is slower than it should be.

**Sam (Accessibility):** Semantic labels are a strength, but pending/applied state is not announced. Focus restoration after query navigation is unproven, and live contrast/target measurements could not be collected.

**Casey (Mobile):** The CSS stacks correctly at 720px and again at 420px, but the primary actions remain at the top of a long customer list and runtime 44px target/overflow evidence is unavailable.

## Minor Observations

- The empty state should distinguish “no customer records” from “no matches” and offer Clear filters for the latter.
- The Thai primary label should describe the real action consistently; “กรองข้อมูล” is more natural than a literal “ใช้ตัวกรอง” if it applies the whole query.
- The page-size control belongs near result count/pagination conceptually, even if retained in the top toolbar for efficiency.
- The page heading uses fluid `clamp()` sizing despite the product reference preferring a fixed `rem` scale for task UIs; this is outside the selected toolbar scope.

## Questions to Consider

- Should search apply on Enter while sort and page size apply immediately, eliminating the generic Apply button?
- If explicit Apply is retained to control service calls, how should the UI make pending changes unmistakable?
- Is Refresh meant to re-fetch the current applied query, or to restore default results? The label and behavior should answer that without inference.
