/* Form fields built from an option schema, as h2s.Options.OptionDescriptions describes it. The panel dialog and the
   Settings → Integrations cards both use them. Fields post as the handlers take them: options[key], booleans as true or
   false; secrets[key] for a newly typed secret; and clearSecrets=key to clear one. A secret's value never reaches the
   browser, so its field shows only whether it's set. An option with a choice list (choicesFrom) offers the list's
   choices, read from the service, beside its usual field for a value the list doesn't hold. */

const make = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) {
        element.className = className;
    }
    if (text !== undefined) {
        element.textContent = text;
    }
    return element;
};

const button = (text, className, label) => {
    const element = make('button', className, text);
    element.type = 'button';
    if (label) {
        element.setAttribute('aria-label', label);
    }
    return element;
};

// What the field says when its value isn't valid.
const invalidMessage = option => {
    if (option.type === 'integer') {
        const bounds = [
            option.minimum !== null ? `at least ${option.minimum}` : '',
            option.maximum !== null ? `at most ${option.maximum}` : ''
        ].filter(Boolean).join(' and ');
        return `Enter a whole number${bounds ? ` of ${bounds}` : ''}${option.isRequired ? '' : ', or leave it blank'}.`;
    }
    if (option.type === 'url') {
        return `Enter an address starting with http:// or https://${option.isRequired ? '' : ', or leave it blank'}.`;
    }
    if (option.pattern) {
        return `${option.label} ${option.patternDescription ?? "isn't in the expected form"}.`;
    }
    return `${option.label} is required.`;
};

const textInput = (option, id) => {
    const input = make('input', 'form-control');
    input.id = id;
    input.autocomplete = 'off';
    if (option.maxLength) {
        input.maxLength = option.maxLength;
    }
    if (option.placeholder) {
        input.placeholder = option.placeholder;
    }

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
    } else if (option.type === 'url') {
        input.type = 'url';
        input.pattern = 'https?://.+';
    } else if (option.type === 'secret') {
        input.type = 'password';
        input.autocomplete = 'new-password';
    } else {
        input.type = 'text';
        if (option.pattern) {
            // The server trims values and accepts a blank optional one, so the browser should too.
            input.pattern = `\\s*(?:${option.pattern})?\\s*`;
        }
    }
    return input;
};

// A secret that's already set: Set, with Replace (shows a password box) and, when it's optional, Clear.
const storedSecret = (field, option, id) => {
    field.dataset.secretState = 'kept';

    const label = make('label', 'form-label', option.label);
    label.htmlFor = id;

    const status = make('div', 'd-flex flex-wrap align-items-center gap-2');
    const badge = make('span', 'badge text-bg-success', 'Set');
    const replace = button('Replace', 'btn btn-sm btn-outline-secondary', `Replace ${option.label}`);
    const clear = button('Clear', 'btn btn-sm btn-outline-danger', `Clear ${option.label}`);
    const keep = button('Keep', 'btn btn-sm btn-outline-secondary d-none', `Keep ${option.label}`);
    const note = make('span', 'small text-muted');
    status.append(badge, replace, ...(option.isRequired ? [] : [clear]), keep, note);

    const input = textInput(option, id);
    input.classList.add('mt-2', 'd-none');
    input.placeholder = 'New value';

    const show = state => {
        field.dataset.secretState = state;
        badge.className = state === 'clear' ? 'badge text-bg-secondary' : 'badge text-bg-success';
        badge.textContent = state === 'clear' ? 'Clearing' : 'Set';
        input.classList.toggle('d-none', state !== 'replace');
        replace.classList.toggle('d-none', state !== 'kept');
        clear.classList.toggle('d-none', state !== 'kept');
        keep.classList.toggle('d-none', state === 'kept');
        note.textContent = state === 'clear' ? 'Cleared when you save.' : '';
        if (state !== 'replace') {
            input.value = '';
        }
    };

    replace.addEventListener('click', () => {
        show('replace');
        input.focus();
    });
    clear.addEventListener('click', () => {
        show('clear');
        keep.focus();
    });
    keep.addEventListener('click', () => {
        show('kept');
        replace.focus();
    });

    field.append(label, status, input);
    return input;
};

// A sentinel for the choice that types a value the list doesn't hold.
const OtherValue = 'h2s:other';

