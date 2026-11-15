// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

// System API Requests are sent from the browser (not the server) so that they are made with the visitor's own
// Session Cookie, and the Instance only returns what the visitor is allowed to read
window.trakhoundWelcome = (function () {

    return {

        get: async function (url) {
            try {
                const response = await fetch(url, { method: 'GET', headers: { 'Accept': 'application/json' }, credentials: 'same-origin', cache: 'no-store' });
                const body = response.ok ? await response.text() : null;

                return {
                    success: true,
                    status: response.status,
                    statusText: response.statusText,
                    body: body
                };
            }
            catch (err) {
                return { success: false, error: (err && err.message) || String(err) };
            }
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
