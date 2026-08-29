window.malievCustomerWorkspace = {
    preservePanelKeyDefaults() {
        const keys = new Set(['Home', 'End', 'ArrowLeft', 'ArrowRight']);
        document.querySelectorAll('.customer-detail__tabs [role="tabpanel"]').forEach(panel => {
            if (panel.dataset.malievKeyGuard === 'true') {
                return;
            }

            panel.dataset.malievKeyGuard = 'true';
            panel.addEventListener('keydown', event => {
                if (keys.has(event.key)) {
                    event.stopPropagation();
                }
            });
        });
    },
    async ensureActiveTabVisible() {
        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        const activeTab = document.querySelector('.customer-detail__tabs [role="tab"][aria-selected="true"]');
        const viewport = activeTab?.closest('.shadcn-tabs-list');
        if (!activeTab || !viewport) {
            return;
        }

        const tab = activeTab.getBoundingClientRect();
        const bounds = viewport.getBoundingClientRect();
        const inset = 6;
        if (tab.left < bounds.left + inset) {
            viewport.scrollLeft -= bounds.left + inset - tab.left;
        } else if (tab.right > bounds.right - inset) {
            viewport.scrollLeft += tab.right - bounds.right + inset;
        }
    }
};
