// Top-level objects are declared with 'var' (not const/let) so they are properties of window, which is where
// Blazor's JS interop looks them up (ex. "JsFunctions.reload")

// Running Timers by ID
var stored_timers = {};


function Timer(timerId, timerElement, options) {

    let offset,
        clock,
        interval;

    // default options (50ms still shows the milliseconds changing, without rendering faster than the screen refreshes)
    options = options || {};
    options.delay = options.delay || 50;

    // initialize
    reset();

    function start() {
        if (!interval) {
            offset = Date.now();
            interval = setInterval(update, options.delay);
        }
    }

    function stop() {
        if (interval) {
            clearInterval(interval);
            interval = null;
        }
    }

    function reset() {
        clock = 0;
        render();
    }

    function update() {
        clock += delta();
        render();
    }

    function render() {
        timerElement.textContent = Number(clock / 1000).toFixed(3) + 's';
    }

    function delta() {
        const now = Date.now(),
            d = now - offset;

        offset = now;
        return d;
    }

    // public API
    this.start = start;
    this.stop = stop;
    this.reset = reset;

    stored_timers[timerId] = this;

};


// Tracks how the user is using the page (when they last interacted with it, whether they are editing, and whether
// input has been typed) and reports it to the Instance, so an admin can tell whether it is safe to reload the page
var TrakHoundActivity = (function () {
    let lastActivity = Date.now();
    let edited = false;
    let unsaved = null; // Declared by the App (null when the App doesn't declare it)
    let reporter = null;
    let reportTimer = null;
    let reportIntervalMs = 10000;
    let lastReport = null;

    ['mousemove', 'mousedown', 'keydown', 'wheel', 'scroll', 'touchstart', 'pointerdown'].forEach(function (name) {
        window.addEventListener(name, function () { lastActivity = Date.now(); }, { capture: true, passive: true });
    });

    ['input', 'change'].forEach(function (name) {
        document.addEventListener(name, function (e) {
            if (e.isTrusted && !edited) {
                edited = true;
                report(false);
            }
        }, true);
    });

    // Focus moving into or out of a text field (the Active Element is updated after the event)
    ['focusin', 'focusout'].forEach(function (name) {
        document.addEventListener(name, function () { setTimeout(function () { report(false); }, 0); }, true);
    });

    document.addEventListener('visibilitychange', function () { report(true); });

    function isEditing() {
        const element = document.activeElement;
        if (!element || element === document.body) return false;
        if (element.isContentEditable) return true;

        const tag = element.tagName;
        if (tag === 'TEXTAREA' || tag === 'SELECT') return true;
        if (tag === 'INPUT') {
            const type = (element.type || '').toLowerCase();
            return ['button', 'submit', 'reset', 'checkbox', 'radio', 'range', 'color', 'file', 'image'].indexOf(type) < 0;
        }

        return false;
    }

    function hasUnsavedChanges() {
        return unsaved !== null ? unsaved : edited;
    }

    function getIdleMs() {
        return Date.now() - lastActivity;
    }

    // Report when the state changes, or (at most every reportIntervalMs) when the user has interacted since the last Report.
    // A page no one is using doesn't report (the Instance knows when the last interaction was)
    function report(force) {
        if (!reporter) return;

        const now = Date.now();
        const editing = isEditing();
        const hidden = document.visibilityState === 'hidden';
        const key = [editing, edited, hidden].join('|');

        const changed = !lastReport || key !== lastReport.key;
        const interacted = lastReport && lastActivity > lastReport.activity && now - lastReport.time >= reportIntervalMs;
        if (!force && !changed && !interacted) return;

        lastReport = { time: now, activity: lastActivity, key: key };

        reporter.invokeMethodAsync('ReportActivity', Math.max(0, (now - lastActivity) / 1000), editing, edited, hidden)
            .catch(function () { }); // The connection may have closed
    }

    function start(dotNetReference, intervalSeconds) {
        stop();
        reporter = dotNetReference;
        if (intervalSeconds) reportIntervalMs = intervalSeconds * 1000;
        lastReport = null;
        report(true);
        reportTimer = setInterval(function () { report(false); }, 5000);
    }

    function stop() {
        if (reportTimer) {
            clearInterval(reportTimer);
            reportTimer = null;
        }
        reporter = null;
    }

    // A new Page (input typed on the previous Page no longer applies)
    function navigated() {
        edited = false;
        unsaved = null;
        report(true);
    }

    function setUnsaved(value) {
        unsaved = value === null || value === undefined ? null : !!value;
    }

    return {
        start: start,
        stop: stop,
        navigated: navigated,
        setUnsaved: setUnsaved,
        isEditing: isEditing,
        hasUnsavedChanges: hasUnsavedChanges,
        getIdleMs: getIdleMs
    };
})();


