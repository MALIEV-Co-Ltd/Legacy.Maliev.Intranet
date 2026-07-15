# Deployment contract

These manifests are planning artifacts only. Deployment stays disabled until every preserved staff workflow, staging, capacity and rollback gate passes.

- Existing GKE cluster only; no node pool creation or resize.
- Namespace: `maliev-legacy` only.
- Server-side employee sessions use the existing shared legacy Redis, not a new cache.
- The BFF has no database and no direct CloudNativePG or SQL Server access.
- Runtime endpoints and Redis configuration are projected from the one `maliev-legacy-secrets` JSON secret.
- The placeholder image digest must never deploy; GitOps receives only a scanned immutable digest.
- One small replica preserves cluster capacity until measured load proves a different setting is safe without additional cost.

The authoritative ingress, secret projection and environment overlay belong in `MALIEV-Co-Ltd/maliev-gitops` after the migration is complete.

