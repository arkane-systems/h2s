/* Coordinates the settings UI for managing Uptime Kuma infrastructure groups. */
function initInfrastructureGroups(config) {
    const urls = config.urls;
    let groups = [...config.initialGroups];

    const message = document.getElementById('infrastructure-group-message');
    const tableBody = document.getElementById('infrastructure-group-table-body');
    const createForm = document.getElementById('create-infrastructure-group-form');
    const editCard = document.getElementById('edit-infrastructure-group-card');
    const editForm = document.getElementById('edit-infrastructure-group-form');
    const editId = document.getElementById('edit-infrastructure-group-id');
    const editName = document.getElementById('edit-infrastructure-group-name');
    const editMonitorId = document.getElementById('edit-infrastructure-group-monitor-id');
    const cancelEdit = document.getElementById('cancel-edit-infrastructure-group');
    const token = document.querySelector('#settings-antiforgery input[name="__RequestVerificationToken"]')?.value ?? '';
    const uptimeKumaServerUrl = document.getElementById('Settings_UptimeKumaServerUrl');
    const infrastructureGroupsSection = document.getElementById('infrastructure-groups-section');

    const escapeHtml = (value) => {
        const div = document.createElement('div');
        div.textContent = value ?? '';
        return div.innerHTML;
    };

    const normalizeGroup = (item) => ({
        id: item.id ?? item.Id,
        name: item.name ?? item.Name,
        monitorId: item.monitorId ?? item.MonitorId,
        sortOrder: item.sortOrder ?? item.SortOrder ?? 0
    });

    const setMessage = (text, isError = false) => {
        if (!message) {
            return;
        }

        message.textContent = text;
        message.classList.remove('d-none', 'alert-success', 'alert-danger');
        message.classList.add(isError ? 'alert-danger' : 'alert-success');
        refreshAppBackground();
    };

    const clearMessage = () => {
        if (!message) {
            return;
        }

        message.classList.add('d-none');
        message.textContent = '';
    };

    const refreshAppBackground = () => {
        window.dispatchEvent(new CustomEvent('app-background-refresh'));
    };

    const updateSectionVisibility = () => {
        if (!infrastructureGroupsSection || !uptimeKumaServerUrl) {
            return;
        }

        const hasServerUrl = uptimeKumaServerUrl.value.trim().length > 0;
        infrastructureGroupsSection.classList.toggle('d-none', !hasServerUrl);
        if (!hasServerUrl) {
            hideEdit();
            clearMessage();
        }

        refreshAppBackground();
    };

    const isPositiveInteger = (value) => /^[1-9][0-9]*$/.test(value ?? '');

    const postForm = async (url, data) => {
        const body = new URLSearchParams(data);
        body.set('__RequestVerificationToken', token);

        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                'RequestVerificationToken': token
            },
            body
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(errorText || 'Request failed.');
        }

        return response.json();
    };

    const getJson = async (url) => {
        const response = await fetch(url, { cache: 'no-store' });
        if (!response.ok) {
            throw new Error('Unable to load groups.');
        }

        return response.json();
    };

    const hideEdit = () => {
        editCard?.classList.add('d-none');
        editForm?.reset();
        if (editId) {
            editId.value = '';
        }
    };

    const render = () => {
        const ordered = groups.map(normalizeGroup).sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name));
        tableBody.innerHTML = ordered.map((group) => `
            <tr data-group-id="${group.id}"
                data-group-name="${escapeHtml(group.name)}"
                data-group-monitor-id="${escapeHtml(group.monitorId)}">
                <td>${escapeHtml(group.name)}</td>
                <td>${escapeHtml(group.monitorId)}</td>
                <td class="text-end">
                    <button type="button" class="btn btn-sm btn-outline-secondary" data-action="move-up">↑</button>
                    <button type="button" class="btn btn-sm btn-outline-secondary" data-action="move-down">↓</button>
                    <button type="button" class="btn btn-sm btn-outline-primary" data-action="edit">Edit</button>
                    <button type="button" class="btn btn-sm btn-outline-danger" data-action="delete">Delete</button>
                </td>
            </tr>`).join('');
    };

    const loadGroups = async () => {
        groups = await getJson(urls.groups);
        render();
        refreshAppBackground();
    };

    const submitReorder = async () => {
        const orderedIds = groups
            .map(normalizeGroup)
            .sort((a, b) => a.sortOrder - b.sortOrder)
            .map((g) => g.id.toString());

        await postForm(urls.reorderGroups, orderedIds.map((id) => ['orderedIds', id]));
        await loadGroups();
    };

    createForm?.addEventListener('submit', async (event) => {
        event.preventDefault();
        clearMessage();

        const formData = new FormData(createForm);
        const name = (formData.get('name') ?? '').toString().trim();
        const monitorId = (formData.get('monitorId') ?? '').toString().trim();

        if (!name) {
            setMessage('Group name is required.', true);
            return;
        }

        if (!isPositiveInteger(monitorId)) {
            setMessage('Monitor ID must be a positive integer.', true);
            return;
        }

        try {
            await postForm(urls.createGroup, { name, monitorId });
            createForm.reset();
            await loadGroups();
            setMessage('Infrastructure group added.');
            refreshAppBackground();
        } catch {
            setMessage('Failed to add infrastructure group.', true);
        }
    });

    tableBody?.addEventListener('click', async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const action = target.getAttribute('data-action');
        if (!action) {
            return;
        }

        const row = target.closest('tr[data-group-id]');
        if (!row) {
            return;
        }

        const id = Number(row.getAttribute('data-group-id'));
        const name = row.getAttribute('data-group-name') ?? '';
        const monitorId = row.getAttribute('data-group-monitor-id') ?? '';
        const ordered = groups.map(normalizeGroup).sort((a, b) => a.sortOrder - b.sortOrder);
        const index = ordered.findIndex((g) => g.id === id);

        clearMessage();

        if (action === 'edit') {
            if (editId) editId.value = id.toString();
            if (editName) editName.value = name;
            if (editMonitorId) editMonitorId.value = monitorId;
            editCard?.classList.remove('d-none');
            editName?.focus();
            return;
        }

        if (action === 'delete') {
            if (!confirm(`Delete infrastructure group "${name}"?`)) {
                return;
            }

            try {
                await postForm(urls.deleteGroup, { id: id.toString() });
                hideEdit();
                await loadGroups();
                setMessage('Infrastructure group deleted.');
                refreshAppBackground();
            } catch {
                setMessage('Failed to delete infrastructure group.', true);
            }
            return;
        }

        if (action === 'move-up' || action === 'move-down') {
            if (index < 0) {
                return;
            }

            const targetIndex = action === 'move-up' ? index - 1 : index + 1;
            if (targetIndex < 0 || targetIndex >= ordered.length) {
                return;
            }

            const temp = ordered[index];
            ordered[index] = ordered[targetIndex];
            ordered[targetIndex] = temp;

            groups = ordered.map((g, sortOrder) => ({
                ...g,
                sortOrder
            }));

            try {
                await submitReorder();
                setMessage('Infrastructure groups reordered.');
            } catch {
                setMessage('Failed to reorder infrastructure groups.', true);
            }
        }
    });

    editForm?.addEventListener('submit', async (event) => {
        event.preventDefault();
        clearMessage();

        const id = editId?.value ?? '';
        const name = editName?.value.trim() ?? '';
        const monitorId = editMonitorId?.value.trim() ?? '';

        if (!id || !name) {
            setMessage('Group name is required.', true);
            return;
        }

        if (!isPositiveInteger(monitorId)) {
            setMessage('Monitor ID must be a positive integer.', true);
            return;
        }

        try {
            await postForm(urls.updateGroup, { id, name, monitorId });
            hideEdit();
            await loadGroups();
            setMessage('Infrastructure group updated.');
            refreshAppBackground();
        } catch {
            setMessage('Failed to update infrastructure group.', true);
        }
    });

    cancelEdit?.addEventListener('click', () => {
        hideEdit();
        clearMessage();
    });

    uptimeKumaServerUrl?.addEventListener('input', () => {
        updateSectionVisibility();
    });

    uptimeKumaServerUrl?.addEventListener('change', () => {
        updateSectionVisibility();
    });

    render();
    updateSectionVisibility();
    refreshAppBackground();
}