// An option offering a choice list's values: a select of the list (grouped), with "Another value…" showing the option's
// usual field, which is what's posted. While the list loads, and if it can't be read, the field shows on its own.
const liveChoices = (field, option, id, value, loadChoices) => {
    const label = make('label', 'form-label', option.label);
    const select = make('select', 'form-select d-none');
    select.id = `${id}-choice`;
    select.setAttribute('aria-label', option.label);

    const input = textInput(option, id);
    input.required = option.isRequired;
    input.value = value ?? '';
    input.setAttribute('aria-label', option.label);
    label.htmlFor = id;

    const feedback = make('div', 'invalid-feedback', invalidMessage(option));
    const status = make('div', 'form-text', 'Loading the choices…');
    status.setAttribute('aria-live', 'polite');
    field.append(label, select, input, feedback, status);

    const showInput = visible => {
        input.classList.toggle('d-none', !visible);
        input.classList.toggle('mt-2', visible && !select.classList.contains('d-none'));
        feedback.textContent = visible ? invalidMessage(option) : `Choose ${option.label.toLowerCase()}.`;
    };

    select.addEventListener('change', () => {
        const isOther = select.value === OtherValue;
        showInput(isOther);
        if (isOther) {
            input.focus();
        } else {
            input.value = select.value;
            // Let the form's listeners (such as a preview) see the change.
            input.dispatchEvent(new Event('input', { bubbles: true }));
        }
    });

    loadChoices(option.choicesFrom).then(list => {
        if (!input.isConnected || !list.choices.length) {
            status.textContent = list.message ?? 'There are no choices to pick from; enter a value.';
            return;
        }

        select.append(new Option(option.isRequired ? `Choose ${option.label.toLowerCase()}…` : option.placeholder ?? 'None', ''));
        let group = null;
        for (const choice of list.choices) {
            if (choice.group && choice.group !== group?.label) {
                group = make('optgroup');
                group.label = choice.group;
                select.append(group);
            } else if (!choice.group) {
                group = null;
            }
            (group ?? select).append(new Option(choice.label, choice.value));
        }
        select.append(new Option('Another value…', OtherValue));

        const current = input.value.trim();
        const listed = current !== '' && list.choices.some(choice => choice.value === current);
        select.value = current === '' ? '' : listed ? current : OtherValue;
        select.required = option.isRequired;
        select.classList.remove('d-none');
        label.htmlFor = select.id;
        status.textContent = '';
        status.classList.add('d-none');
        showInput(current !== '' && !listed);
        // Focus may have gone to the input before the list arrived; keep it on the visible control.
        if (document.activeElement === input && input.classList.contains('d-none')) {
            select.focus();
        }
    });

    return input;
};

/**
 * Builds the field for one option.
 * @param {object} option The option, as OptionDescriptions describes it.
 * @param {*} value The current value; ignored for secrets.
 * @param {{ idPrefix: string, secretIsSet?: boolean, loadChoices?: (list: string) => Promise<{ choices: object[], message?: string }> }} context
 *   The prefix for element IDs, whether a secret is set, and how to read a choice list (one that fails resolves with a
 *   message). Without loadChoices, an option with a choice list is a plain field.
 * @returns {HTMLElement} The field.
 */
export function createOptionField(option, value, { idPrefix, secretIsSet = false, loadChoices = null }) {
    const id = `${idPrefix}${option.key}`;
    const field = make('div', 'mb-3');
    field.dataset.optionKey = option.key;
    field.dataset.optionType = option.type;

    let input;

    if (option.type === 'boolean') {
        field.classList.add('form-check', 'form-switch');
        input = make('input', 'form-check-input');
        input.id = id;
        input.type = 'checkbox';
        input.setAttribute('role', 'switch');
        input.checked = value === true;
        const label = make('label', 'form-check-label', option.label);
        label.htmlFor = id;
        field.append(input, label);
    } else if (option.type === 'select') {
        const label = make('label', 'form-label', option.label);
        label.htmlFor = id;
        input = make('select', 'form-select');
        input.id = id;
        input.required = option.isRequired;
        if (!option.isRequired) {
            input.append(new Option(option.placeholder ?? 'None', ''));
        }
        input.append(...option.choices.map(choice => new Option(choice.label, choice.value)));
        input.value = value ?? '';
        field.append(label, input, make('div', 'invalid-feedback', `Choose ${option.label.toLowerCase()}.`));
    } else if (option.type === 'secret' && secretIsSet) {
        input = storedSecret(field, option, id);
    } else if (option.choicesFrom && loadChoices) {
        input = liveChoices(field, option, id, value, loadChoices);
    } else {
        const label = make('label', 'form-label', option.label);
        label.htmlFor = id;
        input = textInput(option, id);
        input.required = option.isRequired;
        if (option.type === 'secret') {
            field.dataset.secretState = 'new';
        } else {
            input.value = value ?? '';
        }
        field.append(label, input, make('div', 'invalid-feedback', invalidMessage(option)));
    }

    input.name = option.type === 'secret' ? `secrets[${option.key}]` : `options[${option.key}]`;

    const hints = [option.helpText, option.type === 'secret' && !secretIsSet ? 'Not set. It is stored encrypted and never shown again.' : '']
        .filter(Boolean);
    if (hints.length) {
        const help = make('div', 'form-text', hints.join(' '));
        help.id = `${id}-help`;
        for (const control of field.querySelectorAll('input, select')) {
            control.setAttribute('aria-describedby', help.id);
        }
        field.append(help);
    }

    return field;
}

/**
 * Reads the fields' values as the handlers take them.
 * @param {HTMLElement} host The element holding the fields.
 * @returns {string[][]} Name and value pairs.
 */
export function readOptionFields(host) {
    const pairs = [];
    for (const field of host.querySelectorAll('[data-option-key]')) {
        // The posted control: a choice list's select only fills in its field.
        const input = field.querySelector('input[name], select[name]');
        if (field.dataset.optionType === 'boolean') {
            pairs.push([input.name, String(input.checked)]);
        } else if (field.dataset.optionType === 'secret') {
            if (field.dataset.secretState === 'clear') {
                pairs.push(['clearSecrets', field.dataset.optionKey]);
            } else if (input.value.trim()) {
                pairs.push([input.name, input.value.trim()]);
            }
        } else {
            pairs.push([input.name, input.value.trim()]);
        }
    }
    return pairs;
}
