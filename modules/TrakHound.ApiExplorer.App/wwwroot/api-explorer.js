// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

// Requests are sent from the browser (not the server) so that they are made with the developer's own
// Session Cookie or API Key, the same as their application would be
window.trakhoundApiExplorer = (function () {

    const storageKey = 'trakhound.apiExplorer.apiKey';
    const textTypes = ['json', 'text', 'xml', 'javascript', 'yaml', 'csv', 'html', 'x-www-form-urlencoded'];

    let lastResponse = null;

    function isText(contentType) {
        if (!contentType) return true;
        contentType = contentType.toLowerCase();
        return textTypes.some(t => contentType.indexOf(t) >= 0);
    }

    function getStorage() {
        try { return window.sessionStorage; } catch { return null; }
    }

    function saveBlob(blob, filename) {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename || 'response';
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    return {

        // The API Key is kept for the browser tab only (never sent to the server except as a Request Header)
        getApiKey: function () {
            try { return getStorage()?.getItem(storageKey) || null; } catch { return null; }
        },

        setApiKey: function (value) {
            try {
                const storage = getStorage();
                if (!storage) return;
                if (value) storage.setItem(storageKey, value);
                else storage.removeItem(storageKey);
            } catch { }
        },

        // Sends a Request and returns the Response (text bodies are returned, binary bodies can be saved with saveResponse)
        send: async function (url, method, headers, body) {
            const started = performance.now();
            try {
                const init = { method: method || 'GET', headers: headers || {}, credentials: 'same-origin', cache: 'no-store' };
                if (body !== null && body !== undefined && init.method !== 'GET' && init.method !== 'HEAD') init.body = body;

                const response = await fetch(url, init);
                const blob = await response.blob();
                const duration = Math.round(performance.now() - started);
                const contentType = response.headers.get('content-type') || '';

                lastResponse = { blob: blob, contentType: contentType };

                const responseHeaders = [];
                response.headers.forEach((value, name) => responseHeaders.push({ name: name, value: value }));

                const text = isText(contentType) && blob.size <= 2 * 1024 * 1024 ? await blob.text() : null;

                return {
                    success: true,
                    status: response.status,
                    statusText: response.statusText,
                    contentType: contentType,
                    headers: responseHeaders,
                    body: text,
                    isText: text !== null,
                    size: blob.size,
                    durationMs: duration
                };
            }
            catch (e) {
                lastResponse = null;
                return {
                    success: false,
                    error: e && e.message ? e.message : String(e),
                    durationMs: Math.round(performance.now() - started)
                };
            }
        },

        // Saves the body of the last Response sent with send()
        saveResponse: function (filename) {
            if (lastResponse) saveBlob(lastResponse.blob, filename);
        },

        saveText: function (text, filename, contentType) {
            saveBlob(new Blob([text], { type: contentType || 'text/plain' }), filename);
        },

        copy: async function (text) {
            try {
                await navigator.clipboard.writeText(text);
                return true;
            }
            catch {
                return false;
            }
        }
    };
})();