// Reload the page once the user is idle (ex. when a newer version of the App has been loaded).
// Never reloads while an input is focused or while the page has unsaved changes (so form data isn't lost)
var TrakHoundIdleReload = (function () {
    let timer = null;

    function cancel() {
        if (timer) {
            clearInterval(timer);
            timer = null;
        }
    }

    function schedule(idleSeconds, jitterSeconds) {
        cancel();

        // Random extra delay so every open page doesn't reload at the same time
        const idleMs = (idleSeconds || 120) * 1000 + Math.random() * (jitterSeconds || 0) * 1000;

        timer = setInterval(function () {
            if (TrakHoundActivity.hasUnsavedChanges() || TrakHoundActivity.isEditing()) return;
            if (TrakHoundActivity.getIdleMs() >= idleMs) {
                cancel();
                location.reload();
            }
        }, 5000);
    }

    return { schedule: schedule, cancel: cancel };
})();


// Returns null (instead of throwing) when the element isn't rendered, so a call made before (or after) the element
// exists doesn't fail in Blazor
function getElementById(id) {
    return id ? document.getElementById(id) : null;
}

// Copies using a hidden textarea (for pages that aren't a secure context, or when the Clipboard API is refused)
function copyClipboardFallback(text) {
    const textarea = document.createElement('textarea');
    textarea.value = text;

    // Fixed and transparent so the page doesn't scroll, and readonly so a mobile keyboard doesn't open
    textarea.setAttribute('readonly', '');
    textarea.style.position = 'fixed';
    textarea.style.top = '0';
    textarea.style.left = '0';
    textarea.style.opacity = '0';

    document.body.appendChild(textarea);
    textarea.select();

    try {
        document.execCommand('copy');
    } catch (err) {
        console.log(err);
    } finally {
        textarea.remove();
    }
}


// Lets a handle on a sidebar's edge change the sidebar's width. The width is set as a CSS variable on the container
// (its layout uses it), so the drag doesn't wait on Blazor. It's kept in Local Storage, so the sidebar stays as it was left.
// 'side' is the side of the container the sidebar is on. Double-click (or Home) returns it to its default size.
function attachResizeHandle(handle, container, sidebar, options) {
    if (!handle || !container || !sidebar || handle.dataset.resizeAttached) return;
    handle.dataset.resizeAttached = 'true';

    const variable = options.variable;
    const storageKey = options.storageKey;
    const min = options.min || 200;
    const minRemaining = options.minRemaining || 300; // What's left for the rest of the container
    const step = options.step || 20;
    const sign = options.side === 'right' ? -1 : 1; // A sidebar on the right grows when its handle moves left

    function clamp(width) {
        const max = Math.max(min, container.getBoundingClientRect().width - minRemaining);
        return Math.round(Math.min(Math.max(width, min), max));
    }

    function apply(width) {
        container.style.setProperty(variable, width + 'px');
    }

    function save(width) {
        try {
            if (width == null) localStorage.removeItem(storageKey);
            else localStorage.setItem(storageKey, String(width));
        } catch (err) { }
    }

    function reset() {
        container.style.removeProperty(variable);
        save(null);
    }

    try {
        const stored = parseFloat(localStorage.getItem(storageKey));
        if (stored > 0) apply(stored); // Clamped when the layout uses it (the container may not be its full size yet)
    } catch (err) { }

    let startX = 0;
    let startWidth = 0;
    let width = null;

    handle.addEventListener('pointerdown', function (e) {
        if (e.button !== 0) return;
        e.preventDefault();

        startX = e.clientX;
        startWidth = sidebar.getBoundingClientRect().width;
        width = null;

        handle.setPointerCapture(e.pointerId);
        document.body.classList.add('resizing-col');
    });

    handle.addEventListener('pointermove', function (e) {
        if (!handle.hasPointerCapture(e.pointerId)) return;

        width = clamp(startWidth + (e.clientX - startX) * sign);
        apply(width);
    });

    function end(e) {
        if (!handle.hasPointerCapture(e.pointerId)) return;

        handle.releasePointerCapture(e.pointerId);
        document.body.classList.remove('resizing-col');
        if (width != null) save(width);
    }

    handle.addEventListener('pointerup', end);
    handle.addEventListener('pointercancel', end);
    handle.addEventListener('dblclick', reset);

    handle.addEventListener('keydown', function (e) {
        let change = 0;
        if (e.key === 'ArrowLeft') change = -step * sign;
        else if (e.key === 'ArrowRight') change = step * sign;
        else if (e.key === 'Home') { e.preventDefault(); reset(); return; }
        else return;

        e.preventDefault();
        width = clamp(sidebar.getBoundingClientRect().width + change);
        apply(width);
        save(width);
    });
}


