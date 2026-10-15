/* Behavior shared by every page. Page-specific scripts live beside it (infrastructure-groups.js) or under js/modules/. */

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
