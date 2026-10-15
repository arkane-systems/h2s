/* A chip input for a list of domains: each entry becomes a removable chip, and the list is kept in a hidden input as a
   comma-separated string, which is the form the server already parses. */

// Reduces an entry to a lowercase host name: "https://Wiki.Home.Arpa/x" and ".home.arpa" become "wiki.home.arpa" and
// "home.arpa".
const normalize = value => {
    let text = value.trim().toLowerCase();
    try {
        if (/^[a-z][a-z0-9+.-]*:\/\//.test(text)) {
            text = new URL(text).hostname;
        }
    } catch {
        // Not a URL after all; keep the text.
    }
    return text.replace(/^\.+/, '').replace(/\/.*$/, '');
};

const split = text => text.split(/[\s,;]+/).map(normalize).filter(Boolean);

export class ChipInput {
    /**
     * @param {HTMLElement} container The .chip-input element, holding the text entry.
     * @param {HTMLInputElement} hidden The hidden input that is posted.
     */
    constructor(container, hidden) {
        this.container = container;
        this.hidden = hidden;
        this.entry = container.querySelector('.chip-input-entry');
        this.chips = [];

        for (const value of split(hidden.value)) {
            this.add(value);
        }

        container.addEventListener('click', event => {
            const remove = event.target.closest('[data-chip]');
            if (remove) {
                this.remove(remove.dataset.chip);
                this.entry.focus();
            } else if (event.target === container) {
                this.entry.focus();
            }
        });

        this.entry.addEventListener('keydown', event => {
            if (['Enter', ',', ';', ' '].includes(event.key)) {
                event.preventDefault();
                this.commit();
            } else if (event.key === 'Backspace' && this.entry.value === '' && this.chips.length > 0) {
                this.remove(this.chips[this.chips.length - 1]);
            }
        });

        this.entry.addEventListener('blur', () => this.commit());

        this.entry.addEventListener('paste', event => {
            event.preventDefault();
            for (const value of split(event.clipboardData.getData('text'))) {
                this.add(value);
            }
        });
    }

    /** Turns whatever is typed in the entry into chips. */
    commit() {
        for (const value of split(this.entry.value)) {
            this.add(value);
        }
        this.entry.value = '';
    }

    add(value) {
        if (!value || this.chips.includes(value)) {
            return;
        }
        this.chips.push(value);

        const chip = document.createElement('span');
        chip.className = 'chip';
        chip.textContent = value;

        const remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'chip-remove';
        remove.dataset.chip = value;
        remove.setAttribute('aria-label', `Remove ${value}`);
        remove.textContent = '×';

        chip.append(remove);
        this.container.insertBefore(chip, this.entry);
        this.sync();
    }

    remove(value) {
        this.chips = this.chips.filter(chip => chip !== value);
        this.container.querySelector(`[data-chip="${CSS.escape(value)}"]`)?.closest('.chip').remove();
        this.sync();
    }

    sync() {
        this.hidden.value = this.chips.join(', ');
        this.hidden.dispatchEvent(new Event('change', { bubbles: true }));
    }
}
