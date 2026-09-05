window.malievNavigation = {
    replaceCurrentUrl(url) {
        window.history.replaceState(window.history.state, "", url);
    }
};
