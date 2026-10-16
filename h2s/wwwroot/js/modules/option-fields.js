/* Form fields built from an option schema, as h2s.Options.OptionDescriptions describes it. The panel dialog and the
   Settings → Integrations cards both use them. Fields post as the handlers take them: options[key], booleans as true or
   false; secrets[key] for a newly typed secret; and clearSecrets=key to clear one. A secret's value never reaches the
   browser, so its field shows only whether it's set. */

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

/**
 * Builds the field for one option.
 * @param {object} option The option, as OptionDescriptions describes it.
 * @param {*} value The current value; ignored for secrets.
 * @param {{ idPrefix: string, secretIsSet?: boolean }} context The prefix for element IDs, and whether a secret is set.
 * @returns {HTMLElement} The field.
 */
export function createOptionField(option, value, { idPrefix, secretIsSet = false }) {
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
        input.setAttribute('aria-describedby', help.id);
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
        const input = field.querySelector('input, select');
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
