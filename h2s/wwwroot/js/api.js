/* Shared helpers for calling the app's JSON page handlers: antiforgery tokens, JSON parsing and ProblemDetails errors. */
window.h2sApi = (() => {
    // Every antiforgery field on a page carries the same request token, and the layout always renders one.
    const getToken = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    // Builds an Error for a failed response, preferring the ProblemDetails detail (or title) the handlers return.
    const toError = async (response, fallbackMessage) => {
        let message = fallbackMessage;
        let problem = null;

        if ((response.headers.get('Content-Type') ?? '').includes('json')) {
            try {
                problem = await response.json();
                message = problem.detail || problem.title || fallbackMessage;
            } catch {
                // Not a readable body; keep the fallback message.
            }
        }

        const error = new Error(message);
        error.status = response.status;
        error.problem = problem;
        return error;
    };

    // Posts form data to a page handler and returns its JSON. The data is an object or a list of name/value pairs, sent
    // form-encoded; or a FormData, such as one holding a file, sent as multipart.
    const postForm = async (url, data = {}) => {
        const token = getToken();
        const isMultipart = data instanceof FormData;
        const body = isMultipart ? data : new URLSearchParams(data);
        body.set('__RequestVerificationToken', token);

        // The browser sets a multipart body's Content-Type itself, with its boundary.
        const headers = { 'RequestVerificationToken': token };
        if (!isMultipart) {
            headers['Content-Type'] = 'application/x-www-form-urlencoded; charset=UTF-8';
        }

        const response = await fetch(url, { method: 'POST', headers, body });

        if (!response.ok) {
            throw await toError(response, 'Request failed.');
        }

        return response.json();
    };

    // Fetches JSON from a page handler without allowing cached responses.
    const getJson = async (url) => {
        const response = await fetch(url, { cache: 'no-store' });

        if (!response.ok) {
            throw await toError(response, 'Unable to load data.');
        }

        return response.json();
    };

    // Fetches text, such as a server-rendered partial, from a page handler without allowing cached responses.
    const getText = async (url) => {
        const response = await fetch(url, { cache: 'no-store' });

        if (!response.ok) {
            throw await toError(response, 'Unable to load data.');
        }

        return response.text();
    };

    return { getJson, getText, postForm };
})();
