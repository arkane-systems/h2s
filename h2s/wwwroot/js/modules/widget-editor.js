/* The link dialog's Widgets section (Pages/Shared/_LinkDialog.cshtml). It lists the link's widgets, which can be
   reordered, edited and removed, and adds new ones from the kinds the Editor's WidgetKinds handler offers (those whose
   integration has a connection). The fields come from each kind's schema through option-fields.js. Nothing is saved
   here: value() gives the list as the JSON the link handlers take, so widgets save and cancel with the link. */
import { createOptionField, readOptionFields } from './option-fields.js';

const button = (text, className, label) => {
    const element = document.createElement('button');
    element.type = 'button';
    element.className = className;
    element.textContent = text;
    element.setAttribute('aria-label', label);
    return element;
};

// Option values as the handlers take them: booleans as true or false, everything else as typed.
const postedValue = value => value === null || value === undefined ? '' : String(value);

export class WidgetEditor {
    /** @param {HTMLElement} root The Widgets fieldset. */
    constructor(root) {
        this.root = root;
        this.kindsUrl = root.dataset.widgetKindsUrl;
        this.list = root.querySelector('[data-role="widget-list"]');
        this.empty = root.querySelector('[data-role="widget-empty"]');
        this.unavailable = root.querySelector('[data-role="widget-unavailable"]');
        this.addButton = root.querySelector('[data-role="add-widget"]');
        this.form = root.querySelector('[data-role="widget-form"]');
        this.formTitle = root.querySelector('[data-role="widget-form-title"]');
        this.kindSelect = root.querySelector('[data-role="widget-kind"]');
        this.fields = root.querySelector('[data-role="widget-fields"]');
        this.refresh = root.querySelector('[data-role="widget-refresh"]');
        this.refreshHelp = root.querySelector('[data-role="widget-refresh-help"]');
        this.kinds = [];
        this.widgets = [];
        // The index of the widget being edited, -1 for a new one, or null when the form is closed.
        this.editing = null;

        this.addButton.addEventListener('click', () => this.openForm(-1));
        this.kindSelect.addEventListener('change', () => this.showFields(this.selectedKind(), {}));
        root.querySelector('[data-role="widget-done"]').addEventListener('click', () => this.commit());
        // Enter in a widget's field finishes the widget, rather than submitting the whole link.
        this.form.addEventListener('keydown', event => {
            if (event.key === 'Enter' && event.target instanceof HTMLInputElement) {
                event.preventDefault();
                this.commit();
            }
        });
        root.querySelector('[data-role="widget-cancel"]').addEventListener('click', () => {
            const index = this.editing;
            this.closeForm();
            (index >= 0 ? this.list.children[index]?.querySelector('[data-role="edit"]') : this.addButton)?.focus();
        });
    }

    /** Whether a widget is being added or edited, which has to be finished before the link is saved. */
    get isEditing() {
        return this.editing !== null;
    }

    /**
     * Shows a link's widgets, as the Editor's Link handler describes them.
     * @param {object[]} widgets The widgets, in order.
     */
    async load(widgets) {
        this.widgets = widgets.map(widget => ({ ...widget, refreshSeconds: widget.refreshSeconds ?? '' }));
        this.closeForm();
        this.render();

        try {
            this.kinds = await window.h2sApi.getJson(this.kindsUrl);
        } catch {
            this.kinds = [];
        }
        this.render();
    }

    /** The widgets as the link handlers take them, as JSON. */
    value() {
        return JSON.stringify(this.widgets.map(widget => widget.isKnown === false
            ? { id: widget.id, keep: true }
            : {
                id: widget.id ?? null,
                kind: widget.kind,
                options: Object.fromEntries(Object.entries(widget.values).map(([key, value]) => [key, postedValue(value)])),
                refreshSeconds: postedValue(widget.refreshSeconds)
            }));
    }

    selectedKind() {
        return this.kinds.find(kind => kind.reference === this.kindSelect.value);
    }

    hasAppWidget(except) {
        return this.widgets.some((widget, index) => index !== except && widget.role === 'app');
    }

    // A short description of a widget's settings, such as "Monitor ID 12".
    summary(widget) {
        const kind = this.kinds.find(candidate => candidate.reference === widget.kind);
        if (!kind) {
            return '';
        }
        return kind.options
            .filter(option => option.type !== 'boolean' && option.type !== 'secret' && postedValue(widget.values[option.key]) !== '')
            .map(option => {
                const value = postedValue(widget.values[option.key]);
                const choice = option.choices?.find(candidate => candidate.value === value);
                return `${option.label} ${choice ? choice.label : value}`;
            })
            .join(', ');
    }

