# Supplier Catalog persistence acceptance

Bounded joined HTTP acceptance for AppHost issue #144. This adds no production hooks, permissions, grants, migrations or save behavior. It reuses merged Intranet #266 lookup controls and real Catalog/Procurement programs pinned in the workflow.

One synthetic supplier is created through the rendered `/Suppliers/Create` page. The browser selects a postcode combination returned by the real Catalog controller and licensed local dataset. Normal BFF cookie, Redis ticket, certificate-protected key ring, CSRF and server workload token exchange execute over Kestrel HTTP. Real Procurement APIs save the supplier and attached address in a disposable Testcontainers PostgreSQL database. Independent domain API reads verify the relationship and copied values; a full page reload verifies the persisted editor values. No Playwright routing interception or fake Catalog/domain response is used.

The synthetic authority issues runtime RSA-signed employee and service tokens through real HTTP endpoints. Normal BFF token validation and normal domain JWT/RequirePermission handlers remain enabled; APIs run in Production to avoid Defaults Testing signature bypass. IAM is unavailable in this fixture and returns false, exercising existing exact signed permission fallback on these non-live routes. Rejection controls cover missing CSRF, read-only employee, anonymous Catalog access, forged domain signature and missing Catalog/domain workload permissions. They leave exactly one successful supplier and address.

This does not prove real AuthService issuance, full AppHost orchestration, Creden/provider company selection, supplier edit, customer, billing, shipping, PO, or quotation. Those require separate coverage. No existing data or credentials are used.

Standard GitHub Ubuntu runner only; test-scoped containers/factories/authority/browser are disposed. Public artifact is a sanitized actual test outcome plus exact source graph; raw TRX, browser state, tokens, connection strings, assertions and screenshots are not uploaded. Normal PR coverage/security gates remain unchanged.
