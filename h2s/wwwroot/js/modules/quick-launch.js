/* Quick launch (Pages/Shared/_QuickLaunch.cshtml): typing anywhere on the dashboard, or /, opens a floating box that
   filters the tiles as you type. Enter opens the highlighted match (Ctrl or ⌘ for a new tab), ↑ and ↓ move between
   matches, and Esc closes the box and puts the dashboard back. With no matches, or a !shortcut in the query, it offers the
   search engines in Settings order instead, and Enter searches with the highlighted one through /Search, which still
   lets a !shortcut win. Shift+Enter searches the web even when links match. */

const root = document.getElementById('quick-launch');

if (root) {
    initQuickLaunch(root);
}

function initQuickLaunch(root) {
    const input = root.querySelector('[data-role="quick-launch-input"]');
    const hint = root.querySelector('[data-role="quick-launch-hint"]');
    const engineList = root.querySelector('[data-role="quick-launch-engines"]');
    const engines = [...root.querySelectorAll('.quick-launch-engine')];
    const defaultEngine = engines.find(engine => engine.dataset.default === 'true') ?? engines[0] ?? null;

    // The dashboard's tiles, with the text they're matched on, in page order.
    const tiles = [...document.querySelectorAll('.category-grid .link-block')].map((element, order) => ({
        element,
        order,
        label: (element.querySelector('.link-block-title')?.textContent ?? '').trim(),
        description: (element.querySelector('.link-block-description')?.textContent ?? '').trim().toLowerCase(),
        host: hostOf(element.href)
    }));
    const categories = [...document.querySelectorAll('.category-grid > .category-box')];
    const hideWhileFiltering = [...document.querySelectorAll('.panel-zone, .category-grid-break')];

    let matches = [];
    let selected = 0;
    let engineIndex = 0;

    const isOpen = () => !root.hidden;

    const open = text => {
        root.hidden = false;
        document.body.classList.add('is-quick-launching');
        input.value = text;
        input.focus();
        update();
    };

    const close = () => {
        root.hidden = true;
        document.body.classList.remove('is-quick-launching');
        input.value = '';
        update();
    };

    // The engine a !shortcut word at the start or end of the query names, as /Search reads it.
    const shortcutEngine = query => {
        const words = query.split(/\s+/).filter(Boolean);
        const candidates = words.length === 1 ? [words[0]] : [words[0], words.at(-1)];
        for (const word of candidates) {
            const engine = word?.startsWith('!')
                ? engines.find(e => e.dataset.shortcut && e.dataset.shortcut === word.slice(1).toLowerCase())
                : null;
            if (engine) {
                return engine;
            }
        }
        return null;
    };

    const update = () => {
        const query = input.value.trim();
        const shortcut = shortcutEngine(query);

        // A shortcut means a web search, so the dashboard stays as it is.
        matches = query && !shortcut ? findMatches(tiles, query) : [];
        selected = 0;

        const filtering = Boolean(query) && !shortcut;
        const visible = new Set(matches.map(match => match.element));
        for (const tile of tiles) {
            tile.element.classList.toggle('quick-launch-hidden', filtering && !visible.has(tile.element));
        }
        // A category with matches shows them, even if this browser has it collapsed.
        for (const category of categories) {
            const hasMatches = Boolean(category.querySelector('.link-block:not(.quick-launch-hidden)'));
            category.classList.toggle('quick-launch-hidden', filtering && !hasMatches);
            category.classList.toggle('quick-launch-expanded', filtering && hasMatches);
        }
        for (const element of hideWhileFiltering) {
            element.classList.toggle('quick-launch-hidden', filtering);
        }

        // Engines show when there's a query but nothing to open: no matches, or a shortcut.
        const offerEngines = Boolean(query) && matches.length === 0 && engines.length > 0;
        if (engineList) {
            engineList.hidden = !offerEngines;
        }
        engineIndex = Math.max(0, engines.indexOf(shortcut ?? defaultEngine));

        highlight();
    };

    const highlight = () => {
        tiles.forEach(tile => tile.element.classList.remove('is-quick-launch-target'));
        const target = matches[selected]?.element;
        if (target) {
            target.classList.add('is-quick-launch-target');
            target.scrollIntoView({ block: 'nearest' });
        }

        const offering = engineList && !engineList.hidden;
        engines.forEach((engine, index) => engine.setAttribute('aria-selected', String(offering && index === engineIndex)));

        hint.textContent = describe(offering);
    };

    const describe = offering => {
        const query = input.value.trim();
        if (!query) {
            return 'Type to find a link. Esc closes.';
        }
        if (matches.length > 0) {
            const label = matches[selected].label;
            const more = matches.length > 1 ? ` · ↑↓ ${matches.length} matches` : '';
            return `Enter opens ${label}${more}${defaultEngine ? ' · Shift+Enter searches the web' : ''}`;
        }
        if (offering) {
            const name = engines[engineIndex].dataset.engineName;
            return shortcutEngine(query)
                ? `Enter searches with ${name}.`
                : `No links match. Enter searches with ${name}${engines.length > 1 ? '; ↑↓ to pick another' : ''}.`;
        }
        return 'No links match.';
    };

    const search = engine => {
        const query = input.value.trim();
        if (!query || !engine) {
            return;
        }
        const url = new URL(root.dataset.searchUrl, window.location.origin);
        url.searchParams.set('q', query);
        if (engine !== defaultEngine) {
            url.searchParams.set('engine', engine.dataset.engineId);
        }
        window.location.assign(url);
    };

    input.addEventListener('input', update);

    input.addEventListener('keydown', event => {
        const offering = engineList && !engineList.hidden;

        if (event.key === 'Escape') {
            event.preventDefault();
            close();
        } else if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            const step = event.key === 'ArrowDown' ? 1 : -1;
            if (matches.length > 0) {
                event.preventDefault();
                selected = (selected + step + matches.length) % matches.length;
                highlight();
            } else if (offering) {
                event.preventDefault();
                engineIndex = (engineIndex + step + engines.length) % engines.length;
                highlight();
            }
        } else if (event.key === 'Enter') {
            event.preventDefault();
            if (event.shiftKey || matches.length === 0) {
                search(offering ? engines[engineIndex] : defaultEngine);
            } else if (event.ctrlKey || event.metaKey) {
                window.open(matches[selected].element.href, '_blank', 'noopener');
            } else {
                matches[selected].element.click();
            }
        }
    });

    for (const engine of engines) {
        engine.addEventListener('click', () => search(engine));
    }

    // Typing anywhere that isn't a field opens it, with what was typed; / opens it empty.
    document.addEventListener('keydown', event => {
        if (isOpen() || event.defaultPrevented || event.isComposing || event.ctrlKey || event.metaKey || event.altKey ||
            isEditable(event.target) || document.querySelector('.modal.show, .dropdown-menu.show')) {
            return;
        }
        if (event.key === '/') {
            event.preventDefault();
            open('');
        } else if (event.key.length === 1 && event.key.trim()) {
            event.preventDefault();
            open(event.key);
        }
    });

    // A click outside the box closes it, but not one on a tile, which opens that link as usual.
    document.addEventListener('pointerdown', event => {
        if (isOpen() && !root.contains(event.target) && !event.target.closest('.link-block')) {
            close();
        }
    });

    // Coming back to the dashboard from a link restores it from the back/forward cache, so start over.
    window.addEventListener('pageshow', event => {
        if (event.persisted && isOpen()) {
            close();
        }
    });
}

// Ranks the tiles a query matches: every word must appear in the label, description or host. Labels that start with the
// query come first, then labels with a word that starts with it, then other label matches, then the rest; ties keep page
// order.
function findMatches(tiles, query) {
    const lowered = query.toLowerCase();
    const words = lowered.split(/\s+/).filter(Boolean);

    return tiles
        .map(tile => {
            const label = tile.label.toLowerCase();
            const haystack = `${label} ${tile.description} ${tile.host}`;
            if (!words.every(word => haystack.includes(word))) {
                return null;
            }
            const rank = label.startsWith(lowered) ? 0
                : label.split(/[\s\-_.]+/).some(part => part.startsWith(words[0])) ? 1
                : label.includes(lowered) ? 2
                : 3;
            return { ...tile, rank };
        })
        .filter(Boolean)
        .sort((a, b) => a.rank - b.rank || a.order - b.order);
}

function hostOf(href) {
    try {
        return new URL(href).hostname.toLowerCase();
    } catch {
        return '';
    }
}

function isEditable(element) {
    return element instanceof HTMLElement &&
        (element.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(element.tagName));
}