    render() {
        this.list.replaceChildren(...this.widgets.map((widget, index) => {
            const item = document.createElement('li');
            item.className = 'list-group-item d-flex align-items-center gap-2';

            const text = document.createElement('div');
            text.className = 'flex-grow-1 small';
            const name = document.createElement('div');
            name.className = 'fw-semibold';
            name.textContent = widget.displayName;
            const details = document.createElement('div');
            details.className = 'text-muted';
            details.textContent = widget.isKnown === false ? "This version of h2s doesn't know it; you can only remove it." : this.summary(widget);
            text.append(name, details);
            item.append(text);

            const up = button('↑', 'btn btn-sm btn-outline-secondary', `Move ${widget.displayName} up`);
            up.disabled = index === 0;
            up.addEventListener('click', () => this.move(index, -1));
            const down = button('↓', 'btn btn-sm btn-outline-secondary', `Move ${widget.displayName} down`);
            down.disabled = index === this.widgets.length - 1;
            down.addEventListener('click', () => this.move(index, 1));
            item.append(up, down);

            if (widget.isKnown !== false) {
                const edit = button('✎', 'btn btn-sm btn-outline-secondary', `Edit ${widget.displayName}`);
                edit.dataset.role = 'edit';
                edit.disabled = !this.kinds.some(kind => kind.reference === widget.kind);
                edit.addEventListener('click', () => this.openForm(index));
                item.append(edit);
            }

            const remove = button('×', 'btn btn-sm btn-outline-danger', `Remove ${widget.displayName}`);
            remove.addEventListener('click', () => {
                this.widgets.splice(index, 1);
                this.render();
                (this.list.children[Math.min(index, this.widgets.length - 1)]?.querySelector('[data-role="edit"]') ?? this.addButton).focus();
            });
            item.append(remove);
            return item;
        }));

        this.empty.classList.toggle('d-none', this.widgets.length > 0 || this.kinds.length === 0);
        this.unavailable.classList.toggle('d-none', this.kinds.length > 0);
        this.addButton.classList.toggle('d-none', this.kinds.length === 0);
        this.addButton.disabled = this.isEditing;
    }

    move(index, offset) {
        const target = index + offset;
        [this.widgets[index], this.widgets[target]] = [this.widgets[target], this.widgets[index]];
        this.render();
        this.list.children[target]?.querySelectorAll('button')[offset < 0 ? 0 : 1]?.focus();
    }

    /** @param {number} index The widget to edit, or -1 to add one. */
    openForm(index) {
        this.editing = index;
        const widget = index >= 0 ? this.widgets[index] : null;

        this.formTitle.textContent = widget ? `Edit ${widget.displayName}` : 'Add widget';
        this.kindSelect.replaceChildren(...this.kinds.map(kind => {
            const option = new Option(kind.role === 'app' && this.hasAppWidget(index)
                ? `${kind.displayName} (a link has only one app widget)`
                : kind.displayName, kind.reference);
            option.disabled = kind.role === 'app' && this.hasAppWidget(index);
            return option;
        }));
        this.kindSelect.disabled = widget !== null;
        if (widget) {
            this.kindSelect.value = widget.kind;
        } else {
            this.kindSelect.value = [...this.kindSelect.options].find(option => !option.disabled)?.value ?? '';
        }

        this.showFields(this.selectedKind(), widget?.values ?? {});
        this.refresh.value = widget?.refreshSeconds ?? '';
        this.form.classList.remove('was-validated', 'd-none');
        this.render();
        (widget ? this.fields.querySelector('input, select') : this.kindSelect)?.focus();
    }

    showFields(kind, values) {
        this.fields.replaceChildren(...(kind?.options ?? []).map(option => createOptionField(option,
            option.type === 'boolean'
                ? (values[option.key] ?? option.defaultValue) === true || values[option.key] === 'true'
                : values[option.key] ?? option.defaultValue,
            { idPrefix: 'link-dialog-widget-option-' })));
        this.refreshHelp.textContent = kind ? `Leave blank for every ${kind.defaultRefreshSeconds} seconds.` : '';
    }

    closeForm() {
        this.editing = null;
        this.form.classList.add('d-none');
        this.fields.replaceChildren();
        this.addButton.disabled = false;
    }

    commit() {
        const kind = this.selectedKind();
        if (!kind) {
            return;
        }

        this.form.classList.add('was-validated');
        const invalid = [...this.form.querySelectorAll('input, select')].find(input => !input.checkValidity());
        if (invalid) {
            invalid.focus();
            return;
        }

        const index = this.editing;
        const widget = {
            id: index >= 0 ? this.widgets[index].id : null,
            kind: kind.reference,
            displayName: kind.displayName,
            isKnown: true,
            role: kind.role,
            values: Object.fromEntries(readOptionFields(this.fields).map(([name, value]) => [name.replace(/^options\[(.*)\]$/, '$1'), value])),
            refreshSeconds: this.refresh.value.trim()
        };

        if (index >= 0) {
            this.widgets[index] = widget;
        } else {
            this.widgets.push(widget);
        }

        const position = index >= 0 ? index : this.widgets.length - 1;
        this.closeForm();
        this.render();
        this.list.children[position]?.querySelector('[data-role="edit"]')?.focus();
    }
}
