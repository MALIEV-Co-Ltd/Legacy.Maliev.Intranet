(function () {
    "use strict";

    const libraryTimeoutMs = 10000;

    function setStatus(host, message) {
        const status = host.parentElement?.querySelector("[data-google-signin-status]");
        if (status) {
            status.textContent = message || "";
            status.hidden = !message;
        }
    }

    function setAvailability(host, state) {
        const section = host.closest("[data-google-signin-section]");
        if (!section) return;
        section.dataset.googleSigninState = state;
        host.hidden = state === "unavailable";
    }

    function localizedStatus(host, name, fallback) {
        return host.dataset[name] || fallback;
    }

    async function waitForGoogleIdentity() {
        const startedAt = Date.now();
        while (!window.google?.accounts?.id) {
            if (Date.now() - startedAt >= libraryTimeoutMs) {
                throw new Error("Google Identity Services did not load.");
            }
            await new Promise(resolve => window.setTimeout(resolve, 50));
        }
    }

    async function readJson(response) {
        const contentType = response.headers.get("content-type") || "";
        return contentType.includes("application/json") ? response.json() : null;
    }

    async function requestNonce(returnUrl) {
        const response = await fetch("/bff/google/nonce", {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ returnUrl })
        });
        const payload = await readJson(response);
        if (!response.ok || !payload?.clientId || !payload?.nonce) {
            throw new Error("Google sign-in could not be initialized.");
        }
        return payload;
    }

    async function completeSignIn(host, credential, nonce) {
        setStatus(host, localizedStatus(host, "statusCompleting", "Completing Google sign-in..."));
        const response = await fetch("/bff/google", {
            method: "POST",
            credentials: "same-origin",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ credential, nonce })
        });
        const payload = await readJson(response);
        if (!response.ok || !payload?.redirectUrl) {
            throw new Error("Google sign-in could not be completed.");
        }
        window.location.assign(payload.redirectUrl);
    }

    async function initializeHost(host) {
        if (!host || host.dataset.googleSigninInitialized === "true") {
            return;
        }

        host.dataset.googleSigninInitialized = "true";
        try {
            setAvailability(host, "loading");
            setStatus(host, localizedStatus(host, "statusLoading", "Loading Google sign-in..."));
            const [configuration] = await Promise.all([
                requestNonce(host.dataset.returnUrl || "/Dashboard"),
                waitForGoogleIdentity()
            ]);

            window.google.accounts.id.initialize({
                client_id: configuration.clientId,
                nonce: configuration.nonce,
                ux_mode: "popup",
                auto_select: false,
                use_fedcm_for_button: true,
                callback: async response => {
                    try {
                        await completeSignIn(host, response.credential, configuration.nonce);
                    } catch {
                        setStatus(host, localizedStatus(host, "statusCompletionFailed", "Google sign-in could not be completed. Reloading..."));
                        window.setTimeout(() => window.location.reload(), 1200);
                    }
                }
            });

            host.replaceChildren();
            window.google.accounts.id.renderButton(host, {
                type: "standard",
                theme: "outline",
                size: "large",
                text: "continue_with",
                shape: "rectangular",
                logo_alignment: "left",
                width: Math.max(240, Math.min(360, Math.floor(host.clientWidth || 360)))
            });
            setAvailability(host, "ready");
            setStatus(host, "");
        } catch {
            host.dataset.googleSigninInitialized = "false";
            setAvailability(host, "unavailable");
            setStatus(host, localizedStatus(host, "statusUnavailable", "Google sign-in is temporarily unavailable. You can still use your work email."));
        }
    }

    function initialize() {
        document.querySelectorAll("[data-google-signin-host]").forEach(host => {
            void initializeHost(host);
        });
    }

    window.malievGoogleIdentity = { initializeHost };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
