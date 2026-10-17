/* The add/edit panel dialog for dashboard edit mode (Pages/Shared/_PanelDialog.cshtml). Its option fields are built from
   the kind's schema, as the Editor's PanelKinds and Panel handlers describe it, by option-fields.js. A kind that holds
   widgets, such as a status bar's items, gets widget-editor.js, configured for where they go; they're posted with the
   panel. The preview is the panel the server would render, its widgets read live (widget-preview.js). */
import { createOptionField, readOptionFields } from './option-fields.js';
import { WidgetEditor } from './widget-editor.js';
import { describeDraft, readPreviewWidgets } from './widget-preview.js';

// A handler URL with query parameters added, from an object or name and value pairs.
const withQuery = (url, params) => {
    const result = new URL(url, window.location.origin);
    for (const [name, value] of Array.isArray(params) ? params : Object.entries(params)) {
        result.searchParams.append(name, value);
    }
    return result.pathname + result.search;
};

const debounce = (action, wait) => {
    let timer = 0;
    return (...args) => {
        window.clearTimeout(timer);
        timer = window.setTimeout(() => action(...args), wait);
    };
};

export class PanelDialog {
    /**
     * @param {HTMLElement} element The dialog's .modal element.
     * @param {{ onSaved: (panel: object, isNew: boolean) => void, onDeleted: (result: object) => void }} hooks
     *   Called after a successful save (with the handler's JSON) and delete (with its JSON, including the Undo snapshot).
     */
    constructor(element, { onSaved, onDeleted }) {
        this.urls = element.dataset;
        this.onSaved = onSaved;
        this.onDeleted = onDeleted;
        this.modal = window.bootstrap.Modal.getOrCreateInstance(element);
        this.form = element.querySelector('form');
        this.title = element.querySelector('.modal-title');
        this.error = element.querySelector('[data-role="error"]');
        this.newOnly = element.querySelector('[data-role="new-only"]');
        this.kind = this.form.elements.kind;
        this.kindNote = element.querySelector('[data-role="kind-note"]');
        this.zone = this.form.elements.zone;
        this.optionsHost = element.querySelector('[data-role="options"]');
        this.noOptions = element.querySelector('[data-role="no-options"]');
        this.widgetsHost = element.querySelector('[data-role="widgets-host"]');
        this.previewHost = element.querySelector('[data-role="preview-host"]');
        this.preview = element.querySelector('[data-role="preview"]');
        this.saveButton = element.querySelector('[data-role="save"]');
        this.deleteButton = element.querySelector('[data-role="delete"]');
        this.kinds = [];
        this.panel = null;
        // The kind being added or edited, as the handlers describe it.
        this.current = null;
        this.previewRequest = 0;

        const previewSoon = debounce(() => this.refreshPreview(), 250);
        // While a widget is open, the panel can't be saved or deleted: Done or Cancel finishes the widget first.
        this.widgetEditor = new WidgetEditor(this.widgetsHost.querySelector('[data-role="widgets"]'), {
            onEditingChanged: isEditing => {
                this.saveButton.disabled = isEditing || (this.panel !== null && !this.panel.isKnown);
                this.deleteButton.disabled = isEditing;
                this.showError('');
            },
            onChanged: previewSoon
        });

        this.form.addEventListener('submit', event => {
            event.preventDefault();
            this.save();
        });
        this.form.addEventListener('input', previewSoon);
        this.deleteButton.addEventListener('click', () => this.delete());
        this.kind.addEventListener('change', () => this.showKind(this.selectedKind(), {}, []));
        element.addEventListener('shown.bs.modal', () => {
            (this.panel ? this.optionsHost.querySelector('input') : this.kind)?.focus();
        });
    }

    async openNew() {
        this.reset();
        this.title.textContent = 'Add panel';
        this.newOnly.classList.remove('d-none');
        this.deleteButton.classList.add('d-none');

        try {
            this.kinds = await window.h2sApi.getJson(this.urls.kindsUrl);
        } catch (error) {
            this.showError(`Couldn't load the kinds of panel: ${error.message}`);
            this.saveButton.disabled = true;
            this.modal.show();
            return;
        }

        const available = this.kinds.filter(kind => kind.isAvailable);
        this.kind.replaceChildren(...available.map(kind => new Option(kind.displayName, kind.key)));
        this.kind.disabled = available.length === 0;
        this.saveButton.disabled = available.length === 0;

        const taken = this.kinds.filter(kind => !kind.isAvailable).map(kind => kind.displayName);
        this.kindNote.textContent = available.length === 0
            ? 'Every kind of panel is already on the dashboard, and each can appear only once.'
            : taken.length ? `Already on the dashboard, and limited to one: ${taken.join(', ')}.` : '';

        this.showKind(this.selectedKind(), {}, []);
        this.modal.show();
    }

    /** @param {string} id The panel's ID. */
    async openExisting(id) {
        const panel = await window.h2sApi.getJson(withQuery(this.urls.panelUrl, { id }));

        this.reset();
        this.panel = panel;
        this.title.textContent = `Edit ${panel.displayName.toLowerCase()} panel`;
        this.newOnly.classList.add('d-none');
        // Hidden, and disabled so the empty kind list doesn't fail validation.
        this.kind.disabled = true;
        this.deleteButton.classList.remove('d-none');
        this.saveButton.disabled = !panel.isKnown;

        if (panel.isKnown) {
            this.showKind({ key: panel.kind, ...panel }, panel.values, panel.widgets ?? []);
        } else {
            this.showKind(null, {}, [], "This version of h2s doesn't know this kind of panel, so you can only delete it.");
        }
        this.modal.show();
    }

