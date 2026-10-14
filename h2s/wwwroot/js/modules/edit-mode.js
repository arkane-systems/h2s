/* Dashboard edit mode (/?edit=true): rearranges categories and links by drag and drop or the keyboard, saving each move,
   and adds, edits and deletes them through the link and category dialogs, with Undo for deletes. */
import Sortable from '../../lib/sortablejs/modular/sortable.esm.js';
import { CategoryDialog } from './category-dialog.js';
import { LinkDialog } from './link-dialog.js';
import { showToast } from './toast.js';

const config = document.getElementById('edit-mode');
const grid = document.querySelector('.category-grid.is-editing');

if (config && grid) {
    initEditMode(config, grid);
}

function initEditMode(config, grid) {
    const categoriesUrl = config.dataset.reorderCategoriesUrl;
    const linksUrl = config.dataset.reorderLinksUrl;
    const linkDialogElement = document.getElementById('link-dialog');
    const sortables = [];
    let busy = false;
    let snapshot = null;

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
    const listFor = categoryId => grid.querySelector(`.link-block-list[data-category-id="${CSS.escape(String(categoryId))}"]`);
    const labelOf = slot => slot.querySelector('.link-block-title')?.textContent ?? 'link';

    // Tiles keep their markup, but in edit mode they open the link dialog rather than the link.
    const prepareSlot = slot => {
        const tile = slot.querySelector('.link-block');
        tile?.setAttribute('role', 'button');
        tile?.setAttribute('aria-label', `Edit ${labelOf(slot)}`);
        return slot;
    };
    grid.querySelectorAll('.link-slot').forEach(prepareSlot);

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

    // Fetches a saved link as edit mode renders it.
    const fetchSlot = async linkId => {
        const url = new URL(linkDialogElement.dataset.linkUrl, window.location.origin);
        url.searchParams.set('handler', 'LinkSlot');
        url.searchParams.set('id', linkId);
        const template = document.createElement('template');
        template.innerHTML = (await window.h2sApi.getText(url.pathname + url.search)).trim();
        return prepareSlot(template.content.firstElementChild);
    };

    const linkDialog = new LinkDialog(linkDialogElement, {
        // New links, and links moved to another category, go at the end of it, as the server placed them.
        onSaved: async (link, isNew) => {
            try {
                const slot = await fetchSlot(link.id);
                const existing = grid.querySelector(`.link-slot[data-link-id="${link.id}"]`);
                if (existing && existing.parentElement === listFor(link.categoryId)) {
                    existing.replaceWith(slot);
                } else {
                    existing?.remove();
                    listFor(link.categoryId).append(slot);
                }
                showToast(`${isNew ? 'Added' : 'Saved'} “${link.label}”.`, { variant: 'success', delay: 3000 });
            } catch {
                window.location.reload();
            }
        },
        onDeleted: result => {
            grid.querySelector(`.link-slot[data-link-id="${result.deletedId}"]`)?.remove();
            showToast(`Deleted “${result.restore.label}”.`, {
                variant: 'secondary',
                delay: 8000,
                action: { label: 'Undo', onClick: () => restoreLink(result.restore) }
            });
        }
    });

    const restoreLink = async restore => {
        try {
            const link = await window.h2sApi.postForm(linkDialogElement.dataset.restoreUrl, { snapshot: JSON.stringify(restore) });
            const list = listFor(link.categoryId);
            if (!list) {
                window.location.reload();
                return;
            }
            const slot = await fetchSlot(link.id);
            list.insertBefore(slot, linkSlots(list)[link.sortOrder] ?? null);
        } catch (error) {
            showToast(`Couldn't restore the link: ${error.message}`);
        }
    };

    const categoryDialogElement = document.getElementById('category-dialog');
    const categoryDialog = new CategoryDialog(categoryDialogElement, {
        onSaved: (category, previous) => {
            // A new category, or one that changed section, needs the page laid out again.
            if (!previous || String(category.isAdminCategory) !== String(previous.isAdminCategory)) {
                window.location.reload();
                return;
            }

            const box = grid.querySelector(`.category-box[data-category-id="${category.id}"]`);
            box.querySelector('h2').textContent = category.name;
            box.querySelector('.category-drag-handle').setAttribute('aria-label', `Move category ${category.name}`);
            box.querySelector('[data-role="edit-category"]').setAttribute('aria-label', `Edit category ${category.name}`);
            const option = linkDialogElement.querySelector(`option[value="${category.id}"]`);
            if (option) {
                option.textContent = category.name + (category.isAdminCategory ? ' (admin)' : '');
            }
            showToast(`Saved “${category.name}”.`, { variant: 'success', delay: 3000 });
        },
        onDeleted: result => {
            grid.querySelector(`.category-box[data-category-id="${result.deletedId}"]`)?.remove();
            linkDialogElement.querySelector(`option[value="${result.deletedId}"]`)?.remove();
            showToast(`Deleted “${result.restore.name}” and its links.`, {
                variant: 'secondary',
                delay: 8000,
                action: {
                    label: 'Undo',
                    onClick: async () => {
                        try {
                            await window.h2sApi.postForm(categoryDialogElement.dataset.restoreUrl,
                                { snapshot: JSON.stringify(result.restore) });
                            window.location.reload();
                        } catch (error) {
                            showToast(`Couldn't restore the category: ${error.message}`);
                        }
                    }
                }
            });
        }
    });

    config.querySelector('[data-role="add-category"]')?.addEventListener('click', () => categoryDialog.openNew());

    grid.addEventListener('click', event => {
        const tile = event.target.closest('.link-slot .link-block');
        if (tile) {
            event.preventDefault();
            linkDialog.openExisting(tile.closest('.link-slot').dataset.linkId)
                .catch(error => showToast(`Couldn't open the link: ${error.message}`));
            return;
        }

        const box = event.target.closest('.category-box');
        if (event.target.closest('[data-role="add-link"]')) {
            linkDialog.openNew(box.dataset.categoryId);
        } else if (event.target.closest('[data-role="edit-category"]')) {
            categoryDialog.openExisting({
                id: box.dataset.categoryId,
                name: box.querySelector('h2').textContent,
                isAdminCategory: sectionOf(box) === 'true'
            });
        }
    });

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

        const name = slot ? labelOf(slot) : item.querySelector('h2')?.textContent;
        status.textContent = `Moved ${name} to position ${index + step + 1} of ${siblings.length}.`;

        save(() => slot ? saveLinkOrder(slot.parentElement) : saveCategoryOrder(sectionOf(item)), saved);
    });
}
