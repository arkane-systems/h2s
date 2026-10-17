/* A dialog's widgets section (Pages/Shared/_WidgetEditor.cshtml): a link's widgets, or a panel's such as a status bar's
   items. It lists them, to reorder, edit and remove, and adds new ones from the kinds the Editor's WidgetKinds handler
   offers for the place they go (kinds that allow it, whose integration has a connection). Where the widgets go decides
   whether they have labels and how many there may be; configure() sets it. The fields come from each kind's schema through option-fields.js; an option with a
   choice list offers its integration's list, read once per opening of the dialog through the Choices handler. Nothing is
   saved here: value() gives the list as the JSON the link handlers take, so widgets save and cancel with the link, and
   previewValue() gives the same with the widget being edited, for the dialog's live preview. */
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

// A widget as the link and panel handlers take it.
const postedWidget = widget => widget.isKnown === false
    ? { id: widget.id, keep: true }
    : {
        id: widget.id ?? null,
        kind: widget.kind,
        options: Object.fromEntries(Object.entries(widget.values).map(([key, value]) => [key, postedValue(value)])),
        refreshSeconds: postedValue(widget.refreshSeconds),
        label: widget.label ?? ''
    };

// The integration a kind reference (integration/kind) belongs to.
const integrationOf = reference => reference.split('/')[0];

