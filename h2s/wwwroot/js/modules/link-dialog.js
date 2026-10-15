/* The shared add/edit link dialog (Pages/Shared/_LinkDialog.cshtml). URL first: once a URL is entered, the server
   suggests a label, a selfh.st icon (or the site's own icon), and the preview shows the tile the server would render.
   The icon and Uptime Kuma monitor fields have pickers. */
import { IconPicker } from './icon-picker.js';
import { MonitorPicker } from './monitor-picker.js';

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
            iconUrl: this.form.elements.iconUrl,
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
        this.autofilled = { label: false, iconName: false, iconUrl: false };
        this.suggestion = 0;
        this.suggestedUrl = null;
        this.previewRequest = 0;

        const browseIcons = element.querySelector('[data-role="browse-icons"]');
        this.iconPicker = new IconPicker(element.querySelector('[data-role="icon-picker"]'), browseIcons, {
            searchUrl: this.urls.searchIconsUrl,
            onPick: reference => {
                this.fields.iconName.value = reference;
                this.autofilled.iconName = false;
                this.fields.iconName.focus();
                this.refreshPreview();
            }
        });
        browseIcons.addEventListener('click', () => {
            if (this.iconPicker.isOpen) {
                this.iconPicker.close();
            } else {
                this.iconPicker.open(this.fields.iconName.value || this.fields.label.value);
            }
        });

        const monitorSelect = element.querySelector('[data-role="monitor-select"]');
        this.monitorPicker = monitorSelect && this.urls.monitorsUrl
            ? new MonitorPicker(monitorSelect, this.fields.monitorId, element.querySelector('[data-role="monitor-status"]'),
                this.urls.monitorsUrl)
            : null;

        const previewSoon = debounce(() => this.refreshPreview(), 250);

        // Look the page up once the URL is complete: when it's pasted, or when focus leaves the field. Looking up while
        // someone types would chase every partial host name.
        this.fields.url.addEventListener('paste', () => window.setTimeout(() => this.suggest(), 0));
        this.fields.url.addEventListener('change', () => this.suggest());
        for (const name of ['label', 'iconName', 'iconUrl']) {
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
        this.monitorPicker?.setValue('');
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
        this.fields.iconUrl.value = link.iconUrl;
        if (this.monitorPicker) {
            await this.monitorPicker.setValue(link.monitorId);
        } else if (this.fields.monitorId) {
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
        this.autofilled = { label: false, iconName: false, iconUrl: false };
        this.suggestedUrl = null;
        this.showError('');
        this.status.textContent = '';
        this.preview.replaceChildren();
        this.iconPicker.close();
    }

    showError(message) {
        this.error.textContent = message;
        this.error.classList.toggle('d-none', !message);
    }

    // Asks the server what it can suggest for the URL, and fills in the label and icon unless they were typed in.
    async suggest() {
        const url = this.fields.url.value.trim();
        // Pasting and then leaving the field both ask; one lookup per URL is enough.
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
            if (suggestion.label && (this.autofilled.label || !this.fields.label.value.trim())) {
                this.fields.label.value = suggestion.label;
                this.autofilled.label = true;
                notes.push('Label suggested from the page.');
            } else if (!suggestion.label) {
                notes.push("Couldn't read a name from the page.");
            }

            if (suggestion.iconName && (this.autofilled.iconName || !this.fields.iconName.value.trim())) {
                this.fields.iconName.value = suggestion.iconName;
                this.autofilled.iconName = true;
                notes.push(`Icon suggested: ${suggestion.iconName}.`);
            }

            // The site's own icon is only a fallback, so it's offered when there's no selfh.st icon.
            if (suggestion.iconUrl && !this.fields.iconName.value.trim() &&
                (this.autofilled.iconUrl || !this.fields.iconUrl.value.trim())) {
                this.fields.iconUrl.value = suggestion.iconUrl;
                this.autofilled.iconUrl = true;
                notes.push("Using the site's own icon.");
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
            iconUrl: this.fields.iconUrl.value.trim(),
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
