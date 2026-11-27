// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

// Draws a MetricsChart (Pages/Explorer/Diagnostics) on a canvas, so a live chart moves all the time instead of only when a sample is read.
// The time shown ends a little more than one interval before now : the line is always drawn toward a sample that has already been read,
// and nothing is made up between samples. The server only sends the samples that were added.
(function () {
    if (window.thMetricsChart) return;

    var minSpan = 60000;   // The least time shown by a live chart (ms), so the first samples aren't stretched across the chart
    var jitter = 150;      // How late a sample can be read (ms) before the chart has to wait for it
    var timeSteps = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600, 43200, 86400, 172800, 604800];
    var motion = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;

    function pad(n) { return n < 10 ? '0' + n : '' + n; }

    function clamp(value, min, max) { return value < min ? min : value > max ? max : value; }

    // A time axis label in the Instance's Time Zone (the offset is sent by the server, so the labels match the rest of the page).
    // Seconds are shown only when the labels are less than a minute apart, and the date when they're a day or more apart
    function formatTime(state, time, step) {
        var d = new Date(time + state.offset * 60000);
        if (step >= 86400000) return pad(d.getUTCMonth() + 1) + '-' + pad(d.getUTCDate());

        var text = pad(d.getUTCHours()) + ':' + pad(d.getUTCMinutes());
        return step < 60000 ? text + ':' + pad(d.getUTCSeconds()) : text;
    }

    function create(el) {
        // The canvas is sized by the plot (and never the other way around, which would grow it each time it's drawn)
        var canvas = document.createElement('canvas');
        canvas.style.cssText = 'position: absolute; left: 0; top: 0; width: 100%; height: 100%;';
        var tip = document.createElement('div');
        tip.className = 'metrics-chart-tooltip';
        tip.style.display = 'none';
        el.appendChild(canvas);
        el.appendChild(tip);

        var state = { el: el, canvas: canvas, ctx: canvas.getContext('2d'), tip: tip, times: [], timeLabels: [], series: [], ticks: [], visible: true, hoverX: null, maxSpan: 0 };

        state.mousemove = function (e) {
            state.hoverX = e.clientX - canvas.getBoundingClientRect().left;
            state.dirty = true;
            start(state);
        };
        state.mouseleave = function () {
            state.hoverX = null;
            state.dirty = true;
            start(state);
        };
        el.addEventListener('mousemove', state.mousemove);
        el.addEventListener('mouseleave', state.mouseleave);

        if (window.ResizeObserver) {
            state.resize = new ResizeObserver(function () { state.dirty = true; start(state); });
            state.resize.observe(el);
        }

        // A chart that is scrolled out of view isn't drawn
        if (window.IntersectionObserver) {
            state.intersection = new IntersectionObserver(function (entries) {
                state.visible = entries[entries.length - 1].isIntersecting;
                if (state.visible) { state.dirty = true; start(state); }
            });
            state.intersection.observe(el);
        }

        el._thMetricsChart = state;
        return state;
    }

    function dispose(el) {
        var state = el && el._thMetricsChart;
        if (!state) return;

        if (state.raf) cancelAnimationFrame(state.raf);
        state.raf = 0;
        if (state.resize) state.resize.disconnect();
        if (state.intersection) state.intersection.disconnect();
        el.removeEventListener('mousemove', state.mousemove);
        el.removeEventListener('mouseleave', state.mouseleave);
        if (state.canvas.parentNode === el) el.removeChild(state.canvas);
        if (state.tip.parentNode === el) el.removeChild(state.tip);
        el._thMetricsChart = null;
    }

    function update(el, data) {
        if (!el) return;

        var state = el._thMetricsChart || create(el);
        var now = Date.now();
        var i, j;

        if (data.reset) {
            state.times = [];
            state.timeLabels = [];
            state.series = [];
            state.lag = null;
            state.interval = null;
            state.right = null;
            state.frameTime = null;
            state.maxSpan = 0;
            state.shownMax = null;
            state.drawn = null;
            state.tipKey = null;
        }

        for (i = 0; i < data.series.length; i++) {
            var source = data.series[i];
            var target = state.series[i] || (state.series[i] = { values: [], labels: [] });
            target.title = source.title;
            target.color = source.color;
            for (j = 0; j < source.values.length; j++) {
                target.values.push(source.values[j]);
                target.labels.push(source.labels[j]);
            }
        }
        for (i = 0; i < data.times.length; i++) {
            state.times.push(data.times[i]);
            state.timeLabels.push(data.timeLabels[i]);
        }

        state.live = data.live;
        state.area = data.area;
        state.max = data.max > 0 ? data.max : 1;
        state.ticks = data.ticks || [];
        state.offset = data.offset || 0;
        state.maxSpan = Math.max(state.maxSpan, data.span || 0);
        if (state.shownMax == null) state.shownMax = state.max;

        // How far the browser's clock is from the samples' (which includes the time it takes to read one), and how often they're read
        var n = state.times.length;
        if (n > 1 && data.times.length > 0) {
            var lag = now - state.times[n - 1];
            state.lag = state.lag == null || lag < state.lag ? lag : state.lag + (lag - state.lag) * 0.1;

            var gap = clamp(state.times[n - 1] - state.times[n - 2], 250, 10000);
            state.interval = state.interval == null ? gap : state.interval + (gap - state.interval) * 0.3;
        }
        if (state.lag == null) state.lag = 0;
        if (state.interval == null) state.interval = 1000;

        // Samples that have moved off the chart (one is kept before its left edge, which the line starts from)
        if (state.live && state.right != null) {
            var oldest = state.right - Math.max(state.maxSpan, minSpan) - 1000;
            var remove = 0;
            while (remove < state.times.length - 2 && state.times[remove + 1] < oldest) remove++;
            if (remove > 0) {
                state.times.splice(0, remove);
                state.timeLabels.splice(0, remove);
                for (i = 0; i < state.series.length; i++) {
                    state.series[i].values.splice(0, remove);
                    state.series[i].labels.splice(0, remove);
                }
            }
        }

        var style = getComputedStyle(el);
        state.textColor = style.getPropertyValue('--textMild').trim() || style.color;
        state.gridColor = style.getPropertyValue('--borderColorLight').trim() || 'rgba(128, 128, 128, 0.3)';
        state.panelColor = style.getPropertyValue('--panelBackground').trim() || '#fff';
        state.font = style.fontFamily || 'sans-serif';

        state.dirty = true;
        start(state);
    }

    // The time shown. A live chart's right edge follows the clock up to the latest sample, and catches up smoothly after a late one.
    // Any other chart shows all of its samples
    function getView(state, now) {
        var n = state.times.length;
        var last = state.times[n - 1];

        if (!state.live) return { left: state.times[0], right: last };

        var target = Math.min(now - state.lag - state.interval - jitter, last);
        var elapsed = state.frameTime ? now - state.frameTime : 0;
        if (state.right == null) state.right = target;
        else if (target > state.right) state.right = Math.min(target, state.right + Math.max(elapsed, (target - state.right) * 0.15));
        state.frameTime = now;

        var left = Math.max(state.times[0], state.right - state.maxSpan);
        left = Math.min(left, state.right - minSpan);

        return { left: left, right: state.right };
    }

    function start(state) {
        if (!state.raf) state.raf = requestAnimationFrame(function () { frame(state); });
    }

    function frame(state) {
        state.raf = 0;
        if (!state.el.isConnected) { dispose(state.el); return; }
        if (!state.visible || state.times.length < 2) return;

        var view = getView(state, Date.now());

        // The value axis is scaled to the samples, and moves to a new scale instead of jumping to it
        // (a live chart's scroll is the data arriving, so it stays when the browser asks for less motion : only this easing is left out)
        var scaling = !(motion && motion.matches) && Math.abs(state.max - state.shownMax) > state.max * 0.002;
        if (scaling) state.shownMax += (state.max - state.shownMax) * 0.2;
        else state.shownMax = state.max;

        // Nothing is drawn until the chart has moved far enough to see
        var moved = Infinity;
        if (state.drawn) moved = (Math.abs(view.right - state.drawn.right) + Math.abs(view.left - state.drawn.left)) / (view.right - view.left) * state.el.clientWidth;
        if (state.dirty || scaling || moved >= 0.2) {
            draw(state, view);
            state.drawn = view;
            state.dirty = false;
        }

        // Stops when the latest sample has been reached (ex. the page has stopped reading them), until another is added
        if (scaling || (state.live && view.right < state.times[state.times.length - 1])) start(state);
    }

    function draw(state, view) {
        var el = state.el, canvas = state.canvas, ctx = state.ctx;
        var w = el.clientWidth, h = el.clientHeight;
        if (!w || !h) return;

        var ratio = window.devicePixelRatio || 1;
        var cw = Math.round(w * ratio), ch = Math.round(h * ratio);
        if (canvas.width !== cw || canvas.height !== ch) { canvas.width = cw; canvas.height = ch; }
        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        ctx.clearRect(0, 0, w, h);
        ctx.font = '12px ' + state.font;

        var i, s, x, y;
        var left = 0;
        for (i = 0; i < state.ticks.length; i++) left = Math.max(left, ctx.measureText(state.ticks[i].l).width);
        left = Math.ceil(left) + 10;

        var top = 8, right = 12, bottom = 24;
        var pw = w - left - right, ph = h - top - bottom;
        if (pw < 10 || ph < 10) return;

        var span = view.right - view.left;
        if (span <= 0) return;

        var max = state.shownMax > 0 ? state.shownMax : 1;
        function px(time) { return left + (time - view.left) / span * pw; }
        function py(value) { return top + ph - value / max * ph; }

        // Value axis : dashed grid lines with their values, and a solid line at 0
        ctx.lineWidth = 1;
        ctx.textAlign = 'right';
        ctx.textBaseline = 'middle';
        for (i = 0; i < state.ticks.length; i++) {
            y = Math.round(py(state.ticks[i].v)) + 0.5;
            if (y < top - 1) continue;

            ctx.strokeStyle = state.gridColor;
            ctx.setLineDash(state.ticks[i].v === 0 ? [] : [3, 3]);
            ctx.beginPath();
            ctx.moveTo(left, y);
            ctx.lineTo(left + pw, y);
            ctx.stroke();

            ctx.fillStyle = state.textColor;
            ctx.fillText(state.ticks[i].l, left - 7, y);
        }
        ctx.setLineDash([]);

        // Time axis : labels at round times (in the Instance's Time Zone), which move with the samples and fade at the edges
        var labelWidth = ctx.measureText('00:00:00').width;
        var count = Math.max(1, Math.floor(pw / (labelWidth + 28)));
        var step = timeSteps[timeSteps.length - 1] * 1000;
        for (i = 0; i < timeSteps.length; i++) {
            if (span / (timeSteps[i] * 1000) <= count) { step = timeSteps[i] * 1000; break; }
        }
        var shift = state.offset * 60000;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'top';
        ctx.fillStyle = state.textColor;
        for (var time = Math.ceil((view.left + shift) / step) * step - shift; time <= view.right; time += step) {
            x = px(time);
            ctx.globalAlpha = clamp(Math.min(x - left, left + pw - x) / (labelWidth / 2), 0, 1);
            ctx.fillText(formatTime(state, time, step), x, top + ph + 7);
        }
        ctx.globalAlpha = 1;

        // The samples that are shown (with one on either side, which the line is drawn from and toward)
        var n = state.times.length;
        var i0 = 0, i1 = n - 1;
        while (i0 < n - 2 && state.times[i0 + 1] <= view.left) i0++;
        while (i1 > i0 + 1 && state.times[i1 - 1] >= view.right) i1--;

        function trace(values) {
            ctx.beginPath();
            for (var k = i0; k <= i1; k++) {
                if (k === i0) ctx.moveTo(px(state.times[k]), py(values[k]));
                else ctx.lineTo(px(state.times[k]), py(values[k]));
            }
        }

        ctx.save();
        ctx.beginPath();
        ctx.rect(left, 0, pw, top + ph + 1);
        ctx.clip();
        ctx.lineWidth = 2;
        ctx.lineJoin = 'round';

        if (state.area) {
            for (s = 0; s < state.series.length; s++) {
                trace(state.series[s].values);
                ctx.lineTo(px(state.times[i1]), py(0));
                ctx.lineTo(px(state.times[i0]), py(0));
                ctx.closePath();
                ctx.globalAlpha = 0.2;
                ctx.fillStyle = state.series[s].color;
                ctx.fill();
                ctx.globalAlpha = 1;
            }
        }
        for (s = 0; s < state.series.length; s++) {
            trace(state.series[s].values);
            ctx.strokeStyle = state.series[s].color;
            ctx.stroke();
        }

        // The sample nearest to the pointer
        var hover = -1;
        if (state.hoverX != null && state.hoverX >= left && state.hoverX <= left + pw) {
            var pointerTime = view.left + (state.hoverX - left) / pw * span;
            var best = Infinity;
            for (i = i0; i <= i1; i++) {
                if (state.times[i] < view.left || state.times[i] > view.right) continue;
                var distance = Math.abs(state.times[i] - pointerTime);
                if (distance < best) { best = distance; hover = i; }
            }
        }

        if (hover >= 0) {
            x = px(state.times[hover]);
            ctx.lineWidth = 1;
            ctx.globalAlpha = 0.5;
            ctx.strokeStyle = state.textColor;
            ctx.beginPath();
            ctx.moveTo(Math.round(x) + 0.5, top);
            ctx.lineTo(Math.round(x) + 0.5, top + ph);
            ctx.stroke();
            ctx.globalAlpha = 1;

            for (s = 0; s < state.series.length; s++) {
                y = py(state.series[s].values[hover]);
                ctx.fillStyle = state.panelColor;
                ctx.beginPath();
                ctx.arc(x, y, 4.5, 0, Math.PI * 2);
                ctx.fill();
                ctx.fillStyle = state.series[s].color;
                ctx.beginPath();
                ctx.arc(x, y, 3, 0, Math.PI * 2);
                ctx.fill();
            }
        }
        ctx.restore();

        if (hover >= 0) showTip(state, hover, x, w);
        else if (state.tipKey != null) { state.tip.style.display = 'none'; state.tipKey = null; }
    }

    // The time and each Series's value at a sample (both are formatted by the server)
    function showTip(state, index, x, width) {
        var tip = state.tip;

        if (state.tipKey !== state.times[index]) {
            state.tipKey = state.times[index];
            tip.textContent = '';

            var time = document.createElement('span');
            time.className = 'tooltip-time';
            time.textContent = state.timeLabels[index];
            tip.appendChild(time);

            for (var s = 0; s < state.series.length; s++) {
                var row = document.createElement('span');
                row.className = 'tooltip-series';

                var swatch = document.createElement('span');
                swatch.className = 'tooltip-swatch';
                swatch.style.background = state.series[s].color;
                row.appendChild(swatch);

                var title = document.createElement('span');
                title.textContent = state.series[s].title;
                row.appendChild(title);

                var value = document.createElement('span');
                value.className = 'tooltip-value';
                value.textContent = state.series[s].labels[index];
                row.appendChild(value);

                tip.appendChild(row);
            }
        }

        tip.style.display = 'flex';
        var tipWidth = tip.offsetWidth;
        var tipLeft = x + 12;
        if (tipLeft + tipWidth > width) tipLeft = x - 12 - tipWidth;
        tip.style.left = Math.max(0, tipLeft) + 'px';
    }

    window.thMetricsChart = { update: update, dispose: dispose };
})();