// Stops the keys a TreeView handles (in Blazor) from also scrolling the page or following a link. Blazor can only
// prevent a key's default for every key, which would also stop Tab from leaving the tree.
function attachTreeView(tree) {
    if (!tree || tree.dataset.treeAttached) return;
    tree.dataset.treeAttached = 'true';

    const keys = ['ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Home', 'End', 'Enter', ' '];

    tree.addEventListener('keydown', function (e) {
        if (keys.indexOf(e.key) < 0 || e.altKey || e.ctrlKey || e.metaKey) return;

        // Keys typed in a field inside an item (ex. in a HeaderTemplate) are left alone
        const target = e.target;
        if (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON'].indexOf(target.tagName) >= 0) return;
        if (!target.closest('[role="treeitem"]')) return;

        e.preventDefault();
    });
}


var JsFunctions = Object.assign(window.JsFunctions || {}, {
    reload: function () {
        location.reload();
    },
    scheduleIdleReload: function (idleSeconds, jitterSeconds) {
        TrakHoundIdleReload.schedule(idleSeconds, jitterSeconds);
    },
    cancelIdleReload: function () {
        TrakHoundIdleReload.cancel();
    },
    focusElement: function (id) {
        const element = getElementById(id);
        if (element) element.focus();
    },
    setStylesheet: function (id, href) {
        // Changes the link in place (a statically rendered link isn't managed by Blazor, so it isn't replaced by a render)
        const element = getElementById(id);
        if (element && element.getAttribute('href') !== href) element.setAttribute('href', href);
    },
    focusSelectElement: function (id) {
        const element = getElementById(id);
        if (element) {
            element.focus();
            if (element.select) element.select();
        }
    },
    selectElement: function (id) {
        const element = getElementById(id);
        if (element && element.select) element.select();
    },
    clickElement: function (id) {
        const element = getElementById(id);
        if (element) element.click();
    },
    scrollToElement: function (id) {
        const element = getElementById(id);
        if (element) element.scrollIntoView({ block: "start" });
    },
    getElementWidth: function (id) {
        const element = getElementById(id);
        return element ? element.getBoundingClientRect().width : 0;
    },
    scrollIntoView: function (id) {
        const element = getElementById(id);
        if (element) element.scrollIntoView();
    },
    isScrolledToBottom: function (element, threshold) {
        // True when the scroll container is at (or within 'threshold' px of) the bottom, or can't scroll
        if (!element) return true;
        return element.scrollHeight - element.scrollTop - element.clientHeight <= (threshold || 0);
    },
    scrollToBottom: function (element) {
        // Scrolls only this container (unlike scrollIntoView, which also scrolls every ancestor)
        if (element) element.scrollTop = element.scrollHeight;
    },
    startTimer: function (timerId, timerElementId) {
        // Stop a Timer already running with this ID (otherwise it keeps running with nothing able to stop it)
        JsFunctions.stopTimer(timerId);

        const timerElement = getElementById(timerElementId);
        if (!timerElement) return;

        new Timer(timerId, timerElement).start();
    },
    stopTimer: function (timerId) {
        const timer = stored_timers[timerId];
        if (timer) {
            timer.stop();
            delete stored_timers[timerId];
        }
    },
    attachResizeHandle: function (handle, container, sidebar, options) {
        attachResizeHandle(handle, container, sidebar, options || {});
    },
    attachTreeView: function (tree) {
        attachTreeView(tree);
    },
    copyClipboard: function (text) {
        // Returns the Promise so the caller's await completes once the text is copied
        if (navigator.clipboard && window.isSecureContext) {
            return navigator.clipboard.writeText(text).catch(function () { copyClipboardFallback(text); });
        }

        copyClipboardFallback(text);
    }
});