export class WidgetEditor {
    /**
     * @param {HTMLElement} root The Widgets fieldset.
     * @param {{ onEditingChanged?: (isEditing: boolean) => void, onChanged?: () => void }} [hooks] Called when a widget's
     *   form opens or closes, so the dialog can hold off saving the link meanwhile; and when the widgets, or the one being
     *   edited, change, so it can refresh its preview.
     */
    constructor(root, { onEditingChanged, onChanged } = {}) {
        this.root = root;
        this.onEditingChanged = onEditingChanged ?? (() => {});
        this.onChanged = onChanged ?? (() => {});
        this.kindsUrl = root.dataset.widgetKindsUrl;
        this.choicesUrl = root.dataset.choicesUrl;
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
        this.result = root.querySelector('[data-role="widget-result"]');
        // Option fields' IDs follow the section's own (link-dialog-widget-kind gives link-dialog-widget-option-…), so two
        // dialogs on one page don't clash.
        this.optionIdPrefix = this.kindSelect.id.replace(/kind$/, 'option-');
        this.labelField = root.querySelector('[data-role="widget-label-field"]');
        this.labelInput = root.querySelector('[data-role="widget-label"]');
        this.full = root.querySelector('[data-role="widget-full"]');
        this.placement = 'tile';
        this.maxWidgets = 10;
        this.kinds = [];
        // Choice lists read since the dialog opened, as promises by integration/list, and their labels by value.
        this.choiceLists = new Map();
        this.choiceLabels = new Map();
        this.widgets = [];
        // The index of the widget being edited, -1 for a new one, or null when the form is closed.
        this.editing = null;

        this.addButton.addEventListener('click', () => this.openForm(-1));
        this.kindSelect.addEventListener('change', () => {
            this.showFields(this.selectedKind(), {});
            this.onChanged();
        });
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
            this.onChanged();
            (index >= 0 ? this.list.children[index]?.querySelector('[data-role="edit"]') : this.addButton)?.focus();
        });
    }

    /**
     * Sets where the widgets go: a tile, whose widgets have no labels, or a status bar, whose items do.
     * @param {{ placement: string, maxWidgets: number }} place The placement (tile or statusBar) and the most widgets.
     */
    configure({ placement, maxWidgets }) {
        this.placement = placement;
        this.maxWidgets = maxWidgets;
    }

    /** Whether the widgets have labels, as a status bar's items do. */
    get hasLabels() {
        return this.placement !== 'tile';
    }

    /** Whether a widget is being added or edited, which has to be finished before the link or panel is saved. */
    get isEditing() {
        return this.editing !== null;
    }

    /**
     * Shows a link's widgets, as the Editor's Link handler describes them.
     * @param {object[]} widgets The widgets, in order.
     */
    async load(widgets) {
        this.widgets = widgets.map(widget => ({ ...widget, refreshSeconds: widget.refreshSeconds ?? '' }));
        this.choiceLists.clear();
        this.closeForm();
        this.render();

        try {
            const url = new URL(this.kindsUrl, window.location.origin);
            url.searchParams.set('placement', this.placement);
            this.kinds = await window.h2sApi.getJson(url.pathname + url.search);
        } catch {
            this.kinds = [];
        }
        this.render();

        // Read the lists the widgets' values come from, so the list can name them (Plex (#12), not 12).
        const lists = this.widgets.flatMap(widget => (this.kinds.find(kind => kind.reference === widget.kind)?.options ?? [])
            .filter(option => option.choicesFrom)
            .map(option => this.choices(integrationOf(widget.kind), option.choicesFrom)));
        if (lists.length) {
            await Promise.all(lists);
            this.render();
        }
    }

    /** The widgets as the link handlers take them, as JSON. */
    value() {
        return JSON.stringify(this.widgets.map(postedWidget));
    }

    /**
     * The widgets to preview, as the TilePreview handler takes them: the known ones, with the widget being edited in
     * place (or added at the end) when its fields are valid.
     * @returns {{ widgets: string, draftIndex: number | null }} The widgets as JSON, and the edited widget's position.
     */
    previewValue() {
        const widgets = this.widgets.filter(widget => widget.isKnown !== false);
        let draftIndex = null;
        const draft = this.isEditing ? this.readForm(false) : null;

        if (draft) {
            draftIndex = this.editing >= 0
                ? this.widgets.slice(0, this.editing).filter(widget => widget.isKnown !== false).length
                : widgets.length;
            widgets.splice(draftIndex, this.editing >= 0 ? 1 : 0, draft);
        }

        return { widgets: JSON.stringify(widgets.map(postedWidget)), draftIndex };
    }

    /**
     * Says what the widget being edited reads, as the preview found it.
     * @param {string} text The widget's description, or why it can't be shown.
     */
    showResult(text) {
        this.result.textContent = text;
        this.result.classList.toggle('d-none', !text);
    }

    /**
     * Reads a choice list once since the dialog opened; one that can't be read resolves with the reason.
     * @param {string} integration The integration's key.
     * @param {string} list The list's name.
     */
    choices(integration, list) {
        const key = `${integration}/${list}`;
        if (!this.choiceLists.has(key)) {
            const url = new URL(this.choicesUrl, window.location.origin);
            url.searchParams.set('integration', integration);
            url.searchParams.set('list', list);

            this.choiceLists.set(key, window.h2sApi.getJson(url.pathname + url.search)
                .then(result => {
                    this.choiceLabels.set(key, new Map(result.choices.map(choice => [choice.value, choice.label])));
                    return result;
                })
                .catch(error => {
                    // Let a later form try again.
                    this.choiceLists.delete(key);
                    return { choices: [], message: `Couldn't read the choices: ${error.message}` };
                }));
        }
        return this.choiceLists.get(key);
    }

    selectedKind() {
        return this.kinds.find(kind => kind.reference === this.kindSelect.value);
    }

    hasAppWidget(except) {
        return this.widgets.some((widget, index) => index !== except && widget.role === 'app');
    }

    // A short description of a widget's settings, such as "Monitor Plex (#12)".
    summary(widget) {
        const kind = this.kinds.find(candidate => candidate.reference === widget.kind);
        if (!kind) {
            return '';
        }
        return kind.options
            .filter(option => option.type !== 'boolean' && option.type !== 'secret' && postedValue(widget.values[option.key]) !== '')
            .map(option => {
                const value = postedValue(widget.values[option.key]);
                const choice = option.choicesFrom
                    ? this.choiceLabels.get(`${integrationOf(kind.reference)}/${option.choicesFrom}`)?.get(value)
                    : option.choices?.find(candidate => candidate.value === value)?.label;
                return `${option.label} ${choice ?? value}`;
            })
            .join(', ');
    }

    render() {
        this.list.replaceChildren(...this.widgets.map((widget, index) => {
            const item = document.createElement('li');
            item.className = 'list-group-item d-flex align-items-center gap-2';

            const text = document.createElement('div');
            text.className = 'flex-grow-1 small';
            // A labelled widget goes by its label, with its kind among the details.
            const name = document.createElement('div');
            name.className = 'fw-semibold';
            name.textContent = widget.label || widget.displayName;
            const details = document.createElement('div');
            details.className = 'text-muted';
            details.textContent = widget.isKnown === false
                ? "This version of h2s doesn't know it; you can only remove it."
                : [widget.label ? widget.displayName : '', this.summary(widget)].filter(Boolean).join(': ');
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
                this.onChanged();
                (this.list.children[Math.min(index, this.widgets.length - 1)]?.querySelector('[data-role="edit"]') ?? this.addButton).focus();
            });
            item.append(remove);
            return item;
        }));

        const isFull = this.widgets.length >= this.maxWidgets;
        this.empty.classList.toggle('d-none', this.widgets.length > 0 || this.kinds.length === 0);
        this.unavailable.classList.toggle('d-none', this.kinds.length > 0);
        this.full.textContent = isFull ? `That's the most there can be: ${this.maxWidgets}.` : '';
        this.full.classList.toggle('d-none', !isFull);
        this.addButton.classList.toggle('d-none', this.kinds.length === 0);
        this.addButton.disabled = this.isEditing || isFull;
    }

    move(index, offset) {
        const target = index + offset;
        [this.widgets[index], this.widgets[target]] = [this.widgets[target], this.widgets[index]];
        this.render();
        this.onChanged();
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
        this.labelField.classList.toggle('d-none', !this.hasLabels);
        this.labelInput.value = widget?.label ?? '';
        this.form.classList.remove('was-validated', 'd-none');
        this.showResult('');
        this.render();
        this.onEditingChanged(true);
        this.onChanged();
        (widget
            ? (this.hasLabels ? this.labelInput : this.fields.querySelector('input:not(.d-none), select:not(.d-none)'))
            : this.kindSelect)?.focus();
    }

    showFields(kind, values) {
        const loadChoices = kind ? list => this.choices(integrationOf(kind.reference), list) : null;
        this.fields.replaceChildren(...(kind?.options ?? []).map(option => createOptionField(option,
            option.type === 'boolean'
                ? (values[option.key] ?? option.defaultValue) === true || values[option.key] === 'true'
                : values[option.key] ?? option.defaultValue,
            { idPrefix: this.optionIdPrefix, loadChoices })));
        this.refreshHelp.textContent = kind ? `Leave blank for every ${kind.defaultRefreshSeconds} seconds.` : '';
    }

    closeForm() {
        const wasEditing = this.editing !== null;
        this.editing = null;
        this.form.classList.add('d-none');
        this.fields.replaceChildren();
        this.showResult('');
        this.addButton.disabled = false;
        if (wasEditing) {
            this.onEditingChanged(false);
        }
    }

    /**
     * Reads the widget form.
     * @param {boolean} report Whether to show what's invalid and focus it.
     * @returns {object | null} The widget, or null when the form isn't valid.
     */
    readForm(report) {
        const kind = this.selectedKind();
        if (!kind) {
            return null;
        }

        if (report) {
            this.form.classList.add('was-validated');
        }
        const invalid = [...this.form.querySelectorAll('input, select')].find(input => !input.checkValidity());
        if (invalid) {
            if (report) {
                invalid.focus();
            }
            return null;
        }

        const index = this.editing;
        return {
            id: index >= 0 ? this.widgets[index].id : null,
            kind: kind.reference,
            displayName: kind.displayName,
            isKnown: true,
            role: kind.role,
            values: Object.fromEntries(readOptionFields(this.fields).map(([name, value]) => [name.replace(/^options\[(.*)\]$/, '$1'), value])),
            refreshSeconds: this.refresh.value.trim(),
            label: this.hasLabels ? this.labelInput.value.trim() : ''
        };
    }

    commit() {
        const widget = this.readForm(true);
        if (!widget) {
            return;
        }

        const index = this.editing;
        if (index >= 0) {
            this.widgets[index] = widget;
        } else {
            this.widgets.push(widget);
        }

        const position = index >= 0 ? index : this.widgets.length - 1;
        this.closeForm();
        this.render();
        this.onChanged();
        this.list.children[position]?.querySelector('[data-role="edit"]')?.focus();
    }
}
