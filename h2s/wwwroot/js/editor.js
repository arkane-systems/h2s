/* Coordinates the admin editor UI for managing categories, links, and icon suggestions. */
function initEditor(config) {
    const urls = config.urls;

    let categories = [...config.initialCategories];
    let links = [...config.initialLinks];

    const clientErrorBanner = document.getElementById('editor-client-error');

    // Displays unexpected JavaScript errors in development so editor issues are easier to diagnose.
    const showClientError = (message) => {
        if (!clientErrorBanner) {
            return;
        }

        clientErrorBanner.textContent = `Editor script error: ${message}`;
        clientErrorBanner.classList.remove('d-none');
    };

    window.addEventListener('error', (event) => {
        showClientError(event.message || 'Unexpected JavaScript error.');
    });

    window.addEventListener('unhandledrejection', (event) => {
        const reason = event.reason;
        const message = reason instanceof Error ? reason.message : (reason?.toString?.() ?? 'Unhandled promise rejection.');
        showClientError(message);
    });

    const token = document.querySelector('#editor-antiforgery input[name="__RequestVerificationToken"]')?.value ?? '';

    const categoryElements = {
        message: document.getElementById('category-message'),
        tableBody: document.getElementById('category-table-body'),
        createForm: document.getElementById('create-category-form'),
        editCard: document.getElementById('edit-category-card'),
        editForm: document.getElementById('edit-category-form'),
        editId: document.getElementById('edit-category-id'),
        editName: document.getElementById('edit-category-name'),
        editAdmin: document.getElementById('edit-category-is-admin'),
        cancelEdit: document.getElementById('cancel-edit-category')
    };

    const linkElements = {
        message: document.getElementById('link-message'),
        tableBody: document.getElementById('link-table-body'),
        createForm: document.getElementById('create-link-form'),
        editCard: document.getElementById('edit-link-card'),
        editForm: document.getElementById('edit-link-form'),
        editId: document.getElementById('edit-link-id'),
        editCategoryId: document.getElementById('edit-link-category-id'),
        editLabel: document.getElementById('edit-link-label'),
        editDescription: document.getElementById('edit-link-description'),
        editIconName: document.getElementById('edit-link-icon-name'),
        editUrl: document.getElementById('edit-link-url'),
        cancelEdit: document.getElementById('cancel-edit-link'),
        createCategoryId: document.getElementById('create-link-category-id'),
        createLabel: document.getElementById('create-link-label'),
        createIconName: document.getElementById('create-link-icon-name'),
        suggestionWrap: document.getElementById('create-link-icon-suggestion'),
        suggestionName: document.getElementById('create-link-icon-suggestion-name'),
        applySuggestion: document.getElementById('apply-link-icon-suggestion')
    };

    // Escapes user-provided values before inserting them back into rendered HTML.
    const escapeHtml = (value) => {
        const div = document.createElement('div');
        div.textContent = value ?? '';
        return div.innerHTML;
    };

    // Normalizes category payloads from either server-rendered data or JSON responses.
    const normalizeCategory = (item) => ({
        id: item.id ?? item.Id,
        name: item.name ?? item.Name,
        isAdminCategory: item.isAdminCategory ?? item.IsAdminCategory,
        linkCount: item.linkCount ?? item.LinkCount ?? 0
    });

    // Normalizes link payloads from either server-rendered data or JSON responses.
    const normalizeLink = (item) => ({
        id: item.id ?? item.Id,
        categoryId: item.categoryId ?? item.CategoryId,
        categoryName: item.categoryName ?? item.CategoryName ?? '',
        isAdminCategory: item.isAdminCategory ?? item.IsAdminCategory ?? false,
        label: item.label ?? item.Label,
        description: item.description ?? item.Description ?? '',
        iconName: item.iconName ?? item.IconName ?? '',
        url: item.url ?? item.Url
    });

    // Mirrors the server-side URL validation rule so invalid values can be rejected immediately.
    const isValidWebUrl = (value) => {
        if (!value) {
            return false;
        }

        try {
            const parsed = new URL(value);
            return parsed.protocol === 'http:' || parsed.protocol === 'https:';
        } catch {
            return false;
        }
    };

    // Keeps category ordering aligned with the dashboard and editor server queries.
    const sortCategories = (items) => items
        .slice()
        .sort((a, b) => Number(a.isAdminCategory) - Number(b.isAdminCategory) || a.name.localeCompare(b.name));

    // Keeps link ordering aligned with the editor server queries.
    const sortLinks = (items) => items
        .slice()
        .sort((a, b) => Number(a.isAdminCategory) - Number(b.isAdminCategory) || a.categoryName.localeCompare(b.categoryName) || a.label.localeCompare(b.label));

    // Shows a success or error banner for the supplied editor section.
    const setMessage = (element, text, isError = false) => {
        element.textContent = text;
        element.classList.remove('d-none', 'alert-success', 'alert-danger');
        element.classList.add(isError ? 'alert-danger' : 'alert-success');
    };

    // Hides any banner previously shown for the supplied editor section.
    const clearMessage = (element) => {
        element.classList.add('d-none');
        element.textContent = '';
    };

    // Posts form-style data to a Razor Pages handler while including the anti-forgery token.
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

    // Fetches JSON from a page handler without allowing cached responses.
    const getJson = async (url) => {
        const response = await fetch(url, { cache: 'no-store' });
        if (!response.ok) {
            throw new Error('Unable to load data.');
        }

        return response.json();
    };

    let iconSuggestionDebounceHandle;
    let iconSuggestionRequestId = 0;
    let currentSuggestedIconName = '';

    // Clears the current icon suggestion UI state.
    const hideIconSuggestion = () => {
        currentSuggestedIconName = '';
        if (!linkElements.suggestionName || !linkElements.suggestionWrap) {
            return;
        }

        linkElements.suggestionName.textContent = '';
        linkElements.suggestionWrap.classList.add('d-none');
    };

    // Displays the current icon suggestion beneath the create-link form.
    const showIconSuggestion = (iconName) => {
        currentSuggestedIconName = iconName;
        if (!linkElements.suggestionName || !linkElements.suggestionWrap) {
            return;
        }

        linkElements.suggestionName.textContent = iconName;
        linkElements.suggestionWrap.classList.remove('d-none');
    };

    // Requests an icon suggestion for the current create-link label, using the latest response only.
    const requestIconSuggestion = async () => {
        const label = linkElements.createLabel.value.trim();
        const iconValue = linkElements.createIconName.value.trim();

        if (iconValue || label.length < 2) {
            hideIconSuggestion();
            return;
        }

        const requestId = ++iconSuggestionRequestId;

        try {
            const result = await getJson(`${urls.suggestIcon}&label=${encodeURIComponent(label)}`);
            if (requestId !== iconSuggestionRequestId) {
                return;
            }

            const found = result.found ?? result.Found;
            const suggestedIconName = result.suggestedIconName ?? result.SuggestedIconName ?? '';

            if (found && suggestedIconName) {
                showIconSuggestion(suggestedIconName);
                return;
            }

            hideIconSuggestion();
        } catch {
            if (requestId === iconSuggestionRequestId) {
                hideIconSuggestion();
            }
        }
    };

    // Rebuilds the category dropdown options used by the create and edit link forms.
    const renderCategoryOptions = () => {
        const orderedCategories = sortCategories(categories.map(normalizeCategory));
        const optionsHtml = orderedCategories
            .map(c => `<option value="${c.id}">${escapeHtml(c.name)}</option>`)
            .join('');

        linkElements.createCategoryId.innerHTML = optionsHtml;
        linkElements.editCategoryId.innerHTML = optionsHtml;
    };

    // Re-renders the category table from the current in-memory category list.
    const renderCategories = () => {
        const ordered = sortCategories(categories.map(normalizeCategory));
        categoryElements.tableBody.innerHTML = ordered.map((category) => `
            <tr data-category-id="${category.id}"
                data-category-name="${escapeHtml(category.name)}"
                data-category-admin="${category.isAdminCategory}">
                <td>${escapeHtml(category.name)}</td>
                <td>${category.isAdminCategory ? 'Yes' : 'No'}</td>
                <td>${category.linkCount}</td>
                <td class="text-end">
                    <button type="button" class="btn btn-sm btn-outline-primary" data-action="edit">Edit</button>
                    <button type="button" class="btn btn-sm btn-outline-danger" data-action="delete">Delete</button>
                </td>
            </tr>`).join('');

        renderCategoryOptions();
    };

    // Re-renders the link table from the current in-memory link list.
    const renderLinks = () => {
        const ordered = sortLinks(links.map(normalizeLink));
        linkElements.tableBody.innerHTML = ordered.map((link) => `
            <tr data-link-id="${link.id}"
                data-link-category-id="${link.categoryId}"
                data-link-label="${escapeHtml(link.label)}"
                data-link-description="${escapeHtml(link.description)}"
                data-link-icon-name="${escapeHtml(link.iconName)}"
                data-link-url="${escapeHtml(link.url)}">
                <td>${escapeHtml(link.categoryName)}</td>
                <td>${escapeHtml(link.label)}</td>
                <td>${escapeHtml(link.description)}</td>
                <td>${escapeHtml(link.iconName)}</td>
                <td class="text-break">${escapeHtml(link.url)}</td>
                <td class="text-end">
                    <button type="button" class="btn btn-sm btn-outline-primary" data-action="edit">Edit</button>
                    <button type="button" class="btn btn-sm btn-outline-danger" data-action="delete">Delete</button>
                </td>
            </tr>`).join('');
    };

    // Hides and resets the category edit form.
    const hideCategoryEdit = () => {
        categoryElements.editCard.classList.add('d-none');
        categoryElements.editForm.reset();
        categoryElements.editId.value = '';
    };

    // Hides and resets the link edit form.
    const hideLinkEdit = () => {
        linkElements.editCard.classList.add('d-none');
        linkElements.editForm.reset();
        linkElements.editId.value = '';
    };

    // Reloads categories from the server and refreshes the category UI.
    const loadCategories = async () => {
        categories = await getJson(urls.categories);
        renderCategories();
    };

    // Reloads links from the server and refreshes the link UI.
    const loadLinks = async () => {
        links = await getJson(urls.links);
        renderLinks();
    };

    // Handles category creation.
    categoryElements.createForm.addEventListener('submit', async (event) => {
        event.preventDefault();
        clearMessage(categoryElements.message);

        const formData = new FormData(categoryElements.createForm);
        const name = (formData.get('name') ?? '').toString().trim();
        const isAdminCategory = formData.get('isAdminCategory') === 'on';

        if (!name) {
            setMessage(categoryElements.message, 'Category name is required.', true);
            return;
        }

        try {
            await postForm(urls.createCategory, {
                name,
                isAdminCategory: isAdminCategory.toString()
            });

            categoryElements.createForm.reset();
            await loadCategories();
            setMessage(categoryElements.message, 'Category added.');
        } catch {
            setMessage(categoryElements.message, 'Failed to add category.', true);
        }
    });

    // Handles category edit and delete actions from the table.
    categoryElements.tableBody.addEventListener('click', async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const action = target.getAttribute('data-action');
        if (!action) {
            return;
        }

        const row = target.closest('tr[data-category-id]');
        if (!row) {
            return;
        }

        const id = Number(row.getAttribute('data-category-id'));
        const name = row.getAttribute('data-category-name') ?? '';
        const isAdmin = row.getAttribute('data-category-admin') === 'true';

        if (action === 'edit') {
            categoryElements.editId.value = id.toString();
            categoryElements.editName.value = name;
            categoryElements.editAdmin.checked = isAdmin;
            categoryElements.editCard.classList.remove('d-none');
            categoryElements.editName.focus();
            return;
        }

        if (!confirm(`Delete category "${name}" and all its links?`)) {
            return;
        }

        clearMessage(categoryElements.message);
        clearMessage(linkElements.message);

        try {
            await postForm(urls.deleteCategory, { id: id.toString() });
            hideCategoryEdit();
            hideLinkEdit();
            await loadCategories();
            await loadLinks();
            setMessage(categoryElements.message, 'Category deleted.');
        } catch {
            setMessage(categoryElements.message, 'Failed to delete category.', true);
        }
    });

    // Handles category updates.
    categoryElements.editForm.addEventListener('submit', async (event) => {
        event.preventDefault();
        clearMessage(categoryElements.message);

        const id = categoryElements.editId.value;
        const name = categoryElements.editName.value.trim();
        const isAdminCategory = categoryElements.editAdmin.checked;

        if (!id || !name) {
            setMessage(categoryElements.message, 'Category name is required.', true);
            return;
        }

        try {
            await postForm(urls.updateCategory, {
                id,
                name,
                isAdminCategory: isAdminCategory.toString()
            });

            hideCategoryEdit();
            await loadCategories();
            await loadLinks();
            setMessage(categoryElements.message, 'Category updated.');
        } catch {
            setMessage(categoryElements.message, 'Failed to update category.', true);
        }
    });

    categoryElements.cancelEdit.addEventListener('click', () => {
        hideCategoryEdit();
        clearMessage(categoryElements.message);
    });

    // Handles link creation.
    linkElements.createForm.addEventListener('submit', async (event) => {
        event.preventDefault();
        clearMessage(linkElements.message);

        const formData = new FormData(linkElements.createForm);
        const categoryId = (formData.get('categoryId') ?? '').toString();
        const label = (formData.get('label') ?? '').toString().trim();
        const url = (formData.get('url') ?? '').toString().trim();

        if (!categoryId || !label || !url) {
            setMessage(linkElements.message, 'Category, name, and URL are required.', true);
            return;
        }

        if (!isValidWebUrl(url)) {
            setMessage(linkElements.message, 'URL must be a valid HTTP or HTTPS address.', true);
            return;
        }

        try {
            await postForm(urls.createLink, {
                categoryId,
                label,
                description: (formData.get('description') ?? '').toString(),
                iconName: (formData.get('iconName') ?? '').toString(),
                url
            });

            linkElements.createForm.reset();
            iconSuggestionRequestId++;
            hideIconSuggestion();
            await loadCategories();
            await loadLinks();
            setMessage(linkElements.message, 'Link added.');
        } catch {
            setMessage(linkElements.message, 'Failed to add link.', true);
        }
    });

    // Handles link edit and delete actions from the table.
    linkElements.tableBody.addEventListener('click', async (event) => {
        const target = event.target;
        if (!(target instanceof HTMLElement)) {
            return;
        }

        const action = target.getAttribute('data-action');
        if (!action) {
            return;
        }

        const row = target.closest('tr[data-link-id]');
        if (!row) {
            return;
        }

        const id = Number(row.getAttribute('data-link-id'));
        const categoryId = row.getAttribute('data-link-category-id') ?? '';
        const label = row.getAttribute('data-link-label') ?? '';
        const description = row.getAttribute('data-link-description') ?? '';
        const iconName = row.getAttribute('data-link-icon-name') ?? '';
        const url = row.getAttribute('data-link-url') ?? '';

        if (action === 'edit') {
            linkElements.editId.value = id.toString();
            linkElements.editCategoryId.value = categoryId;
            linkElements.editLabel.value = label;
            linkElements.editDescription.value = description;
            linkElements.editIconName.value = iconName;
            linkElements.editUrl.value = url;
            linkElements.editCard.classList.remove('d-none');
            linkElements.editLabel.focus();
            return;
        }

        if (!confirm(`Delete link "${label}"?`)) {
            return;
        }

        clearMessage(linkElements.message);

        try {
            await postForm(urls.deleteLink, { id: id.toString() });
            hideLinkEdit();
            await loadCategories();
            await loadLinks();
            setMessage(linkElements.message, 'Link deleted.');
        } catch {
            setMessage(linkElements.message, 'Failed to delete link.', true);
        }
    });

    // Handles link updates.
    linkElements.editForm.addEventListener('submit', async (event) => {
        event.preventDefault();
        clearMessage(linkElements.message);

        const id = linkElements.editId.value;
        const categoryId = linkElements.editCategoryId.value;
        const label = linkElements.editLabel.value.trim();
        const url = linkElements.editUrl.value.trim();

        if (!id || !categoryId || !label || !url) {
            setMessage(linkElements.message, 'Category, name, and URL are required.', true);
            return;
        }

        if (!isValidWebUrl(url)) {
            setMessage(linkElements.message, 'URL must be a valid HTTP or HTTPS address.', true);
            return;
        }

        try {
            await postForm(urls.updateLink, {
                id,
                categoryId,
                label,
                description: linkElements.editDescription.value,
                iconName: linkElements.editIconName.value,
                url
            });

            hideLinkEdit();
            await loadCategories();
            await loadLinks();
            setMessage(linkElements.message, 'Link updated.');
        } catch {
            setMessage(linkElements.message, 'Failed to update link.', true);
        }
    });

    linkElements.cancelEdit.addEventListener('click', () => {
        hideLinkEdit();
        clearMessage(linkElements.message);
    });

    // Debounces server-side icon suggestions while the create-link form is being filled in.
    if (linkElements.createLabel && linkElements.createIconName && linkElements.applySuggestion) {
        linkElements.createLabel.addEventListener('input', () => {
            clearTimeout(iconSuggestionDebounceHandle);
            iconSuggestionDebounceHandle = setTimeout(requestIconSuggestion, 400);
        });

        linkElements.createLabel.addEventListener('blur', async () => {
            clearTimeout(iconSuggestionDebounceHandle);
            await requestIconSuggestion();
        });

        linkElements.createIconName.addEventListener('input', () => {
            if (linkElements.createIconName.value.trim()) {
                hideIconSuggestion();
                return;
            }

            clearTimeout(iconSuggestionDebounceHandle);
            iconSuggestionDebounceHandle = setTimeout(requestIconSuggestion, 300);
        });

        linkElements.applySuggestion.addEventListener('click', () => {
            if (!currentSuggestedIconName) {
                return;
            }

            linkElements.createIconName.value = currentSuggestedIconName;
            hideIconSuggestion();
        });
    }

    // Render the initial server-provided state immediately so the page is interactive on load.
    renderCategories();
    renderLinks();
}
