(() => {
    const navigation = document.getElementById('compat-navigation');
    const menuButton = document.getElementById('compat-menu-button');
    const backdrop = document.getElementById('compat-backdrop');
    const search = document.getElementById('compat-search');
    const searchInput = document.getElementById('compat-search-input');
    const routes = Array.from(document.querySelectorAll('#compat-search-routes option'));
    const language = document.getElementById('compat-language');

    function closeNavigation() {
        if (!navigation || !menuButton || !backdrop) return;
        navigation.classList.remove('open');
        menuButton.setAttribute('aria-expanded', 'false');
        backdrop.hidden = true;
    }

    menuButton?.addEventListener('click', () => {
        const open = !navigation?.classList.contains('open');
        navigation?.classList.toggle('open', open);
        menuButton.setAttribute('aria-expanded', String(open));
        if (backdrop) backdrop.hidden = !open;
    });
    backdrop?.addEventListener('click', closeNavigation);
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') closeNavigation();
        if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
            event.preventDefault();
            searchInput?.focus();
        }
    });
    search?.addEventListener('submit', event => {
        event.preventDefault();
        const query = searchInput?.value.trim() ?? '';
        const match = routes.find(option => option.value.localeCompare(query, undefined, { sensitivity: 'accent' }) === 0);
        const route = match?.dataset.route;
        if (route?.startsWith('/')) window.location.assign(route);
    });
    language?.addEventListener('change', event => {
        const culture = event.target.value === 'th-TH' ? 'th-TH' : 'en-TH';
        try { window.localStorage.setItem('maliev_culture', culture); } catch (_) { }
        document.cookie = 'maliev_culture=' + encodeURIComponent(culture) + '; path=/; max-age=31536000; SameSite=Lax';
        window.location.reload();
    });
})();
