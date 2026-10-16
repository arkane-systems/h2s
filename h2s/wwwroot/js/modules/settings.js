/* The Settings page's General and Appearance cards: each form saves on its own through its JSON handler, showing
   problems inline and success as a toast. Integrations, Search and Backup have modules of their own. */
import { ChipInput } from './chip-input.js';
import { showToast } from './toast.js';

const chipInputs = [...document.querySelectorAll('[data-role="chip-input"]')].map(container =>
    new ChipInput(container, container.parentElement.querySelector('[data-role="chip-value"]')));

const showError = (form, message) => {
    const error = form.querySelector('[data-role="error"]');
    error.textContent = message;
    error.classList.toggle('d-none', !message);
};

// What each section does once it's saved, beyond the toast.
const afterSave = {
    general: result => {
        // The navbar shows the title and motto; the browser tab, the title.
        const title = document.querySelector('[data-role="site-title"]');
        const motto = document.querySelector('[data-role="site-motto"]');
        if (title) {
            title.textContent = result.title;
        }
        if (motto) {
            motto.textContent = result.motto;
            motto.classList.toggle('d-none', !result.motto);
        }
        document.title = document.title.replace(/^h2s - .*? - Settings$/, `h2s - ${result.title} - Settings`);
    },
    appearance: result => window.h2sColorMode?.setSiteDefault(result.colorMode)
};

for (const form of document.querySelectorAll('[data-role="settings-form"]')) {
    const section = form.dataset.section;

    form.addEventListener('submit', async event => {
        event.preventDefault();
        chipInputs.forEach(chips => chips.commit());

        form.classList.add('was-validated');
        if (!form.checkValidity()) {
            form.querySelector(':invalid')?.focus();
            return;
        }

        showError(form, '');
        const button = form.querySelector('[type="submit"]');
        button.disabled = true;

        try {
            const result = await window.h2sApi.postForm(form.dataset.saveUrl, [...new FormData(form)]);
            afterSave[section]?.(result);
            form.classList.remove('was-validated');
            showToast(`Saved ${form.querySelector('h2, h3').textContent}.`, { variant: 'success', delay: 3000 });
        } catch (error) {
            showError(form, error.message);
        } finally {
            button.disabled = false;
        }
    });
}
