# Reproducible Intranet image restore

The compatibility and BFF Dockerfiles build from the 14 checked-in Intranet
`packages.lock.json` files. Their two pinned external project references also
need locks. Snapshots in `build/nuget-locks/` correspond to the exact
ServiceDefaults and CompatibilityContracts commits checked out by both
Dockerfiles; the Dockerfiles copy them into those checkouts before
`dotnet restore --locked-mode`. Publish uses `--no-restore`, so a changed or
missing lock fails before an image is produced. The SDK and runtime base-image
manifest digests are pinned separately.

When a project dependency, external source commit, or base image must change:

1. Use isolated checkouts of both external repositories at the Dockerfile
   commits. Keep `GITHUB_ACTIONS` unset so ServiceDefaults follows the same
   project-reference branch as the Docker build.
2. From this repository, regenerate both image graphs with
   `dotnet restore Legacy.Maliev.Intranet/Legacy.Maliev.Intranet.csproj --use-lock-file -p:MalievWorkspaceRoot=<external-checkout-parent>`
   and the same command for `Legacy.Maliev.Intranet.Bff/Legacy.Maliev.Intranet.Bff.csproj`.
   Copy the two resulting external project locks into `build/nuget-locks/`.
3. Verify both Docker build stages with
   `docker build --file Legacy.Maliev.Intranet/Dockerfile --target build .`
   and the corresponding BFF Dockerfile. PR validation runs these checks; no
   image is pushed and `LEGACY_DEPLOY_ENABLED` remains unchanged.

There is no Node/Gulp restore or host-wide Docker image pruning in this
repository. This guard does not authorize publishing or deploying either image.
