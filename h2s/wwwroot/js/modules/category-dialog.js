/* The add/edit category dialog for dashboard edit mode (Pages/Shared/_CategoryDialog.cshtml). */

export class CategoryDialog {
    /**
     * @param {HTMLElement} element The dialog's .modal element.
     * @param {{ onSaved: (category: object, previous: object|null) => void, onDeleted: (result: object) => void }} hooks
     *   Called after a successful save (with the handler's JSON and the category as it was, or null when new) and delete
     *   (with its JSON, including the Undo snapshot).
     */
    constructor(element, { onSaved, onDeleted }) {
        this.urls = element.dataset;
        this.onSaved = onSaved;
        this.onDeleted = onDeleted;
        this.modal = window.bootstrap.Modal.getOrCreateInstance(element);
        this.form = element.querySelector('form');
        this.name = this.form.elements.name;
        this.isAdmin = this.form.elements.isAdminCategory;
        this.title = element.querySelector('.modal-title');
        this.error = element.querySelector('[data-role="error"]');
        this.deleteButton = element.querySelector('[data-role="delete"]');
        this.previous = null;

        this.form.addEventListener('submit', event => {
            event.preventDefault();
            this.save();
        });
        this.deleteButton.addEventListener('click', () => this.delete());
        element.addEventListener('shown.bs.modal', () => this.name.focus());
    }

    openNew() {
        this.reset();
        this.title.textContent = 'Add category';
        this.deleteButton.classList.add('d-none');
        this.modal.show();
    }

    /** @param {{ id: string, name: string, isAdminCategory: boolean }} category */
    openExisting(category) {
        this.reset();
        this.previous = category;
        this.title.textContent = 'Edit category';
        this.name.value = category.name;
        this.isAdmin.checked = category.isAdminCategory;
        this.deleteButton.textContent = 'Delete category and its links';
        this.deleteButton.classList.remove('d-none');
        this.modal.show();
    }

    reset() {
        this.form.reset();
        this.form.classList.remove('was-validated');
        this.previous = null;
        this.showError('');
    }

    showError(message) {
        this.error.textContent = message;
        this.error.classList.toggle('d-none', !message);
    }

    async save() {
        this.form.classList.add('was-validated');
        if (!this.form.checkValidity()) {
            this.name.focus();
            return;
        }

        this.showError('');
        const data = { name: this.name.value.trim(), isAdminCategory: this.isAdmin.checked ? 'true' : 'false' };

        try {
            const category = this.previous
                ? await window.h2sApi.postForm(this.urls.updateUrl, { id: this.previous.id, ...data })
                : await window.h2sApi.postForm(this.urls.createUrl, data);
            this.modal.hide();
            this.onSaved(category, this.previous);
        } catch (error) {
            this.showError(error.message);
        }
    }

    async delete() {
        this.showError('');

        try {
            const result = await window.h2sApi.postForm(this.urls.deleteUrl, { id: this.previous.id });
            this.modal.hide();
            this.onDeleted(result);
        } catch (error) {
            this.showError(error.message);
        }
    }
}
