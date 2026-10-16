/* Picks an Uptime Kuma monitor from every monitor Kuma reports, for the link dialog. The posted field stays the plain
   monitor ID input: the select fills it, and it is shown for an ID Kuma doesn't report, or when the monitors
   can't be listed. */

export class MonitorPicker {
    /**
     * @param {HTMLSelectElement} select The monitor select, holding "None" ('') and "Another monitor ID…" ('other').
     * @param {HTMLInputElement} input The monitor ID input that is posted.
     * @param {HTMLElement} status Where to say why the monitors couldn't be listed.
     * @param {string} monitorsUrl The KumaMonitors handler URL.
     */
    constructor(select, input, status, monitorsUrl) {
        this.select = select;
        this.input = input;
        this.status = status;
        this.monitorsUrl = monitorsUrl;
        this.other = select.querySelector('option[value="other"]');
        this.loading = null;

        select.addEventListener('change', () => {
            const isOther = select.value === 'other';
            this.showInput(isOther);
            if (isOther) {
                this.input.focus();
            } else {
                this.input.value = select.value;
            }
            // Let the dialog's preview see the change.
            this.input.dispatchEvent(new Event('input', { bubbles: true }));
        });
    }

    showInput(visible) {
        this.input.classList.toggle('d-none', !visible);
    }

    /** Lists Kuma's monitors once, however often it is asked. */
    load() {
        this.loading ??= this.fetchMonitors();
        return this.loading;
    }

    async fetchMonitors() {
        try {
            const list = await window.h2sApi.getJson(this.monitorsUrl);

            for (const group of list.groups) {
                const optgroup = document.createElement('optgroup');
                optgroup.label = group.name;
                for (const monitor of group.monitors) {
                    optgroup.append(new Option(`${monitor.name} (#${monitor.id})`, String(monitor.id)));
                }
                this.select.insertBefore(optgroup, this.other);
            }

            this.status.textContent = list.message ?? '';
        } catch (error) {
            this.status.textContent = `Couldn't list the monitors: ${error.message}`;
            // Let a later opening try again.
            this.loading = null;
        }
    }

    /** Shows a stored monitor ID: picked in the select when Kuma reports it, otherwise typed in. */
    async setValue(monitorId) {
        const value = monitorId == null ? '' : String(monitorId);
        this.input.value = value;
        await this.load();

        const listed = value !== '' && [...this.select.options].some(option => option.value === value);
        this.select.value = value === '' ? '' : listed ? value : 'other';
        this.showInput(value !== '' && !listed);
    }
}
