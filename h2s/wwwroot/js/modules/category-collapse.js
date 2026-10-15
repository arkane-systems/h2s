/* Collapsible categories on the dashboard (not in edit mode): a category's title collapses it to its header. The
   collapsed IDs live in a cookie (ViewerPreferences.CollapsedCategoriesCookie), so the server renders them collapsed
   with no flash, and this browser only. Saving lists only the categories on the page, so deleted ones drop out. */

const grid = document.querySelector('.category-grid:not(.is-editing)');

if (grid) {
    const cookie = grid.dataset.collapseCookie;

    const save = () => {
        const ids = [...grid.querySelectorAll(':scope > .category-box.is-collapsed')].map(box => box.dataset.categoryId);
        document.cookie = ids.length
            ? `${cookie}=${ids.join('.')}; path=/; max-age=31536000; samesite=lax`
            : `${cookie}=; path=/; max-age=0; samesite=lax`;
    };

    grid.addEventListener('click', event => {
        const button = event.target.closest('.category-collapse-button');
        if (!button) {
            return;
        }

        const box = button.closest('.category-box');
        const collapsed = !box.classList.contains('is-collapsed');
        box.classList.toggle('is-collapsed', collapsed);
        button.setAttribute('aria-expanded', String(!collapsed));
        save();
    });
}
