/* Settings → Integrations: one card per integration, its fields built from the integration's connection schema by
   option-fields.js. Save, Test connection and Remove (after a confirmation) call the page's SaveConnection,
   TestConnection and RemoveConnection handlers, and each card re-renders from what Save and Remove answer. Elements
   marked data-shown-with="<key>" (Uptime Kuma's infrastructure groups) show only while that integration has a
   connection. */
import { createOptionField, readOptionFields } from './option-fields.js';
import { showToast } from './toast.js';

const root = document.getElementById('integrations');
const cards = JSON.parse(document.getElementById('integrations-data').textContent);

const showError = (form, message) => {
    const error = form.querySelector('[data-role="error"]');
    error.textContent = message;
    error.classList.toggle('d-none', !message);
};

const render = (form, card) => {
    const fields = form.querySelector('[data-role="fields"]');
    fields.replaceChildren(...card.options.map(option => createOptionField(option, card.values[option.key] ?? option.defaultValue, {
        idPrefix: `integration-${card.key}-`,
        secretIsSet: card.secretsSet[option.key] === true
    })));

    form.querySelector('[data-role="state"]').classList.toggle('d-none', card.isConfigured);
    form.querySelector('[data-role="remove"]').classList.toggle('d-none', !card.isConfigured);
    form.querySelector('[data-role="remove-confirm"]').classList.add('d-none');
    form.classList.remove('was-validated');

    for (const element of document.querySelectorAll(`[data-shown-with="${card.key}"]`)) {
        element.classList.toggle('d-none', !card.isConfigured);
    }
};

for (const card of cards) {
    const form = root.querySelector(`[data-role="integration-form"][data-integration="${card.key}"]`);
    if (!form) {
        continue;
    }

    const fields = form.querySelector('[data-role="fields"]');
    const save = form.querySelector('[type="submit"]');
    const test = form.querySelector('[data-role="test"]');
    const testResult = form.querySelector('[data-role="test-result"]');
    const remove = form.querySelector('[data-role="remove"]');
    const confirm = form.querySelector('[data-role="remove-confirm"]');
    const values = () => [['integration', card.key], ...readOptionFields(fields)];

    render(form, card);

    form.addEventListener('submit', async event => {
        event.preventDefault();
        form.classList.add('was-validated');
        if (!form.checkValidity()) {
            form.querySelector(':invalid')?.focus();
            return;
        }

        showError(form, '');
        save.disabled = true;
        try {
            render(form, await window.h2sApi.postForm(root.dataset.saveUrl, values()));
            showToast(`Saved ${card.displayName}.`, { variant: 'success', delay: 3000 });
        } catch (error) {
            showError(form, error.message);
        } finally {
            save.disabled = false;
        }
    });

    test.addEventListener('click', async () => {
        test.disabled = true;
        testResult.className = 'alert alert-secondary';
        testResult.textContent = 'Testing…';
        try {
            const outcome = await window.h2sApi.postForm(root.dataset.testUrl, values());
            testResult.className = `alert alert-${outcome.level}`;
            testResult.textContent = outcome.message;
        } catch (error) {
            testResult.className = 'alert alert-danger';
            testResult.textContent = error.message;
        } finally {
            test.disabled = false;
        }
    });

    remove.addEventListener('click', () => {
        confirm.classList.remove('d-none');
        confirm.querySelector('[data-role="remove-no"]').focus();
    });

    confirm.querySelector('[data-role="remove-no"]').addEventListener('click', () => {
        confirm.classList.add('d-none');
        remove.focus();
    });

    confirm.querySelector('[data-role="remove-yes"]').addEventListener('click', async () => {
        showError(form, '');
        try {
            render(form, await window.h2sApi.postForm(root.dataset.removeUrl, { integration: card.key }));
            testResult.className = 'alert d-none';
            showToast(`Removed ${card.displayName}.`, { variant: 'success', delay: 3000 });
            save.focus();
        } catch (error) {
            confirm.classList.add('d-none');
            showError(form, error.message);
        }
    });
}
