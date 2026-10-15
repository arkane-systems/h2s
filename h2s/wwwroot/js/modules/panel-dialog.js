/* The add/edit panel dialog for dashboard edit mode (Pages/Shared/_PanelDialog.cshtml). Its option fields are built from
   the kind's schema, as the Editor's PanelKinds and Panel handlers describe it. */

// A handler URL with query parameters added.
const withQuery = (url, params) => {
    const result = new URL(url, window.location.origin);
    for (const [name, value] of Object.entries(params)) {
        result.searchParams.set(name, value);
    }
    return result.pathname + result.search;
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
        this.saveButton = element.querySelector('[data-role="save"]');
        this.deleteButton = element.querySelector('[data-role="delete"]');
        this.kinds = [];
        this.panel = null;

        this.form.addEventListener('submit', event => {
            event.preventDefault();
            this.save();
        });
        this.deleteButton.addEventListener('click', () => this.delete());
        this.kind.addEventListener('change', () => this.showOptions(this.selectedKind()?.options ?? [], {}));
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

        this.showOptions(this.selectedKind()?.options ?? [], {});
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
            this.showOptions(panel.options, panel.values);
        } else {
            this.showOptions([], {}, "This version of h2s doesn't know this kind of panel, so you can only delete it.");
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
        this.kind.disabled = false;
        this.saveButton.disabled = false;
        this.kindNote.textContent = '';
        this.showError('');
    }

    showError(message) {
        this.error.textContent = message;
        this.error.classList.toggle('d-none', !message);
    }

    /**
     * Builds a field per option.
     * @param {object[]} options The kind's option schema.
     * @param {object} values The current values, by key; missing ones show the default.
     * @param {string} [emptyMessage] What to say when there are no options.
     */
    showOptions(options, values, emptyMessage = 'This kind of panel has no options.') {
        this.optionsHost.replaceChildren(...options.map(option => this.createField(option, values[option.key] ?? option.defaultValue)));
        this.noOptions.textContent = emptyMessage;
        this.noOptions.classList.toggle('d-none', options.length > 0);
    }

    createField(option, value) {
        const id = `panel-option-${option.key}`;
        const field = document.createElement('div');
        field.className = 'mb-3';
        field.dataset.optionKey = option.key;
        field.dataset.optionType = option.type;

        const input = document.createElement('input');
        input.id = id;
        input.name = `options[${option.key}]`;

        const label = document.createElement('label');
        label.htmlFor = id;
        label.textContent = option.label;

        if (option.type === 'boolean') {
            field.classList.add('form-check', 'form-switch');
            input.type = 'checkbox';
            input.className = 'form-check-input';
            input.setAttribute('role', 'switch');
            input.checked = value === true;
            label.className = 'form-check-label';
            field.append(input, label);
        } else {
            input.className = 'form-control';
            input.required = option.isRequired;
            if (option.type === 'integer') {
                input.type = 'number';
                input.step = '1';
                input.inputMode = 'numeric';
                if (option.minimum !== null) {
                    input.min = String(option.minimum);
                }
                if (option.maximum !== null) {
                    input.max = String(option.maximum);
                }
            } else {
                input.type = 'text';
                input.maxLength = option.maxLength;
                input.autocomplete = 'off';
            }
            input.value = value ?? '';
            label.className = 'form-label';

            const invalid = document.createElement('div');
            invalid.className = 'invalid-feedback';
            const bounds = [
                option.minimum !== null ? `at least ${option.minimum}` : '',
                option.maximum !== null ? `at most ${option.maximum}` : ''
            ].filter(Boolean).join(' and ');
            invalid.textContent = option.type === 'integer'
                ? `Enter a whole number${bounds ? ` of ${bounds}` : ''}${option.isRequired ? '' : ', or leave it blank'}.`
                : `${option.label} is required.`;
            field.append(label, input, invalid);
        }

        if (option.helpText) {
            const help = document.createElement('div');
            help.className = 'form-text';
            help.id = `${id}-help`;
            help.textContent = option.helpText;
            input.setAttribute('aria-describedby', help.id);
            field.append(help);
        }

        return field;
    }

    // The option values as the handlers take them: booleans as true or false, everything else as typed.
    optionValues() {
        return [...this.optionsHost.querySelectorAll('[data-option-key]')].map(field => {
            const input = field.querySelector('input');
            return [input.name, field.dataset.optionType === 'boolean' ? String(input.checked) : input.value.trim()];
        });
    }

    async save() {
        this.form.classList.add('was-validated');
        if (!this.form.checkValidity()) {
            this.form.querySelector(':invalid')?.focus();
            return;
        }

        this.showError('');
        const isNew = !this.panel;

        try {
            const panel = isNew
                ? await window.h2sApi.postForm(this.urls.createUrl,
                    [['kind', this.kind.value], ['zone', this.zone.value], ...this.optionValues()])
                : await window.h2sApi.postForm(this.urls.updateUrl, [['id', this.panel.id], ...this.optionValues()]);
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
