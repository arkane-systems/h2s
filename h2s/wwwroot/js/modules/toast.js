/* Short-lived Bootstrap toasts in the layout's #toast-host container. */

/**
 * Shows a toast with a plain-text message.
 * @param {string} message The message; it is inserted as text, never as HTML.
 * @param {{ variant?: string, delay?: number }} [options] The Bootstrap colour variant (default 'danger') and how long
 *   the toast stays, in milliseconds.
 */
export function showToast(message, { variant = 'danger', delay = 6000 } = {}) {
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

    const close = document.createElement('button');
    close.type = 'button';
    close.className = 'btn-close btn-close-white me-2 m-auto';
    close.dataset.bsDismiss = 'toast';
    close.setAttribute('aria-label', 'Close');

    layout.append(body, close);
    toast.append(layout);
    host.append(toast);

    toast.addEventListener('hidden.bs.toast', () => toast.remove());
    window.bootstrap.Toast.getOrCreateInstance(toast, { delay }).show();
}
