/* The search panel's engine menu (Pages/Shared/_SearchPanel.cshtml): choosing an engine sends the next search, and only
   the next one, to it. The choice posts as the form's engine field; /Search still lets a !shortcut in the query win. */

for (const form of document.querySelectorAll('[data-role="search-panel"]')) {
    initSearchPanel(form);
}

function initSearchPanel(form) {
    const choice = form.querySelector('[data-role="engine-choice"]');
    const input = form.elements.q;
    const items = [...form.querySelectorAll('.home-search-engine-item')];
    const defaultItem = items.find(item => item.dataset.default === 'true');
    const defaultLabel = input.getAttribute('aria-label');

    // Choosing the default (or nothing) clears the choice, so the form posts no engine at all.
    const choose = item => {
        const chosen = item && item !== defaultItem ? item : null;

        choice.disabled = !chosen;
        choice.value = chosen?.dataset.engineId ?? '';
        for (const other of items) {
            other.setAttribute('aria-current', String(other === (chosen ?? defaultItem)));
        }

        const label = chosen ? `Search with ${chosen.dataset.engineName}` : null;
        input.placeholder = label ?? form.dataset.defaultPlaceholder;
        input.setAttribute('aria-label', label ?? defaultLabel);
        form.classList.toggle('has-engine-choice', Boolean(chosen));
    };

    for (const item of items) {
        item.addEventListener('click', () => {
            choose(item);
            input.focus();
        });
    }

    // Next search only: the form has read the choice by the time this runs, and a page restored from the back/forward
    // cache starts over too.
    form.addEventListener('submit', () => setTimeout(() => choose(null)));
    window.addEventListener('pageshow', event => {
        if (event.persisted) {
            choose(null);
        }
    });
}
