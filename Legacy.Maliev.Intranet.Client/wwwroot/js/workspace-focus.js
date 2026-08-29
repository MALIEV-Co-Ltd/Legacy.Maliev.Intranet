window.malievFocus = {
    byId(id) {
        const element = document.getElementById(id);
        if (element instanceof HTMLElement) {
            element.focus();
            return true;
        }

        return false;
    }
};
