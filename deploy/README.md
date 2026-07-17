# Deployment contract

These manifests are planning artifacts only. Deployment stays disabled until every preserved staff workflow, staging, capacity and rollback gate passes.

- Existing GKE cluster only; no node pool creation or resize.
- Namespace: `maliev-legacy` only.
- Server-side employee sessions use the existing shared legacy Redis, not a new cache.
- The BFF has no database and no direct CloudNativePG or SQL Server access.
- Runtime endpoints and Redis configuration are projected from the one `maliev-legacy-secrets` JSON secret.
- Employee-token validation is fail-closed outside `Testing`. Runtime configuration must project `Jwt__PublicKey` as the existing Base64-encoded RSA public-key PEM contract. `Jwt__Issuer` and `Jwt__Audience` override the checked-in non-secret production defaults where the environment differs; `Jwt__KeyId` is optional. The Intranet never receives AuthService's private signing key.
- Local Aspire must project the same generated `Jwt__PublicKey`, issuer and audience into the Intranet resource before exercising employee login. Production GitOps must map these public validation values through `legacy-maliev-intranet-runtime` from the consolidated `maliev-legacy-secrets` document before enabling deployment.
- Both hosts use the stable Data Protection application name `Legacy.Maliev.Intranet` and the existing Redis key `legacy:intranet:data-protection-keys`. Outside `Testing`, project `DataProtection__CertificatePfxBase64` and `DataProtection__CertificatePassword` from `maliev-legacy-secrets`. This certificate encrypts the shared Redis key ring at rest, and the process reuses one Redis multiplexer for both the distributed session cache and key persistence. Startup fails closed if Redis or either certificate value is missing or invalid; never commit certificate material.
- `Legacy.Maliev.AppHost` main commit `47a1d41` does not yet project these two Data Protection certificate values into either Intranet host. Coordinate that local-only follow-up before exercising the hosts through Aspire; do not weaken the fail-closed contract.
- Purchase-order workflows require the internal `Services__Procurement`, `Services__Catalog`, `Services__Employee`, `Services__Document`, and `Services__File` endpoints; no Google service-account key is mounted because FileService owns GCS through Workload Identity.
- The placeholder image digest must never deploy; GitOps receives only a scanned immutable digest.
- One small replica preserves cluster capacity until measured load proves a different setting is safe without additional cost.

The authoritative ingress, secret projection and environment overlay belong in `MALIEV-Co-Ltd/maliev-gitops` after the migration is complete.
