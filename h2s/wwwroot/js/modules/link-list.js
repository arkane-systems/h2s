/* The list view (Pages/Admin/Editor): every link in a filterable, sortable table, with bulk move and delete (with Undo).
   Rows open the shared link dialog. The table is rendered here from JSON, so every change just refreshes the data. */
import { LinkDialog } from './link-dialog.js';
import { showToast } from './toast.js';

const data = JSON.parse(document.getElementById('link-list-data').textContent);
const urls = data.urls;
const root = document.querySelector('.list-view');

const filterInput = root.querySelector('[data-role="filter"]');
const categoryFilter = root.querySelector('[data-role="category-filter"]');
const rows = root.querySelector('[data-role="rows"]');
const empty = root.querySelector('[data-role="empty"]');
const selectAll = root.querySelector('[data-role="select-all"]');
const bulkBar = root.querySelector('[data-role="bulk-bar"]');
const selectionCount = root.querySelector('[data-role="selection-count"]');
const moveTarget = root.querySelector('[data-role="move-target"]');

// The category filter lists categories in dashboard order, which is also the order the Category column sorts by.
const categoryOrder = new Map([...categoryFilter.options].filter(o => o.value).map((o, i) => [Number(o.value), i]));

let links = data.links;
let selected = new Set();
let sort = { key: 'category', descending: false };

const compare = {
    label: (a, b) => a.label.localeCompare(b.label, undefined, { sensitivity: 'base' }),
    url: (a, b) => a.url.localeCompare(b.url),
    category: (a, b) => (categoryOrder.get(a.categoryId) ?? 0) - (categoryOrder.get(b.categoryId) ?? 0) || a.sortOrder - b.sortOrder,
    monitor: (a, b) => (a.monitorId ?? Infinity) - (b.monitorId ?? Infinity)
};

const shownLinks = () => {
    const text = filterInput.value.trim().toLowerCase();
    const category = categoryFilter.value;

    return links
        .filter(link => !category || String(link.categoryId) === category)
        .filter(link => !text || [link.label, link.url, link.description, link.iconName].some(v => v.toLowerCase().includes(text)))
        .sort((a, b) => (sort.descending ? -1 : 1) * compare[sort.key](a, b));
};

const cell = (...children) => {
    const td = document.createElement('td');
    td.append(...children);
    return td;
};

const renderRow = link => {
    const row = document.createElement('tr');
    row.dataset.id = link.id;

    const check = document.createElement('input');
    check.type = 'checkbox';
    check.className = 'form-check-input';
    check.checked = selected.has(link.id);
    check.dataset.role = 'select';
    check.setAttribute('aria-label', `Select ${link.label}`);

    let icon;
    if (link.iconSrc) {
        icon = document.createElement('img');
        icon.className = 'link-block-icon list-view-icon-image';
        icon.src = link.iconSrc;
        icon.alt = '';
        icon.loading = 'lazy';
    } else {
        icon = document.createElement('span');
        icon.className = 'link-block-icon-fallback';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = '•';
    }

    // The label is a button, so the row's dialog can be opened from the keyboard too.
    const edit = document.createElement('button');
    edit.type = 'button';
    edit.className = 'btn btn-link p-0 text-start fw-semibold text-decoration-none';
    edit.dataset.role = 'edit';
    edit.textContent = link.label;
    const labelCell = cell(edit);
    if (link.openInNewTab) {
        const newTab = document.createElement('span');
        newTab.className = 'text-muted ms-1';
        newTab.title = 'Opens in a new tab';
        newTab.setAttribute('aria-label', 'Opens in a new tab');
        newTab.textContent = '↗';
        labelCell.append(newTab);
    }
    if (link.description) {
        const description = document.createElement('div');
        description.className = 'small text-muted';
        description.textContent = link.description;
        labelCell.append(description);
    }

    const anchor = document.createElement('a');
    anchor.href = link.url;
    anchor.target = '_blank';
    anchor.rel = 'noopener';
    anchor.className = 'list-view-url';
    anchor.textContent = link.url;

    const category = document.createElement('span');
    category.textContent = link.categoryName;
    const categoryCell = cell(category);
    if (link.isAdminCategory) {
        const badge = document.createElement('span');
        badge.className = 'badge text-bg-danger-subtle text-danger-emphasis ms-1';
        badge.textContent = 'admin';
        categoryCell.append(badge);
    }

    row.append(cell(check), cell(icon), labelCell, cell(anchor), categoryCell);
    if (data.showMonitors) {
        row.append(cell(document.createTextNode(link.monitorId ? `#${link.monitorId}` : '')));
    }
    return row;
};