    selectedKind() {
        return this.kinds.find(kind => kind.key === this.kind.value);
    }

    reset() {
        this.form.reset();
        this.form.classList.remove('was-validated');
        this.panel = null;
        this.current = null;
        this.kind.disabled = false;
        this.saveButton.disabled = false;
        this.deleteButton.disabled = false;
        this.kindNote.textContent = '';
        this.preview.replaceChildren();
        this.showError('');
    }

    showError(message) {
        this.error.textContent = message;
        this.error.classList.toggle('d-none', !message);
    }

    /**
     * Shows a kind's option fields, its widgets when it holds them, and its preview.
     * @param {object | null} kind The kind, as the handlers describe it; null for an unknown one.
     * @param {object} values The current option values, by key; missing ones show the default.
     * @param {object[]} widgets The panel's widgets, as the Panel handler describes them.
     * @param {string} [emptyMessage] What to say when there are no options.
     */
    showKind(kind, values, widgets, emptyMessage = 'This kind of panel has no options.') {
        this.current = kind;
        const options = kind?.options ?? [];
        this.optionsHost.replaceChildren(...options.map(option =>
            createOptionField(option, values[option.key] ?? option.defaultValue, { idPrefix: 'panel-option-' })));
        this.noOptions.textContent = emptyMessage;
        this.noOptions.classList.toggle('d-none', options.length > 0 || Boolean(kind?.widgetPlacement));

        const holdsWidgets = Boolean(kind?.widgetPlacement);
        this.widgetsHost.classList.toggle('d-none', !holdsWidgets);
        if (holdsWidgets) {
            this.widgetEditor.configure({ placement: kind.widgetPlacement, maxWidgets: kind.maxWidgets });
            this.widgetEditor.load(widgets);
        }

        this.previewHost.classList.toggle('d-none', !kind);
        this.refreshPreview();
    }

    // Shows the panel the server would render for the current values, then reads its widgets that haven't been read.
    async refreshPreview() {
        const kind = this.current;
        if (!kind) {
            return;
        }

        const request = ++this.previewRequest;
        const holdsWidgets = Boolean(kind.widgetPlacement);
        const { widgets, draftIndex } = holdsWidgets ? this.widgetEditor.previewValue() : { widgets: '[]', draftIndex: null };

        try {
            const html = await window.h2sApi.getText(withQuery(this.urls.previewUrl,
                [['kind', kind.key], ['widgets', widgets], ...readOptionFields(this.optionsHost)]));
            if (request !== this.previewRequest) {
                return;
            }
            this.preview.innerHTML = html;
        } catch {
            // The preview is a convenience; the form still works without it.
            return;
        }

        if (!holdsWidgets) {
            return;
        }

        const reportDraft = () => {
            if (this.widgetEditor.isEditing) {
                this.widgetEditor.showResult(describeDraft(this.preview, draftIndex));
            }
        };
        reportDraft();

        await readPreviewWidgets(this.preview, {
            url: this.urls.widgetPreviewUrl,
            widgets,
            placement: kind.widgetPlacement,
            isCurrent: () => request === this.previewRequest
        });

        if (request === this.previewRequest) {
            reportDraft();
        }
    }

    async save() {
        if (this.widgetEditor.isEditing) {
            this.showError('Finish or cancel the widget you are editing first.');
            return;
        }

        this.form.classList.add('was-validated');
        if (!this.form.checkValidity()) {
            this.form.querySelector(':invalid')?.focus();
            return;
        }

        this.showError('');
        const isNew = !this.panel;
        const fields = [
            ...readOptionFields(this.optionsHost),
            ...(this.current?.widgetPlacement ? [['widgets', this.widgetEditor.value()]] : [])
        ];

        try {
            const panel = isNew
                ? await window.h2sApi.postForm(this.urls.createUrl, [['kind', this.kind.value], ['zone', this.zone.value], ...fields])
                : await window.h2sApi.postForm(this.urls.updateUrl, [['id', this.panel.id], ...fields]);
            this.modal.hide();
            this.onSaved(panel, isNew);
        } catch (error) {
            this.showError(error.message);
        }
    }

    async delete() {
        this.showError('');

        try {
            const result = await window.h2sApi.postForm(this.urls.deleteUrl, { id: this.panel.id });
            this.modal.hide();
            this.onDeleted(result);
        } catch (error) {
            this.showError(error.message);
        }
    }

    // Recreates a deleted panel from its snapshot, for Undo.
    restore(snapshot) {
        return window.h2sApi.postForm(this.urls.restoreUrl, { snapshot: JSON.stringify(snapshot) });
    }

    // Fetches a saved panel as edit mode renders it.
    async fetchSlot(id) {
        const template = document.createElement('template');
        template.innerHTML = (await window.h2sApi.getText(withQuery(this.urls.slotUrl, { id }))).trim();
        return template.content.firstElementChild;
    }
}
