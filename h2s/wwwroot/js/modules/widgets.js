/* Keeps the dashboard's widgets (Pages/Shared/_Widget.cshtml) current. Each widget says how often to read it
   (data-widget-refresh, in seconds) and where (data-widget-url); one rendered without a result yet (.widget-pending)
   is read straight away. A read swaps in the server's fresh widget. Nothing is read while the tab is hidden, and widgets
   that fell due meanwhile are read as soon as it's shown again. Widgets the page adds later, such as a tile edit mode
   re-renders, are picked up on the next tick. */

const TickMilliseconds = 5000;

// When each widget is next due, by widget ID, and the widgets being read now.
const due = new Map();
const reading = new Set();

const refreshMilliseconds = element => Math.max(Number(element.dataset.widgetRefresh) || 60, 15) * 1000;

const read = async element => {
    const id = element.dataset.widgetId;
    reading.add(id);

    try {
        const template = document.createElement('template');
        template.innerHTML = (await window.h2sApi.getText(element.dataset.widgetUrl)).trim();
        const fresh = template.content.firstElementChild;

        // The page may have replaced the tile meanwhile; then its own widget is read in its turn.
        if (fresh && element.isConnected) {
            element.replaceWith(fresh);
        }
    } catch {
        // Keep what's shown, and try again at the next interval.
    } finally {
        reading.delete(id);
        due.set(id, Date.now() + refreshMilliseconds(element));
    }
};

const tick = () => {
    if (document.hidden) {
        return;
    }

    const now = Date.now();

    for (const element of document.querySelectorAll('[data-widget-id]')) {
        const id = element.dataset.widgetId;

        if (reading.has(id)) {
            continue;
        }

        if (!due.has(id)) {
            due.set(id, element.classList.contains('widget-pending') ? now : now + refreshMilliseconds(element));
        }

        if (due.get(id) <= now) {
            read(element);
        }
    }
};

tick();
window.setInterval(tick, TickMilliseconds);
document.addEventListener('visibilitychange', tick);
