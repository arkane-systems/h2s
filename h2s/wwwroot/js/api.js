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

    // Posts form-encoded data (an object or a list of name/value pairs) to a page handler and returns its JSON.
    const postForm = async (url, data = {}) => {
        const token = getToken();
        const body = new URLSearchParams(data);
        body.set('__RequestVerificationToken', token);

        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                'RequestVerificationToken': token
            },
            body
        });

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

    return { getJson, postForm };
})();
