window.malievFocus = {
    byId(id) {
        const element = document.getElementById(id);
        if (element instanceof HTMLElement) {
            element.focus();
            return true;
        }

        return false;
    },

    lastNavigationLink(sentinel) {
        const drawer = sentinel instanceof HTMLElement ? sentinel.closest("aside") : null;
        const links = drawer?.querySelectorAll(".legacy-rail-link");
        const lastLink = links?.item(links.length - 1);
        if (lastLink instanceof HTMLElement) {
            lastLink.focus();
            return true;
        }

        return window.malievFocus.byId("legacy-navigation-close");
    }
};
