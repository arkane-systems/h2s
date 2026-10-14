/* The shared add/edit link dialog (Pages/Shared/_LinkDialog.cshtml). URL first: once a URL is entered, the server
   suggests a label from the page title and a selfh.st icon, and the preview shows the tile the server would render. */

const debounce = (action, wait) => {
    let timer = 0;
    return (...args) => {
        window.clearTimeout(timer);
        timer = window.setTimeout(() => action(...args), wait);
    };
};

// Adds query parameters to a handler URL, which already carries ?handler=...
const withQuery = (url, params) => {
    const target = new URL(url, window.location.origin);
    for (const [name, value] of Object.entries(params)) {
        target.searchParams.set(name, value ?? '');
    }
    return target.pathname + target.search;
};

const isWebUrl = value => /^https?:\/\/.+/i.test(value.trim());

export class LinkDialog {
    /**
     * @param {HTMLElement} element The dialog's .modal element.
     * @param {{ onSaved: (link: object, isNew: boolean) => void, onDeleted: (result: object) => void }} hooks Called
     *   after a successful save (with the handler's JSON) and delete (with its JSON, including the Undo snapshot).
     */
    constructor(element, { onSaved, onDeleted }) {
        this.element = element;
        this.urls = element.dataset;
        this.onSaved = onSaved;
        this.onDeleted = onDeleted;
        this.modal = window.bootstrap.Modal.getOrCreateInstance(element);
        this.form = element.querySelector('form');
        this.fields = {
            url: this.form.elements.url,
            label: this.form.elements.label,
            description: this.form.elements.description,
            iconName: this.form.elements.iconName,
            monitorId: this.form.elements.monitorId ?? null,
            categoryId: this.form.elements.categoryId
        };
        this.title = element.querySelector('.modal-title');
        this.error = element.querySelector('[data-role="error"]');
        this.status = element.querySelector('[data-role="suggest-status"]');
        this.preview = element.querySelector('[data-role="preview"]');
        this.deleteButton = element.querySelector('[data-role="delete"]');
        this.saveButton = element.querySelector('[data-role="save"]');
        this.linkId = null;
        this.autofilled = { label: false, iconName: false };
        this.suggestion = 0;
        this.suggestedUrl = null;
        this.previewRequest = 0;

        const suggestSoon = debounce(() => this.suggest(), 600);
        const previewSoon = debounce(() => this.refreshPreview(), 250);

        this.fields.url.addEventListener('input', suggestSoon);
        this.fields.url.addEventListener('change', () => this.suggest());
        for (const name of ['label', 'iconName']) {
            // Typing in a field takes it over from the suggestions.
            this.fields[name].addEventListener('input', () => { this.autofilled[name] = false; });
        }
        this.form.addEventListener('input', previewSoon);
        this.form.addEventListener('submit', event => {
            event.preventDefault();
            this.save();
        });
        this.deleteButton.addEventListener('click', () => this.delete());
        element.addEventListener('shown.bs.modal', () => this.fields.url.focus());
    }

    /** Opens the dialog to add a link to a category. */
    openNew(categoryId) {
        this.reset();
        this.title.textContent = 'Add link';
        this.fields.categoryId.value = String(categoryId);
        this.deleteButton.classList.add('d-none');
        this.refreshPreview();
        this.modal.show();
    }

    /** Opens the dialog to edit a saved link. */
    async openExisting(linkId) {
        this.reset();
        this.title.textContent = 'Edit link';
        this.linkId = linkId;
        this.deleteButton.classList.remove('d-none');

        const link = await window.h2sApi.getJson(withQuery(this.urls.linkUrl, { id: linkId }));
        this.fields.url.value = link.url;
        this.fields.label.value = link.label;
        this.fields.description.value = link.description;
        this.fields.iconName.value = link.iconName;
        if (this.fields.monitorId) {
            this.fields.monitorId.value = link.monitorId ?? '';
        }
        this.fields.categoryId.value = String(link.categoryId);
        this.refreshPreview();
        this.modal.show();
    }

    reset() {
        this.form.reset();
        this.form.classList.remove('was-validated');
        this.linkId = null;
        this.autofilled = { label: false, iconName: false };
        this.suggestedUrl = null;
        this.showError('');
        this.status.textContent = '';
        this.preview.replaceChildren();
    }

    showError(message) {
        this.error.textContent = message;
        this.error.classList.toggle('d-none', !message);
    }

    // Asks the server what it can suggest for the URL, and fills in the label and icon unless they were typed in.
    async suggest() {
        const url = this.fields.url.value.trim();
        // Typing and then leaving the field both ask; one lookup per URL is enough.
        if (!isWebUrl(url) || url === this.suggestedUrl) {
            return;
        }

        this.suggestedUrl = url;

        const request = ++this.suggestion;
        this.status.textContent = 'Looking up the page…';

        try {
            const suggestion = await window.h2sApi.getJson(withQuery(this.urls.suggestUrl, {
                url,
                label: this.autofilled.label ? '' : this.fields.label.value
            }));

            if (request !== this.suggestion) {
                return;
            }

            const notes = [];
            if (suggestion.title && (this.autofilled.label || !this.fields.label.value.trim())) {
                this.fields.label.value = suggestion.title;
                this.autofilled.label = true;
                notes.push('Label suggested from the page title.');
            } else if (!suggestion.title) {
                notes.push("Couldn't read a title from the page.");
            }

            if (suggestion.iconName && (this.autofilled.iconName || !this.fields.iconName.value.trim())) {
                this.fields.iconName.value = suggestion.iconName;
                this.autofilled.iconName = true;
                notes.push(`Icon suggested: ${suggestion.iconName}.`);
            }

            this.status.textContent = notes.join(' ');
            this.refreshPreview();
        } catch {
            if (request === this.suggestion) {
                this.status.textContent = "Couldn't look up the page.";
            }
        }
    }

    values() {
        const values = {
            url: this.fields.url.value.trim(),
            label: this.fields.label.value.trim(),
            description: this.fields.description.value.trim(),
            iconName: this.fields.iconName.value.trim(),
            categoryId: this.fields.categoryId.value
        };
        if (this.fields.monitorId) {
            values.monitorId = this.fields.monitorId.value.trim();
        }
        return values;
    }

    // Shows the tile the server would render for the current values.
    async refreshPreview() {
        const request = ++this.previewRequest;
        const values = this.values();

        try {
            const html = await window.h2sApi.getText(withQuery(this.urls.previewUrl, {
                ...values,
                label: values.label || 'Label'
            }));
            if (request === this.previewRequest) {
                this.preview.innerHTML = html;
            }
        } catch {
            // The preview is a convenience; the form still works without it.
        }
    }

    async save() {
        this.form.classList.add('was-validated');
        if (!this.form.checkValidity()) {
            this.form.querySelector(':invalid')?.focus();
            return;
        }

        this.showError('');
        this.saveButton.disabled = true;
        const isNew = this.linkId === null;

        try {
            const data = isNew ? this.values() : { id: this.linkId, ...this.values() };
            const link = await window.h2sApi.postForm(isNew ? this.urls.createUrl : this.urls.updateUrl, data);
            this.modal.hide();
            this.onSaved(link, isNew);
        } catch (error) {
            this.showError(error.message);
        } finally {
            this.saveButton.disabled = false;
        }
    }

    async delete() {
        this.showError('');

        try {
            const result = await window.h2sApi.postForm(this.urls.deleteUrl, { id: this.linkId });
            this.modal.hide();
            this.onDeleted(result);
        } catch (error) {
            this.showError(error.message);
        }
    }
}
