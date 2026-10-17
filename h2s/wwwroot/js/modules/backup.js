/* Settings → Backup: import a dashboard file in two steps. The server first checks it and summarizes what it holds; only
   confirming replaces the configuration. Export is a plain download link. */
import { showToast } from './toast.js';

const section = document.getElementById('backup');

if (section) {
    const fileInput = section.querySelector('[data-role="backup-file"]');
    const error = section.querySelector('[data-role="backup-error"]');
    const preview = section.querySelector('[data-role="backup-preview"]');
    const fileName = section.querySelector('[data-role="backup-file-name"]');
    const summary = section.querySelector('[data-role="backup-summary"]');
    const warning = section.querySelector('[data-role="backup-warning"]');
    const confirm = section.querySelector('[data-role="backup-confirm"]');
    let file = null;

    const plural = (count, noun, nouns = `${noun}s`) => `${count} ${count === 1 ? noun : nouns}`;

    const showError = message => {
        error.textContent = message;
        error.classList.toggle('d-none', !message);
    };

    const reset = () => {
        file = null;
        fileInput.value = '';
        preview.classList.add('d-none');
    };

    const upload = url => {
        const form = new FormData();
        form.append('file', file);
        return window.h2sApi.postForm(url, form);
    };

    fileInput.addEventListener('change', async () => {
        file = fileInput.files[0] ?? null;
        showError('');
        preview.classList.add('d-none');
        if (!file) {
            return;
        }

        try {
            const { incoming, current } = await upload(section.dataset.previewUrl);
            const exported = incoming.exportedAt ? `, exported ${new Date(incoming.exportedAt).toLocaleString()}` : '';
            const admin = incoming.adminCategories ? ` (${incoming.adminCategories} admin)` : '';

            fileName.textContent = file.name;
            summary.textContent = `“${incoming.title}”${exported}: ${plural(incoming.categories, 'category', 'categories')}${admin}, `
                + `${plural(incoming.links, 'link')}, `
                + `${plural(incoming.panels, 'panel')}, ${plural(incoming.searchEngines, 'search engine')}, `
                + `${plural(incoming.connections, 'connection')}, ${plural(incoming.widgets, 'widget')} and its settings.`;
            warning.textContent = `Importing replaces everything here now: ${plural(current.categories, 'category', 'categories')}, `
                + `${plural(current.links, 'link')}, `
                + `${plural(current.panels, 'panel')}, ${plural(current.searchEngines, 'search engine')}, `
                + `${plural(current.connections, 'connection')}, ${plural(current.widgets, 'widget')} and the settings. `
                + 'Export first if you might want them back.';
            preview.classList.remove('d-none');
            confirm.focus();
        } catch (problem) {
            showError(problem.message);
            reset();
        }
    });

    section.querySelector('[data-role="backup-cancel"]').addEventListener('click', reset);

    confirm.addEventListener('click', async () => {
        confirm.disabled = true;
        try {
            await upload(section.dataset.importUrl);
            // Everything on this page, the title in the navbar included, may have changed.
            window.location.reload();
        } catch (problem) {
            showError(problem.message);
            showToast(`Import failed: ${problem.message}`);
        } finally {
            confirm.disabled = false;
        }
    });
}
