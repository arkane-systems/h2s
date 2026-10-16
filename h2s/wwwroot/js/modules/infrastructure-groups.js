/* Settings → Integrations → Infrastructure groups: a list that reorders by drag and drop (or arrow keys on a handle),
   with a small dialog to add, edit and delete groups (with Undo). The list is rendered here from JSON, so every change
   just refreshes it. */
import Sortable from '../../lib/sortablejs/modular/sortable.esm.js';
import { MonitorPicker } from './monitor-picker.js';
import { showToast } from './toast.js';

const root = document.getElementById('infrastructure-groups');

if (root) {
    const urls = root.dataset;
    const list = root.querySelector('[data-role="group-list"]');
    const empty = root.querySelector('[data-role="group-empty"]');
    let groups = JSON.parse(document.getElementById('infrastructure-groups-data').textContent);
    let busy = false;

    const render = () => {
        list.replaceChildren(...groups.map(group => {
            const item = document.createElement('li');
            item.className = 'list-group-item d-flex align-items-center gap-2';
            item.dataset.id = group.id;

            const handle = document.createElement('button');
            handle.type = 'button';
            handle.className = 'drag-handle';
            handle.title = 'Drag to move';
            handle.setAttribute('aria-label', `Move ${group.name}`);
            handle.textContent = '⠿';

            const edit = document.createElement('button');
            edit.type = 'button';
            edit.className = 'btn btn-link p-0 text-start text-decoration-none me-auto';
            edit.dataset.role = 'edit';
            edit.textContent = group.name;

            const monitor = document.createElement('span');
            monitor.className = 'text-muted small';
            monitor.textContent = `Monitor #${group.monitorId}`;

            item.append(handle, edit, monitor);
            return item;
        }));
        empty.classList.toggle('d-none', groups.length > 0);
    };

    const refresh = async () => {
        try {
            groups = await window.h2sApi.getJson(urls.groupsUrl);
        } catch (error) {
            showToast(`Couldn't refresh the groups: ${error.message}`);
        }
        render();
    };

    // Saves the order shown; on failure, puts the list back as it was.
    const saveOrder = async () => {
        const ids = [...list.children].map(item => Number(item.dataset.id));
        busy = true;
        try {
            await window.h2sApi.postForm(urls.reorderUrl, ids.map(id => ['orderedIds', id]));
            groups = ids.map(id => groups.find(group => group.id === id));
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
    const dialogElement = document.getElementById('infrastructure-group-dialog');
    const modal = window.bootstrap.Modal.getOrCreateInstance(dialogElement);
    const form = dialogElement.querySelector('form');
    const title = dialogElement.querySelector('.modal-title');
    const error = dialogElement.querySelector('[data-role="error"]');
    const deleteButton = dialogElement.querySelector('[data-role="delete"]');
    const picker = new MonitorPicker(dialogElement.querySelector('[data-role="monitor-select"]'), form.elements.monitorId,
        dialogElement.querySelector('[data-role="monitor-status"]'), dialogElement.dataset.monitorsUrl);
    let editing = null;

    const showError = message => {
        error.textContent = message;
        error.classList.toggle('d-none', !message);
    };

    const open = async group => {
        editing = group;
        form.reset();
        form.classList.remove('was-validated');
        showError('');
        title.textContent = group ? 'Edit infrastructure group' : 'Add infrastructure group';
        deleteButton.classList.toggle('d-none', !group);
        form.elements.name.value = group?.name ?? '';
        await picker.setValue(group?.monitorId ?? '');
        modal.show();
    };

    dialogElement.addEventListener('shown.bs.modal', () => form.elements.name.focus());

    form.addEventListener('submit', async event => {
        event.preventDefault();
        form.classList.add('was-validated');
        if (!form.checkValidity()) {
            form.querySelector(':invalid')?.focus();
            return;
        }
        // The monitor is required, but its field is hidden while the picker shows a listed monitor.
        if (!form.elements.monitorId.value.trim()) {
            showError('Choose a monitor, or enter its ID.');
            return;
        }

        showError('');
        const data = { name: form.elements.name.value.trim(), monitorId: form.elements.monitorId.value.trim() };

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
                            showToast(`Couldn't restore the group: ${problem.message}`);
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
            open(groups.find(group => group.id === id));
        }
    });

    root.querySelector('[data-role="add-group"]').addEventListener('click', () => open(null));

    render();
}
