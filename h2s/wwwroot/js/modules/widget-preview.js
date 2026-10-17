/* The live preview's widgets, for the link and panel dialogs. A preview renders with the results already read for widgets
   of the same settings; the rest are placeholders (.widget-pending, marked data-widget-preview with their position),
   which readPreviewWidgets reads one by one through the Editor's WidgetPreview handler, so a slow service never holds up
   the preview. widgets.js leaves them alone. */

// A handler URL with query parameters added.
const withQuery = (url, params) => {
    const target = new URL(url, window.location.origin);
    for (const [name, value] of Object.entries(params)) {
        target.searchParams.set(name, value ?? '');
    }
    return target.pathname + target.search;
};

/**
 * Reads a preview's widgets that haven't been read, swapping each in as it arrives.
 * @param {HTMLElement} preview The preview.
 * @param {{ url: string, widgets: string, placement: string, isCurrent: () => boolean }} request The WidgetPreview
 *   handler, the widgets the preview was rendered from, where they go, and whether the preview is still the latest.
 */
export async function readPreviewWidgets(preview, { url, widgets, placement, isCurrent }) {
    await Promise.all([...preview.querySelectorAll('.widget-pending[data-widget-preview]')].map(async element => {
        try {
            const template = document.createElement('template');
            template.innerHTML = (await window.h2sApi.getText(withQuery(url, {
                widgets,
                index: element.dataset.widgetPreview,
                placement
            }))).trim();
            const fresh = template.content.firstElementChild;
            if (fresh && isCurrent() && element.isConnected) {
                element.replaceWith(fresh);
            }
        } catch {
            // Leave the placeholder; the next change to the form tries again.
        }
    }));
}

/**
 * Says what the widget being edited reads, as the preview shows it.
 * @param {HTMLElement} preview The preview.
 * @param {number | null} draftIndex The widget's position among the previewed widgets, or null when its fields aren't
 *   valid yet.
 * @returns {string} The widget's description, or why there isn't one yet.
 */
export function describeDraft(preview, draftIndex) {
    if (draftIndex === null) {
        return 'Fill in the fields to preview the widget.';
    }
    const element = preview.querySelector(`[data-widget-preview="${draftIndex}"]`);
    return element === null || element.classList.contains('widget-pending') ? 'Reading…' : element.title;
}
