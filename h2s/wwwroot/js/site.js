/* Behavior shared by every page. Page-specific scripts live under js/modules/. */

// A tile icon that fails to load (a moved favicon, an unreachable host) falls back to the same dot as a link without one.
(() => {
    const replaceWithDot = icon => {
        const fallback = document.createElement('span');
        fallback.className = 'link-block-icon-fallback';
        fallback.setAttribute('aria-hidden', 'true');
        fallback.textContent = '•';
        icon.replaceWith(fallback);
    };

    // Image errors don't bubble, so listen for them during capture.
    document.addEventListener('error', event => {
        if (event.target instanceof HTMLImageElement && event.target.classList.contains('link-block-icon')) {
            replaceWithDot(event.target);
        }
    }, true);

    // This script loads at the end of the page, so catch any icon that failed before it ran.
    for (const icon of document.querySelectorAll('img.link-block-icon')) {
        if (icon.complete && icon.naturalWidth === 0) {
            replaceWithDot(icon);
        }
    }
})();

// Closing a dialog returns focus to what opened it. Bootstrap does that only for data-bs-toggle triggers, and every dialog
// here opens from script. When a save has replaced the opener (a tile swapped for its re-rendered slot, say), focus goes to
// the same control in the item's new markup, found by the item's data-*-id.
(() => {
    const itemSelector = '[data-link-id], [data-panel-id], [data-category-id], [data-id]';
    let opener = null;

    document.addEventListener('show.bs.modal', () => {
        const active = document.activeElement;
        if (!(active instanceof HTMLElement) || active === document.body) {
            opener = null;
            return;
        }
        const item = active.closest(itemSelector);
        const attribute = item ? [...item.attributes].find(a => /^data-(link|panel|category)?-?id$/.test(a.name)) : null;
        opener = {
            element: active,
            item: attribute ? `[${attribute.name}="${CSS.escape(attribute.value)}"]` : null,
            control: active.classList.length ? `.${CSS.escape(active.classList[0])}` : null
        };
    });

    document.addEventListener('hidden.bs.modal', () => {
        if (!opener) {
            return;
        }
        let target = opener.element.isConnected ? opener.element : null;
        if (!target && opener.item) {
            const item = document.querySelector(opener.item);
            target = (opener.control && item?.querySelector(opener.control)) || null;
        }
        target?.focus();
        opener = null;
    });
})();
