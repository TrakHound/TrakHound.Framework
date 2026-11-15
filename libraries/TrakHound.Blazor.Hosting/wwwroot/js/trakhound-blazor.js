(() => {
    // Reconnection Panel : #reconnection-panel and #reconnection-panel-label are required. Optional elements :
    // #reconnection-panel-description (state), #reconnection-panel-since (time disconnected), #reconnection-panel-host (server address),
    // #reconnection-panel-retry and #reconnection-panel-reload (buttons)
    const showDelayMilliseconds = 2000; // Brief drops reconnect without showing the Panel
    const retryDelaysMilliseconds = [0, 1000, 2000, 5000, 10000]; // Then repeats the last (backoff)
    const maximumRetryDelayMilliseconds = 15000;
    const maximumRetryCount = 10000;
    const probeIntervalMilliseconds = 2000;
    const probePath = '_health/live'; // Any response (other than a 5xx from a proxy) means the server is reachable

    const reconnectionPanel = document.getElementById('reconnection-panel');
    const reconnectionLabel = document.getElementById('reconnection-panel-label');
    const reconnectionDescription = document.getElementById('reconnection-panel-description');
    const reconnectionSince = document.getElementById('reconnection-panel-since');
    const retryButton = document.getElementById('reconnection-panel-retry');
    const reloadButton = document.getElementById('reconnection-panel-reload');

    const setText = (element, text) => {
        if (element && element.innerText !== text) element.innerText = text;
    };

    const formatDuration = (milliseconds) => {
        const seconds = Math.floor(milliseconds / 1000);
        if (seconds < 60) return `${seconds}s`;
        const minutes = Math.floor(seconds / 60);
        if (minutes < 60) return `${minutes}m ${seconds % 60}s`;
        return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
    };

    const getRetryDelay = (attempt) => {
        const delay = retryDelaysMilliseconds[Math.min(attempt, retryDelaysMilliseconds.length - 1)];
        return attempt < retryDelaysMilliseconds.length ? delay : maximumRetryDelayMilliseconds;
    };

    // Keep keyboard focus and screen readers out of the page behind the Panel
    const setPageInert = (inert) => {
        if (!reconnectionPanel) return;
        for (const element of document.body.children) {
            if (element === reconnectionPanel || element.tagName === 'SCRIPT') continue;
            if (inert) element.setAttribute('inert', '');
            else element.removeAttribute('inert');
        }
    };

    const startReconnectionProcess = () => {
        const disconnected = Date.now();
        let isCanceled = false;
        let isShown = false;
        let attempt = 0;
        let isAttempting = false;
        let nextAttempt = disconnected + getRetryDelay(0);
        let serverReachable = null;
        let probing = false;

        const show = () => {
            if (isShown || isCanceled || !reconnectionPanel) return;
            isShown = true;
            reconnectionPanel.style.display = 'block';
            setPageInert(true);
            update();
        };

        const update = () => {
            if (!isShown) return;

            const now = Date.now();
            const since = new Date(disconnected).toLocaleTimeString();
            setText(reconnectionSince, `Disconnected at ${since} (${formatDuration(now - disconnected)} ago)`);

            // For styling (ex. a spinner while reconnecting)
            const state = isAttempting ? 'reconnecting' : !navigator.onLine ? 'offline' : serverReachable === false ? 'unreachable' : 'waiting';
            if (reconnectionPanel.dataset.state !== state) reconnectionPanel.dataset.state = state;

            if (!navigator.onLine) setText(reconnectionDescription, 'This device is offline. Waiting for the network..');
            else if (serverReachable === false) setText(reconnectionDescription, 'The server is not reachable (it may be restarting)');
            else setText(reconnectionDescription, 'Lost connection to the server');

            if (isAttempting) {
                setText(reconnectionLabel, `Reconnecting.. (attempt ${attempt})`);
            } else {
                const seconds = Math.max(0, Math.ceil((nextAttempt - now) / 1000));
                setText(reconnectionLabel, attempt > 0 ? `Retrying in ${seconds}s (attempt ${attempt + 1})` : `Retrying in ${seconds}s`);
            }
        };

        const tryReconnect = async () => {
            if (isCanceled || isAttempting) return;

            if (attempt >= maximumRetryCount) {
                // Retried too many times; reload the page.
                location.reload();
                return;
            }

            isAttempting = true;
            attempt++;
            update();

            try {
                const result = await Blazor.reconnect();
                if (isCanceled) return;

                if (!result) {
                    // The server was reached, but the connection was rejected (ex. the server restarted); reload the page.
                    location.reload();
                    return;
                }

                // Successfully reconnected to the server (onConnectionUp cancels this process).
                return;
            } catch {
                // Didn't reach the server; try again.
            } finally {
                isAttempting = false;
            }

            if (isCanceled) return;
            nextAttempt = Date.now() + getRetryDelay(attempt);
            update();
        };

        // Retry now (ex. the network came back, the tab is visible again, or the server is reachable)
        const retryNow = () => {
            if (isCanceled || isAttempting) return;
            nextAttempt = Date.now();
            tryReconnect();
        };

        // Check if the server is reachable without waiting for the next (backed off) attempt
        const probe = async () => {
            if (isCanceled || isAttempting || probing || !navigator.onLine) return;

            probing = true;
            try {
                const response = await fetch(new URL(probePath, document.baseURI), { cache: 'no-store' });
                const reachable = response.status < 500;
                const cameBack = reachable && serverReachable === false;
                serverReachable = reachable;
                if (cameBack) retryNow();
            } catch {
                serverReachable = false;
            } finally {
                probing = false;
            }
        };

        const onVisibilityChange = () => {
            if (document.visibilityState === 'visible') retryNow();
        };

        const tick = setInterval(() => {
            if (!isAttempting && Date.now() >= nextAttempt) tryReconnect();
            update();
        }, 250);
        const probeTimer = setInterval(probe, probeIntervalMilliseconds);
        const showTimer = setTimeout(show, showDelayMilliseconds);

        window.addEventListener('online', retryNow);
        document.addEventListener('visibilitychange', onVisibilityChange);
        retryButton?.addEventListener('click', retryNow);

        return {
            cancel: () => {
                isCanceled = true;
                clearInterval(tick);
                clearInterval(probeTimer);
                clearTimeout(showTimer);
                window.removeEventListener('online', retryNow);
                document.removeEventListener('visibilitychange', onVisibilityChange);
                retryButton?.removeEventListener('click', retryNow);

                if (reconnectionPanel) reconnectionPanel.style.display = 'none';
                setPageInert(false);
            },
        };
    };

    reloadButton?.addEventListener('click', () => location.reload());
    setText(document.getElementById('reconnection-panel-host'), location.host);

    let currentReconnectionProcess = null;

    Blazor.start({
        circuit: {
            configureSignalR: function (builder) {
                let c = builder.build();
                c.serverTimeoutInMilliseconds = 30000;
                c.keepAliveIntervalInMilliseconds = 5000;
                builder.build = () => {
                    return c;
                };
            },
            reconnectionHandler: {
                onConnectionDown: () => currentReconnectionProcess ??= startReconnectionProcess(),
                onConnectionUp: () => {
                    currentReconnectionProcess?.cancel();
                    currentReconnectionProcess = null;
                }
            }
        }
    });
})();
