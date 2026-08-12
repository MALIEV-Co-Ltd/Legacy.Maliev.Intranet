window.malievCustomerWorkspace = {
    async ensureActiveTabVisible() {
        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        const activeTab = document.querySelector('.customer-detail__tabs [role="tab"][aria-selected="true"]');
        const viewport = activeTab?.closest('.mud-tabs-tabbar-content');
        if (!activeTab || !viewport) {
            return;
        }

        const tab = activeTab.getBoundingClientRect();
        const bounds = viewport.getBoundingClientRect();
        const wrapper = activeTab.closest('.mud-tabs-tabbar-wrapper');
        if (!wrapper) {
            return;
        }
        const inset = 6;
        const currentTransform = getComputedStyle(wrapper).transform;
        const currentOffset = currentTransform === 'none' ? 0 : new DOMMatrixReadOnly(currentTransform).m41;
        if (tab.left < bounds.left + inset) {
            wrapper.style.setProperty('transform', `translateX(${currentOffset + bounds.left + inset - tab.left}px)`, 'important');
        } else if (tab.right > bounds.right - inset) {
            wrapper.style.setProperty('transform', `translateX(${currentOffset - (tab.right - bounds.right + inset)}px)`, 'important');
        } else {
            wrapper.style.removeProperty('transform');
        }
    }
};