const render = () => {
    const shown = shownLinks();
    rows.replaceChildren(...shown.map(renderRow));

    empty.textContent = links.length === 0 ? 'No links yet.' : 'No links match.';
    empty.classList.toggle('d-none', shown.length > 0);

    const shownSelected = shown.filter(link => selected.has(link.id)).length;
    selectAll.checked = shown.length > 0 && shownSelected === shown.length;
    selectAll.indeterminate = shownSelected > 0 && shownSelected < shown.length;

    bulkBar.classList.toggle('d-none', selected.size === 0);
    selectionCount.textContent = `${selected.size} selected`;

    for (const header of root.querySelectorAll('th[aria-sort]')) {
        const key = header.querySelector('[data-sort]').dataset.sort;
        header.setAttribute('aria-sort', key !== sort.key ? 'none' : sort.descending ? 'descending' : 'ascending');
    }
};

const refresh = async () => {
    try {
        links = await window.h2sApi.getJson(urls.links);
    } catch (error) {
        showToast(`Couldn't refresh the list: ${error.message}`);
        return;
    }
    const existing = new Set(links.map(link => link.id));
    selected = new Set([...selected].filter(id => existing.has(id)));
    render();
};

// Restores deleted links one at a time, in the order the server gave, which puts each back where it was.
const restore = async snapshots => {
    try {
        for (const snapshot of snapshots) {
            await window.h2sApi.postForm(urls.restoreLink, { snapshot: JSON.stringify(snapshot) });
        }
    } catch (error) {
        showToast(`Couldn't restore everything: ${error.message}`);
    }
    await refresh();
};

const dialog = new LinkDialog(document.getElementById('link-dialog'), {
    onSaved: async (link, isNew) => {
        await refresh();
        showToast(`${isNew ? 'Added' : 'Saved'} “${link.label}”.`, { variant: 'success', delay: 3000 });
    },
    onDeleted: async result => {
        selected.delete(result.deletedId);
        await refresh();
        showToast(`Deleted “${result.restore.label}”.`, {
            variant: 'secondary',
            delay: 8000,
            action: { label: 'Undo', onClick: () => restore([result.restore]) }
        });
    }
});

filterInput.addEventListener('input', render);
categoryFilter.addEventListener('change', render);

root.querySelector('thead').addEventListener('click', event => {
    const button = event.target.closest('[data-sort]');
    if (!button) {
        return;
    }
    sort = { key: button.dataset.sort, descending: sort.key === button.dataset.sort && !sort.descending };
    render();
});

selectAll.addEventListener('change', () => {
    for (const link of shownLinks()) {
        if (selectAll.checked) {
            selected.add(link.id);
        } else {
            selected.delete(link.id);
        }
    }
    render();
});

rows.addEventListener('change', event => {
    if (event.target.dataset.role === 'select') {
        const id = Number(event.target.closest('tr').dataset.id);
        if (event.target.checked) {
            selected.add(id);
        } else {
            selected.delete(id);
        }
        render();
    }
});

// A click anywhere on a row opens it, except on its checkbox or its link.
rows.addEventListener('click', event => {
    const row = event.target.closest('tr');
    if (!row || event.target.closest('input, a')) {
        return;
    }
    dialog.openExisting(Number(row.dataset.id)).catch(error => showToast(`Couldn't open the link: ${error.message}`));
});

root.querySelector('[data-role="add-link"]').addEventListener('click', () => {
    dialog.openNew(categoryFilter.value || categoryFilter.options[1]?.value);
});

root.querySelector('[data-role="clear-selection"]').addEventListener('click', () => {
    selected.clear();
    render();
});

root.querySelector('[data-role="move-selected"]').addEventListener('click', async () => {
    if (!moveTarget.value) {
        moveTarget.focus();
        return;
    }
    // Move in the order the table shows them.
    const ids = shownLinks().filter(link => selected.has(link.id)).map(link => link.id);
    const hidden = [...selected].filter(id => !ids.includes(id));
    const target = moveTarget.selectedOptions[0].textContent;

    try {
        await window.h2sApi.postForm(urls.moveLinks,
            [['categoryId', moveTarget.value], ...[...ids, ...hidden].map(id => ['orderedIds', id])]);
        showToast(`Moved ${selected.size} link${selected.size === 1 ? '' : 's'} to ${target}.`, { variant: 'success', delay: 3000 });
        selected.clear();
        moveTarget.value = '';
    } catch (error) {
        showToast(`Couldn't move the links: ${error.message}`);
    }
    await refresh();
});

root.querySelector('[data-role="delete-selected"]').addEventListener('click', async () => {
    const count = selected.size;

    try {
        const result = await window.h2sApi.postForm(urls.deleteLinks, [...selected].map(id => ['ids', id]));
        selected.clear();
        await refresh();
        showToast(`Deleted ${count} link${count === 1 ? '' : 's'}.`, {
            variant: 'secondary',
            delay: 8000,
            action: { label: 'Undo', onClick: () => restore(result.restore) }
        });
    } catch (error) {
        showToast(`Couldn't delete the links: ${error.message}`);
        await refresh();
    }
});

render();
