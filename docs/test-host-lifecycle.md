# Intranet test-host lifecycle

Issue #231 tracks broad-suite resource growth while repeatedly starting and
disposing actual BFF applications in one test process. A bounded baseline
BFF-only run reached approximately 3.89 GB working set after two minutes
(159 tests passed before the diagnostic run was stopped). A heap snapshot
showed 1.56 GB allocated objects, including 1.39 GB reachable live objects.

The retained roots include ASP.NET Core 10.0.12's endpoint rate-limiter
heartbeat. Its captured `HostFactoryResolver.HostingListener` retains the
factory and application pipeline, including static-asset endpoint graphs and
OpenTelemetry metric buffers. The middleware constructs that limiter outside
DI but never disposes it. This matches the upstream issue
[dotnet/aspnetcore#66434](https://github.com/dotnet/aspnetcore/issues/66434).

`TestHostLifecycle` supplies the missing ownership only in test configuration.
Its startup filter follows composed request delegates, captures the actual
endpoint limiter, and disposes it when the test host's DI container is disposed.
The real middleware and its configured policies remain active. Authentication,
CSRF, authorization, endpoint mapping, rate-limit permit/window values, and
production source are unchanged. No test is excluded or assertion relaxed.

The regression creates five real BFF hosts (one warmup), verifies that the first
ten rejected login attempts retain their unauthorized result and the eleventh
is limited with 429, then proves later disposed factories are collectible.
It uses the real Auth client without pooled transport because that route rejects
the email before any external call. This separates the endpoint-limiter leak
from `IHttpClientFactory`'s intentional two-minute handler lifetime. The first
OpenTelemetry diagnostics worker is process-wide; warming it isolates repeated
host retention rather than asserting all framework-global initialization vanishes.

The workaround relies on the pinned middleware's private `_endpointLimiter`
field. A changed shape throws rather than silently bypassing cleanup, and the
collection regression catches missing ownership. Review and remove this utility
when upgrading to a framework version with verified native shutdown disposal.
Do not copy it into production or disable rate limiting to stabilize tests.

Run the Release build before focused/full tests:

```powershell
dotnet build Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release -p:UseLocalMalievDependencies=true -p:MalievWorkspaceRoot='<isolated-dependencies>'
dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build --filter FullyQualifiedName~TestHostLifecycleTests
dotnet test Legacy.Maliev.Intranet.Tests/Legacy.Maliev.Intranet.Tests.csproj -c Release --no-build
```

The original local broad run (815 passed before abort at 6m17s/about 6.8 GB)
is not a full-suite pass. CI 36670721935 did finish its unit suite (1,234/1,234)
but failed a separate browser shell assertion. That CI result neither proves
the local resource condition absent nor authorizes restarting the CI/runtime.

## Verified repair evidence

On 2026-09-30, the Release solution build finished with zero warnings/errors.
The focused lifecycle regression passed (1/1). Removing the disposal hook made
that same regression fail with all four disposed factories retained; restoring
the hook made it pass again. The complete unit suite then passed 1,235/1,235
with no failures or skipped tests in 8m33s, using `DOTNET_PROCESSOR_COUNT=1`.
Its highest observed process working-set peak was 3,739,492,352 bytes (3.48 GiB),
not a promised memory ceiling. Pooled HTTP-handler lifetimes and framework-global
initialization still consume memory; the repair addresses the demonstrated
per-host endpoint-limiter root, not every transient allocation.

`dotnet format --verify-no-changes --no-restore`, `git diff --check`, and the
test project's transitive vulnerable-package audit passed. Test results and
diagnostic dumps remain outside the checkout. Browser tests and full Aspire/TLS
acceptance were not rerun for this test-host-only change; the separate PR #230
browser assertion is owned by its lane and no workflows or production files
were changed here. Exact-head CI remains a merge gate.
