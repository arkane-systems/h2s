/* Short-lived Bootstrap toasts in the layout's #toast-host container. */

/**
 * Shows a toast with a plain-text message.
 * @param {string} message The message; it is inserted as text, never as HTML.
 * @param {{ variant?: string, delay?: number, action?: { label: string, onClick: () => void } }} [options] The Bootstrap
 *   colour variant (default 'danger'), how long the toast stays in milliseconds, and an optional action button, such
 *   as Undo, which also dismisses the toast.
 */
export function showToast(message, { variant = 'danger', delay = 6000, action = null } = {}) {
    const host = document.getElementById('toast-host');

    if (!host || !window.bootstrap) {
        window.alert(message);
        return;
    }

    const toast = document.createElement('div');
    toast.className = `toast align-items-center text-bg-${variant} border-0`;
    toast.setAttribute('role', variant === 'danger' ? 'alert' : 'status');

    const layout = document.createElement('div');
    layout.className = 'd-flex';

    const body = document.createElement('div');
    body.className = 'toast-body';
    body.textContent = message;
    layout.append(body);

    const instance = window.bootstrap.Toast.getOrCreateInstance(toast, { delay });

    if (action) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-sm btn-light my-auto ms-auto';
        button.textContent = action.label;
        button.addEventListener('click', () => {
            instance.hide();
            action.onClick();
        }, { once: true });
        layout.append(button);
    }

    const close = document.createElement('button');
    close.type = 'button';
    close.className = `btn-close btn-close-white me-2 m-auto${action ? ' ms-2' : ''}`;
    close.dataset.bsDismiss = 'toast';
    close.setAttribute('aria-label', 'Close');
    layout.append(close);

    toast.append(layout);
    host.append(toast);

    toast.addEventListener('hidden.bs.toast', () => toast.remove());
    instance.show();
}
