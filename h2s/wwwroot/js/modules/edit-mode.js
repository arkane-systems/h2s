/* Dashboard edit mode (/?edit=true): rearranges categories and links by drag and drop or the keyboard, saving each move. */
import Sortable from '../../lib/sortablejs/modular/sortable.esm.js';
import { showToast } from './toast.js';

const config = document.getElementById('edit-mode');
const grid = document.querySelector('.category-grid.is-editing');

if (config && grid) {
    initEditMode(config, grid);
}

function initEditMode(config, grid) {
    const categoriesUrl = config.dataset.reorderCategoriesUrl;
    const linksUrl = config.dataset.reorderLinksUrl;
    const sortables = [];
    let busy = false;
    let snapshot = null;

    // Tiles keep their markup, but in edit mode they are things to arrange, not links to follow.
    for (const tile of grid.querySelectorAll('.link-block')) {
        tile.setAttribute('tabindex', '-1');
        tile.setAttribute('aria-disabled', 'true');
    }
    grid.addEventListener('click', event => {
        if (event.target.closest('.link-block')) {
            event.preventDefault();
        }
    }, true);

    // A live region for keyboard moves, which have no visible drag to follow.
    const status = document.createElement('div');
    status.className = 'visually-hidden';
    status.setAttribute('aria-live', 'polite');
    config.append(status);

    const linkLists = () => [...grid.querySelectorAll('.link-block-list')];
    const categoryBoxes = () => [...grid.querySelectorAll(':scope > .category-box')];
    const linkSlots = list => [...list.querySelectorAll(':scope > .link-slot')];
    const sectionOf = box => box.dataset.adminCategory;
    const sectionBoxes = section => categoryBoxes().filter(box => sectionOf(box) === section);

    // Records every container's children, so a failed save can put the page back exactly as it was.
    const takeSnapshot = () => [grid, ...linkLists()].map(container => [container, [...container.children]]);
    const restoreSnapshot = saved => {
        for (const [container, children] of saved ?? []) {
            container.append(...children);
        }
    };

    const setBusy = value => {
        busy = value;
        grid.classList.toggle('is-saving', value);
        for (const sortable of sortables) {
            sortable.option('disabled', value);
        }
    };

    // Runs one save at a time; on failure, undoes the move on the page and says why.
    const save = async (post, saved) => {
        setBusy(true);
        try {
            await post();
        } catch (error) {
            restoreSnapshot(saved);
            showToast(`Couldn't save the new order: ${error.message}`);
        } finally {
            setBusy(false);
        }
    };

    const saveCategoryOrder = section => window.h2sApi.postForm(categoriesUrl,
        sectionBoxes(section).map(box => ['orderedIds', box.dataset.categoryId]));

    const saveLinkOrder = list => window.h2sApi.postForm(linksUrl, [
        ['categoryId', list.dataset.categoryId],
        ...linkSlots(list).map(slot => ['orderedIds', slot.dataset.linkId])
    ]);

    // Categories: only within their own section, so the regular/admin break stays where it is.
    sortables.push(Sortable.create(grid, {
        handle: '.category-drag-handle',
        draggable: '.category-box',
        animation: 150,
        ghostClass: 'sortable-ghost',
        onStart: () => { snapshot = takeSnapshot(); },
        onMove: event => sectionOf(event.related) === sectionOf(event.dragged),
        onEnd: event => {
            if (event.oldDraggableIndex !== event.newDraggableIndex) {
                save(() => saveCategoryOrder(sectionOf(event.item)), snapshot);
            }
        }
    }));

    // Links: within a category or into another one. The server closes the gap a moved link leaves behind.
    for (const list of linkLists()) {
        sortables.push(Sortable.create(list, {
            group: 'links',
            handle: '.link-drag-handle',
            draggable: '.link-slot',
            animation: 150,
            ghostClass: 'sortable-ghost',
            onStart: () => { snapshot = takeSnapshot(); },
            onEnd: event => {
                if (event.from !== event.to || event.oldDraggableIndex !== event.newDraggableIndex) {
                    save(() => saveLinkOrder(event.to), snapshot);
                }
            }
        }));
    }

    // Keyboard: arrow keys on a focused handle move its item one place within its category or section.
    grid.addEventListener('keydown', event => {
        const handle = event.target.closest('.drag-handle');
        const step = { ArrowUp: -1, ArrowLeft: -1, ArrowDown: 1, ArrowRight: 1 }[event.key];

        if (!handle || !step) {
            return;
        }

        event.preventDefault();
        if (busy) {
            return;
        }

        const slot = handle.closest('.link-slot');
        const item = slot ?? handle.closest('.category-box');
        const siblings = slot ? linkSlots(slot.parentElement) : sectionBoxes(sectionOf(item));
        const index = siblings.indexOf(item);
        const neighbour = siblings[index + step];

        if (!neighbour) {
            return;
        }

        const saved = takeSnapshot();
        neighbour.insertAdjacentElement(step < 0 ? 'beforebegin' : 'afterend', item);
        handle.focus();

        const name = slot ? slot.querySelector('.link-block-title')?.textContent : item.querySelector('h2')?.textContent;
        status.textContent = `Moved ${name} to position ${index + step + 1} of ${siblings.length}.`;

        save(() => slot ? saveLinkOrder(slot.parentElement) : saveCategoryOrder(sectionOf(item)), saved);
    });
}
