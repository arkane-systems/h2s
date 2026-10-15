/* A searchable picker over the selfh.st icons, shown inside the link dialog. */

const debounce = (action, wait) => {
    let timer = 0;
    return (...args) => {
        window.clearTimeout(timer);
        timer = window.setTimeout(() => action(...args), wait);
    };
};

export class IconPicker {
    /**
     * @param {HTMLElement} panel The picker panel, holding [data-role="icon-search"] and [data-role="icon-results"].
     * @param {HTMLButtonElement} toggle The button that opens and closes the panel.
     * @param {{ searchUrl: string, onPick: (reference: string) => void }} options The SearchIcons handler URL, and what
     *   to do with the chosen icon's reference.
     */
    constructor(panel, toggle, { searchUrl, onPick }) {
        this.panel = panel;
        this.toggle = toggle;
        this.searchUrl = searchUrl;
        this.onPick = onPick;
        this.search = panel.querySelector('[data-role="icon-search"]');
        this.results = panel.querySelector('[data-role="icon-results"]');
        this.request = 0;

        const searchSoon = debounce(() => this.find(this.search.value), 250);
        this.search.addEventListener('input', searchSoon);

        // Escape closes the picker rather than the whole dialog.
        this.search.addEventListener('keydown', event => {
            if (event.key === 'Escape') {
                event.stopPropagation();
                this.close();
                this.toggle.focus();
            }
        });

        this.results.addEventListener('click', event => {
            const option = event.target.closest('[data-reference]');
            if (option) {
                this.onPick(option.dataset.reference);
                this.close();
            }
        });
    }

    get isOpen() {
        return !this.panel.classList.contains('d-none');
    }

    /** Opens the picker, searching for a starting query such as the link's label. */
    open(query) {
        this.panel.classList.remove('d-none');
        this.toggle.setAttribute('aria-expanded', 'true');
        this.search.value = query ?? '';
        this.find(this.search.value);
        this.search.focus();
    }

    close() {
        this.panel.classList.add('d-none');
        this.toggle.setAttribute('aria-expanded', 'false');
    }

    showMessage(message) {
        const note = document.createElement('p');
        note.className = 'text-muted small mb-0';
        note.textContent = message;
        this.results.replaceChildren(note);
    }

    async find(query) {
        const request = ++this.request;

        if (!query.trim()) {
            this.showMessage('Type to search the selfh.st icons.');
            return;
        }

        try {
            const url = new URL(this.searchUrl, window.location.origin);
            url.searchParams.set('q', query.trim());
            const icons = await window.h2sApi.getJson(url.pathname + url.search);

            if (request !== this.request) {
                return;
            }

            if (icons.length === 0) {
                this.showMessage('No icons match.');
                return;
            }

            this.results.replaceChildren(...icons.map(icon => {
                const option = document.createElement('button');
                option.type = 'button';
                option.className = 'icon-picker-option';
                option.dataset.reference = icon.reference;
                option.title = `${icon.name} (${icon.reference})`;

                const image = document.createElement('img');
                image.src = icon.url;
                image.alt = '';
                image.loading = 'lazy';

                const name = document.createElement('span');
                name.textContent = icon.name;

                option.append(image, name);
                return option;
            }));
        } catch {
            if (request === this.request) {
                this.showMessage("Couldn't search the icons.");
            }
        }
    }
}
