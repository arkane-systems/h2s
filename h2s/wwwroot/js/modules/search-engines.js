/* Settings → Search: the search engines, as a list that reorders by drag and drop (or arrow keys on a handle), with a
   small dialog to add, edit and delete them (with Undo). The list is rendered here from JSON, so every change just
   refreshes it. */
import Sortable from '../../lib/sortablejs/modular/sortable.esm.js';
import { showToast } from './toast.js';

const root = document.getElementById('search-engines');

if (root) {
    const urls = root.dataset;
    const list = root.querySelector('[data-role="engine-list"]');
    const empty = root.querySelector('[data-role="engine-empty"]');
    let engines = JSON.parse(document.getElementById('search-engines-data').textContent);
    let busy = false;

    const badge = (text, className) => {
        const element = document.createElement('span');
        element.className = `badge ${className}`;
        element.textContent = text;
        return element;
    };

    const render = () => {
        list.replaceChildren(...engines.map(engine => {
            const item = document.createElement('li');
            item.className = 'list-group-item d-flex align-items-center gap-2';
            item.dataset.id = engine.id;

            const handle = document.createElement('button');
            handle.type = 'button';
            handle.className = 'drag-handle';
            handle.title = 'Drag to move';
            handle.setAttribute('aria-label', `Move ${engine.name}`);
            handle.textContent = '⠿';

            const edit = document.createElement('button');
            edit.type = 'button';
            edit.className = 'btn btn-link p-0 text-start text-decoration-none me-auto';
            edit.dataset.role = 'edit';
            edit.textContent = engine.name;

            item.append(handle, edit);
            if (engine.isDefault) {
                item.append(badge('Default', 'text-bg-primary'));
            }
            if (engine.shortcut) {
                item.append(badge(`!${engine.shortcut}`, 'text-bg-secondary'));
            }
            return item;
        }));
        empty.classList.toggle('d-none', engines.length > 0);
    };

    const refresh = async () => {
        try {
            engines = await window.h2sApi.getJson(urls.enginesUrl);
        } catch (error) {
            showToast(`Couldn't refresh the search engines: ${error.message}`);
        }
        render();
    };

    // Saves the order shown; on failure, puts the list back as it was.
    const saveOrder = async () => {
        const ids = [...list.children].map(item => Number(item.dataset.id));
        busy = true;
        try {
            await window.h2sApi.postForm(urls.reorderUrl, ids.map(id => ['orderedIds', id]));
            engines = ids.map(id => engines.find(engine => engine.id === id));
        } catch (error) {
            showToast(`Couldn't save the new order: ${error.message}`);
            render();
        } finally {
            busy = false;
        }
    };

    Sortable.create(list, {
        handle: '.drag-handle',
        // No animation for people who asked for less motion.
        animation: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 150,
        ghostClass: 'sortable-ghost',
        onEnd: event => {
            if (event.oldIndex !== event.newIndex) {
                saveOrder();
            }
        }
    });

    list.addEventListener('keydown', event => {
        const handle = event.target.closest('.drag-handle');
        const step = { ArrowUp: -1, ArrowLeft: -1, ArrowDown: 1, ArrowRight: 1 }[event.key];
        if (!handle || !step) {
            return;
        }
        event.preventDefault();

        const item = handle.closest('li');
        const neighbour = step < 0 ? item.previousElementSibling : item.nextElementSibling;
        if (busy || !neighbour) {
            return;
        }
        neighbour.insertAdjacentElement(step < 0 ? 'beforebegin' : 'afterend', item);
        handle.focus();
        saveOrder();
    });

    // The add/edit dialog.
    const dialogElement = document.getElementById('search-engine-dialog');
    const modal = window.bootstrap.Modal.getOrCreateInstance(dialogElement);
    const form = dialogElement.querySelector('form');
    const fields = form.elements;
    const title = dialogElement.querySelector('.modal-title');
    const error = dialogElement.querySelector('[data-role="error"]');
    const shortcutInvalid = dialogElement.querySelector('[data-role="shortcut-invalid"]');
    const deleteButton = dialogElement.querySelector('[data-role="delete"]');
    let editing = null;

    const showError = message => {
        error.textContent = message;
        error.classList.toggle('d-none', !message);
    };

    const open = engine => {
        editing = engine;
        form.reset();
        form.classList.remove('was-validated');
        shortcutInvalid.classList.add('d-none');
        showError('');
        title.textContent = engine ? 'Edit search engine' : 'Add search engine';
        deleteButton.classList.toggle('d-none', !engine);
        fields.name.value = engine?.name ?? '';
        fields.urlTemplate.value = engine?.urlTemplate ?? '';
        fields.shortcut.value = engine?.shortcut ?? '';

        // The first engine is always the default, and the default can't be unmarked; another engine takes it over.
        const isDefault = engine ? engine.isDefault : engines.length === 0;
        fields.isDefault.checked = isDefault;
        fields.isDefault.disabled = isDefault;
        modal.show();
    };

    dialogElement.addEventListener('shown.bs.modal', () => fields.name.focus());

    form.addEventListener('submit', async event => {
        event.preventDefault();
        form.classList.add('was-validated');
        // The shortcut's input group keeps Bootstrap's sibling rule from showing its message, so show it here.
        shortcutInvalid.classList.toggle('d-none', fields.shortcut.validity.valid);
        if (!form.checkValidity()) {
            form.querySelector(':invalid')?.focus();
            return;
        }

        showError('');
        const data = {
            name: fields.name.value.trim(),
            urlTemplate: fields.urlTemplate.value.trim(),
            shortcut: fields.shortcut.value.trim(),
            isDefault: fields.isDefault.checked ? 'true' : 'false'
        };

        try {
            const saved = editing
                ? await window.h2sApi.postForm(urls.updateUrl, { id: editing.id, ...data })
                : await window.h2sApi.postForm(urls.createUrl, data);
            modal.hide();
            await refresh();
            showToast(`${editing ? 'Saved' : 'Added'} “${saved.name}”.`, { variant: 'success', delay: 3000 });
        } catch (problem) {
            showError(problem.message);
        }
    });

    deleteButton.addEventListener('click', async () => {
        try {
            const result = await window.h2sApi.postForm(urls.deleteUrl, { id: editing.id });
            modal.hide();
            await refresh();
            showToast(`Deleted “${result.restore.name}”.`, {
                variant: 'secondary',
                delay: 8000,
                action: {
                    label: 'Undo',
                    onClick: async () => {
                        try {
                            await window.h2sApi.postForm(urls.restoreUrl, result.restore);
                        } catch (problem) {
                            showToast(`Couldn't restore the search engine: ${problem.message}`);
                        }
                        await refresh();
                    }
                }
            });
        } catch (problem) {
            showError(problem.message);
        }
    });

    list.addEventListener('click', event => {
        if (event.target.closest('[data-role="edit"]')) {
            const id = Number(event.target.closest('li').dataset.id);
            open(engines.find(engine => engine.id === id));
        }
    });

    root.querySelector('[data-role="add-engine"]').addEventListener('click', () => open(null));

    render();
}
