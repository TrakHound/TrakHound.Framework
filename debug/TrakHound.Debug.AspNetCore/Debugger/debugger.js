// TrakHound Api Debugger (served by the Api debug host at /_debug).
// Reads the Api's OpenAPI Document (the one the Instance creates), sends Requests to its Endpoints, connects to its Streams
// (WebSocket or Server-Sent Events, with the Instance's message format) and shows the debug host's Log entries.

(function () {
    "use strict";

    const OpenApiUrl = "/_debug/openapi.json";
    const LogsUrl = "/_debug/logs";
    const MaxMessages = 500;
    const MaxLogs = 1000;
    const LogLevels = { critical: 0, error: 1, warning: 2, information: 3, debug: 4, trace: 5 };

    let documentJson = null;
    let items = [];
    let selected = null;
    let activeStream = null;


    // ---- Helpers ----

    // Creates an element. Text is always set as text (never parsed as HTML)
    function el(tag, attributes, ...children) {
        const element = document.createElement(tag);
        for (const [name, value] of Object.entries(attributes || {})) {
            if (value === null || value === undefined || value === false) continue;
            if (name === "class") element.className = value;
            else if (name.startsWith("on")) element.addEventListener(name.substring(2), value);
            else if (name === "value") element.value = value;
            else element.setAttribute(name, value === true ? "" : value);
        }
        for (const child of children.flat()) {
            if (child === null || child === undefined || child === false) continue;
            element.append(child instanceof Node ? child : document.createTextNode(String(child)));
        }
        return element;
    }

    function load(key) {
        try { return JSON.parse(localStorage.getItem(`trakhound.debugger.${key}`)); } catch { return null; }
    }

    function save(key, value) {
        try { localStorage.setItem(`trakhound.debugger.${key}`, JSON.stringify(value)); } catch { }
    }

    function resolve(schema) {
        let depth = 0;
        while (schema && schema.$ref && depth++ < 20) {
            const path = schema.$ref.replace(/^#\//, "").split("/");
            schema = path.reduce((o, key) => (o ? o[key] : undefined), documentJson);
        }
        return schema || {};
    }

    function schemaType(schema) {
        schema = resolve(schema);
        let type = Array.isArray(schema.type) ? schema.type.find(o => o !== "null") : schema.type;
        if (type === "array") return `${schemaType(schema.items)}[]`;
        return type || (schema.properties ? "object" : "string");
    }

    // A JSON skeleton of the Schema, to start a Request Body from
    function sample(schema, depth) {
        schema = resolve(schema);
        depth = depth || 0;
        if (depth > 6) return null;
        if (schema.default !== undefined) return schema.default;
        if (schema.enum && schema.enum.length) return schema.enum[0];
        const type = Array.isArray(schema.type) ? schema.type.find(o => o !== "null") : schema.type;
        switch (type) {
            case "object": {
                const result = {};
                for (const [name, property] of Object.entries(schema.properties || {})) result[name] = sample(property, depth + 1);
                return result;
            }
            case "array": return [sample(schema.items, depth + 1)];
            case "integer": case "number": return 0;
            case "boolean": return false;
            default: return schema.properties ? sample({ type: "object", properties: schema.properties }, depth) : "";
        }
    }

    function formatBytes(bytes) {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
    }

    function formatTime(date) {
        return date.toLocaleTimeString(undefined, { hour12: false }) + "." + String(date.getMilliseconds()).padStart(3, "0");
    }

    function statusClass(statusCode) {
        return statusCode >= 500 || statusCode === 0 ? "status error" : statusCode >= 400 ? "status warning" : "status";
    }

    function isText(contentType) {
        return !contentType || /^text\/|json|xml|javascript|yaml|csv/i.test(contentType);
    }

    function prettyText(text, contentType) {
        if (/json/i.test(contentType || "") || /^\s*[{[]/.test(text)) {
            try { return JSON.stringify(JSON.parse(text), null, 2); } catch { }
        }
        return text;
    }

    function base64ToBytes(base64) {
        const binary = atob(base64 || "");
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
    }

    // Copies text to the clipboard. Falls back to a selected textarea where the Clipboard API isn't allowed (ex. an embedded browser)
    async function copyText(text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        }
        catch { }

        const area = el("textarea", { readonly: true, style: "position: fixed; top: 0; left: 0; opacity: 0;" });
        area.value = text;
        document.body.append(area);
        area.select();
        let copied = false;
        try { copied = document.execCommand("copy"); } catch { }
        area.remove();
        return copied;
    }

    // A button that copies the text getText returns. getText runs when it's clicked and must return right away
    // (the clipboard only accepts a copy made while the click is being handled)
    function copyButton(getText, label) {
        label = label || "Copy";
        const button = el("button", { type: "button", title: "Copy to the clipboard" }, label);
        button.addEventListener("click", async () => {
            button.textContent = await copyText(String(getText() ?? "")) ? "Copied" : "Copy failed";
            setTimeout(() => (button.textContent = label), 1500);
        });
        return button;
    }

    // Runs action when Enter is pressed (with Ctrl or Cmd when modifier is true, so Enter alone still adds a new line)
    function onEnter(element, action, modifier) {
        element.addEventListener("keydown", e => {
            if (e.key !== "Enter" || e.isComposing) return;
            if (modifier ? !(e.ctrlKey || e.metaKey) : e.shiftKey) return;
            e.preventDefault();
            action();
        });
        return element;
    }

    // A route with its {parameters} highlighted
    function renderPath(path) {
        return el("span", { class: "path" }, path.split(/(\{[^}]*\})/).filter(Boolean).map(part =>
            part.startsWith("{") ? el("span", { class: "path-parameter" }, part) : part));
    }

    // The method and route of an endpoint, as a request line, with its controls (ex. Send) at the end
    function renderRequestBar(item, ...controls) {
        return el("div", { class: "request-bar" },
            el("span", { class: `method ${item.method.toLowerCase()}` }, item.method),
            el("span", { class: "request-path" }, renderPath(item.path)),
            el("div", { class: "request-controls" }, controls));
    }

    // The endpoint's summary (or route) as the title, its description and its group
    function renderTitle(item) {
        const description = item.summary && item.description ? item.description : (!item.summary ? item.description : null);
        return el("div", { class: "endpoint-title" },
            el("div", { class: "endpoint-group" }, item.group),
            el("h1", null, item.summary || item.path),
            description ? el("p", { class: "endpoint-description" }, description) : null);
    }

    function textToBase64(text) {
        const bytes = new TextEncoder().encode(text);
        let binary = "";
        for (const b of bytes) binary += String.fromCharCode(b);
        return btoa(binary);
    }


    // ---- Document ----

    async function loadDocument() {
        const response = await fetch(OpenApiUrl, { cache: "no-store" });
        documentJson = await response.json();

        document.getElementById("api-title").textContent = documentJson.info?.title || "";
        document.getElementById("api-version").textContent = documentJson.info?.version ? `Version ${documentJson.info.version}` : "";
        document.title = `${documentJson.info?.title || "Api"} - Api Debugger`;

        items = [];
        for (const [path, pathItem] of Object.entries(documentJson.paths || {})) {
            for (const [method, operation] of Object.entries(pathItem)) {
                if (!["get", "post", "put", "patch", "delete", "head", "options"].includes(method)) continue;
                items.push({
                    kind: "operation",
                    id: `${method.toUpperCase()} ${path}`,
                    method: method.toUpperCase(),
                    path: path,
                    group: operation.tags?.[0] || "Endpoints",
                    summary: operation.summary,
                    description: operation.description,
                    parameters: [...(pathItem.parameters || []), ...(operation.parameters || [])],
                    requestBody: operation.requestBody,
                    responses: operation.responses
                });
            }
        }
        for (const stream of documentJson["x-trakhound-streams"] || []) {
            items.push({
                kind: "stream",
                id: `STREAM ${stream.path}`,
                method: "STREAM",
                path: stream.path,
                group: "Streams",
                summary: stream.summary,
                description: stream.description,
                parameters: stream.parameters || []
            });
        }

        renderNav();

        const last = load("selected");
        const initial = items.find(o => o.id === (location.hash ? decodeURIComponent(location.hash.substring(1)) : last)) || items[0];
        if (initial) select(initial);
        else document.getElementById("main").replaceChildren(el("div", { class: "empty" }, "The Api has no Endpoints"));
    }


    // ---- Navigation ----

    function renderNav() {
        const filter = document.getElementById("search").value.trim().toLowerCase();
        const nav = document.getElementById("nav");
        const groups = new Map();

        for (const item of items) {
            if (filter && !`${item.method} ${item.path} ${item.summary || ""}`.toLowerCase().includes(filter)) continue;
            if (!groups.has(item.group)) groups.set(item.group, []);
            groups.get(item.group).push(item);
        }

        nav.replaceChildren(...[...groups.entries()].flatMap(([group, groupItems]) => [
            el("div", { class: "nav-group" }, el("span", null, group), el("span", { class: "nav-count" }, String(groupItems.length))),
            ...groupItems.map(item => el("div", {
                class: `nav-item${item === selected ? " selected" : ""}`,
                title: item.summary ? `${item.summary}\n${item.method} ${item.path}` : `${item.method} ${item.path}`,
                onclick: () => select(item)
            },
                el("span", { class: `method ${item.method.toLowerCase()}` }, item.method),
                el("span", { class: "nav-path" }, renderPath(item.path))))
        ]));

        if (groups.size === 0) nav.append(el("div", { class: "empty" }, "No Endpoints match"));
    }

    function select(item) {
        disconnectStream();
        selected = item;
        save("selected", item.id);
        history.replaceState(null, "", `#${encodeURIComponent(item.id)}`);
        renderNav();

        const main = document.getElementById("main");
        main.replaceChildren(el("div", { class: "content" }, (item.kind === "stream" ? renderStream(item) : renderOperation(item)).filter(Boolean)));
        main.scrollTop = 0;
    }


    // ---- Parameters ----

    // onSubmit runs when Enter is pressed in a parameter's field (Send, or Connect for a Stream), and onChange when a value changes.
    // The section's fields (key => row and input) are kept on it to mark the missing required values (see checkRequired)
    function renderParameters(item, values, onSubmit, onChange) {
        if (!item.parameters.length) return null;

        const fields = new Map();
        const rows = item.parameters.map(parameter => {
            const schema = resolve(parameter.schema);
            const type = schemaType(parameter.schema);
            const key = `${parameter.in}:${parameter.name}`;
            const value = values[key] !== undefined ? values[key] : (schema.default !== undefined ? String(schema.default) : "");

            const changed = newValue => {
                values[key] = newValue;
                fields.get(key)?.row.classList.remove("invalid");
                if (onChange) onChange();
            };

            let input;
            if (schema.enum && schema.enum.length) {
                input = el("select", { onchange: e => changed(e.target.value) },
                    parameter.required ? null : el("option", { value: "" }, ""),
                    schema.enum.map(o => el("option", { value: String(o), selected: String(o) === value }, String(o))));
                if (value === "" && parameter.required) values[key] = String(schema.enum[0]);
            }
            else if (type === "boolean") {
                input = el("select", { onchange: e => changed(e.target.value) },
                    el("option", { value: "" }, ""),
                    el("option", { value: "true", selected: value === "true" }, "true"),
                    el("option", { value: "false", selected: value === "false" }, "false"));
            }
            else {
                input = el("input", {
                    type: type === "integer" || type === "number" ? "number" : "text",
                    step: type === "number" ? "any" : null,
                    value: value,
                    placeholder: schema.default !== undefined ? `${type} (default ${schema.default})` : type,
                    oninput: e => changed(e.target.value)
                });
            }
            values[key] = values[key] !== undefined ? values[key] : value;
            if (onSubmit) onEnter(input, onSubmit);

            const row = el("div", { class: "parameter" },
                el("div", { class: "parameter-label" },
                    el("div", { class: "parameter-name" }, parameter.name, parameter.required ? el("span", { class: "required", title: "Required" }, "*") : null),
                    el("div", { class: "parameter-meta" }, el("span", { class: "chip" }, parameter.in), type)),
                el("div", { class: "parameter-field" },
                    input,
                    el("div", { class: "parameter-error" }, "Required"),
                    parameter.description ? el("div", { class: "parameter-description" }, parameter.description) : null));

            fields.set(key, { row, input });
            return row;
        });

        const section = el("div", { class: "section" },
            el("div", { class: "section-header" }, el("span", { class: "section-label" }, "Parameters")),
            el("div", { class: "section-body" }, rows));
        section.fields = fields;
        return section;
    }

    // Marks the required parameters that have no value (and focuses the first). Returns true when none are missing
    function checkRequired(section, item, values) {
        const missing = item.parameters.filter(o => o.required && !values[`${o.in}:${o.name}`]);
        for (const parameter of missing) section?.fields.get(`${parameter.in}:${parameter.name}`)?.row.classList.add("invalid");
        if (missing.length) section?.fields.get(`${missing[0].in}:${missing[0].name}`)?.input.focus();
        return missing.length === 0;
    }

    function buildUrl(item, values) {
        let path = item.path;
        const query = new URLSearchParams();

        for (const parameter of item.parameters) {
            const value = values[`${parameter.in}:${parameter.name}`];
            if (parameter.in === "path") {
                // A catch-all ({*name}) keeps its slashes
                const pattern = new RegExp(`\\{(\\**)${parameter.name}[^}]*\\}`);
                const match = path.match(pattern);
                if (match) path = path.replace(pattern, match[1] ? (value || "") : encodeURIComponent(value || ""));
            }
            else if (parameter.in === "query" && value !== undefined && value !== "") {
                query.append(parameter.name, value);
            }
        }

        const queryText = query.toString();
        return path + (queryText ? `?${queryText}` : "");
    }

    // The values of the "in: header" parameters, by Header name
    function buildHeaders(item, values) {
        const headers = {};
        for (const parameter of item.parameters) {
            const value = values[`${parameter.in}:${parameter.name}`];
            if (parameter.in === "header" && value !== undefined && value !== "") headers[parameter.name] = value;
        }
        return headers;
    }



    // ---- Requests ----

    function renderOperation(item) {
        const values = load(`values.${item.id}`) || {};
        const body = { contentType: null, text: null, file: null };
        const result = el("div", null, el("div", { class: "placeholder" }, "Send the request to see its response here"));

        // The URL the Request is sent to, as the parameters are filled in
        const urlPreview = el("div", { class: "url-preview", title: "The URL the request is sent to" });
        const updateUrl = () => (urlPreview.textContent = `${location.origin}${buildUrl(item, values)}`);

        const parameters = renderParameters(item, values, () => send(), updateUrl);
        updateUrl();

        const sendButton = el("button", { class: "primary", type: "button", onclick: () => send() }, "Send");
        const curlButton = copyButton(() => {
            let command = `curl -X ${item.method} '${location.origin}${buildUrl(item, values)}'`;
            for (const [name, value] of Object.entries(buildHeaders(item, values))) command += ` -H '${`${name}: ${value}`.replace(/'/g, "'\\''")}'`;
            if (body.contentType && body.file) command += ` -H 'Content-Type: ${body.contentType}' --data-binary '@${body.file.name}'`;
            else if (body.contentType && body.text) command += ` -H 'Content-Type: ${body.contentType}' --data-binary '${body.text.replace(/'/g, "'\\''")}'`;
            return command;
        }, "Copy as curl");

        const sections = [
            renderTitle(item),
            renderRequestBar(item, curlButton, sendButton),
            urlPreview,
            parameters
        ];

        const content = item.requestBody?.content || {};
        const contentTypes = Object.keys(content);
        if (contentTypes.length) {
            body.contentType = contentTypes[0];
            const savedBody = load(`body.${item.id}`);
            const editor = el("div");

            const renderEditor = () => {
                const schema = content[body.contentType]?.schema;
                if (/octet-stream|image\/|application\/pdf/i.test(body.contentType) || schema?.format === "binary") {
                    editor.replaceChildren(el("input", { type: "file", onchange: e => (body.file = e.target.files[0] || null) }));
                }
                else {
                    if (body.text === null) {
                        body.text = savedBody !== null ? savedBody : (/json/i.test(body.contentType) ? JSON.stringify(sample(schema), null, 2) : "");
                    }
                    editor.replaceChildren(onEnter(el("textarea", {
                        spellcheck: "false",
                        value: body.text,
                        title: "Ctrl+Enter sends the Request",
                        oninput: e => (body.text = e.target.value)
                    }), () => send(), true));
                }
            };
            renderEditor();

            sections.push(el("div", { class: "section" },
                el("div", { class: "section-header" },
                    el("span", { class: "section-label" }, "Body"),
                    item.requestBody.required ? el("span", { class: "chip" }, "required") : null,
                    el("div", { class: "spacer" }),
                    el("select", { onchange: e => { body.contentType = e.target.value; body.text = null; renderEditor(); } },
                        contentTypes.map(o => el("option", { value: o }, o)))),
                el("div", { class: "section-body" },
                    item.requestBody.description ? el("p", { class: "muted" }, item.requestBody.description) : null,
                    editor)));
        }

        const send = async () => {
            if (sendButton.disabled) return; // A Request is already being sent
            if (!checkRequired(parameters, item, values)) return;

            save(`values.${item.id}`, values);
            if (body.text !== null) save(`body.${item.id}`, body.text);

            const url = buildUrl(item, values);
            const init = { method: item.method, headers: buildHeaders(item, values) };
            if (body.contentType) {
                if (body.file) init.body = body.file;
                else if (body.text !== null && body.text !== "") init.body = body.text;
                if (init.body !== undefined) init.headers["Content-Type"] = body.contentType;
            }

            sendButton.disabled = true;
            sendButton.textContent = "Sending..";
            result.replaceChildren(el("div", { class: "placeholder" }, "Waiting for the response.."));
            const started = performance.now();

            try {
                const response = await fetch(url, init);
                const blob = await response.blob();
                renderResponse(result, response, blob, performance.now() - started);
            }
            catch (error) {
                result.replaceChildren(el("div", { class: "section" },
                    el("div", { class: "section-header" }, el("span", { class: "section-label" }, "Response"), el("span", { class: "status error" }, "Failed")),
                    el("div", { class: "section-body" }, String(error))));
            }
            finally {
                sendButton.disabled = false;
                sendButton.textContent = "Send";
            }
        };

        sections.push(result);
        return sections;
    }

    function renderResponse(container, response, blob, milliseconds) {
        const contentType = response.headers.get("content-type") || "";
        const bodyPanel = el("div");
        const headersPanel = el("table", { class: "headers" },
            [...response.headers.entries()].map(([name, value]) => el("tr", null, el("td", null, name), el("td", null, value))));
        headersPanel.hidden = true;

        if (blob.size === 0) {
            bodyPanel.append(el("div", { class: "muted" }, "No Content"));
        }
        else if (/^image\//i.test(contentType)) {
            bodyPanel.append(el("img", { class: "response-image", src: URL.createObjectURL(blob), alt: "Response image" }));
        }
        else if (isText(contentType)) {
            blob.text().then(text => bodyPanel.append(el("pre", { class: "code-block" }, prettyText(text, contentType))));
        }
        else {
            bodyPanel.append(el("a", { class: "link", href: URL.createObjectURL(blob), download: "response" }, `Download (${contentType}, ${formatBytes(blob.size)})`));
        }

        const bodyTab = el("button", { type: "button", class: "active" }, "Body");
        const headersTab = el("button", { type: "button" }, "Headers");
        bodyTab.addEventListener("click", () => { bodyPanel.hidden = false; headersPanel.hidden = true; bodyTab.className = "active"; headersTab.className = ""; });
        headersTab.addEventListener("click", () => { bodyPanel.hidden = true; headersPanel.hidden = false; bodyTab.className = ""; headersTab.className = "active"; });

        // Copies the Body as shown (formatted), or the Headers when they're shown. Binary Bodies can't be copied as text
        const textBody = blob.size > 0 && isText(contentType) && !/^image\//i.test(contentType);
        let bodyText = "";
        if (textBody) blob.text().then(text => (bodyText = prettyText(text, contentType)));
        const copy = copyButton(() => headersPanel.hidden
            ? bodyText
            : [...response.headers.entries()].map(([name, value]) => `${name}: ${value}`).join("\n"));
        copy.hidden = !textBody;
        bodyTab.addEventListener("click", () => (copy.hidden = !textBody));
        headersTab.addEventListener("click", () => (copy.hidden = false));

        container.replaceChildren(el("div", { class: "section" },
            el("div", { class: "section-header" },
                el("span", { class: "section-label" }, "Response"),
                el("span", { class: statusClass(response.status) }, `${response.status} ${response.statusText || ""}`.trim()),
                el("span", { class: "chip", title: "Time to the full response" }, `${milliseconds.toFixed(0)} ms`),
                el("span", { class: "chip", title: "Size of the body" }, formatBytes(blob.size)),
                contentType ? el("span", { class: "chip" }, contentType.split(";")[0]) : null,
                el("div", { class: "spacer" }),
                el("div", { class: "tabs" }, bodyTab, headersTab),
                copy),
            el("div", { class: "section-body" }, bodyPanel, headersPanel)));
    }


    // ---- Streams ----

    function renderStream(item) {
        const values = load(`values.${item.id}`) || {};
        const state = el("span", { class: "stream-state" }, "Disconnected");
        const count = el("span", { class: "chip" });
        count.hidden = true;
        const messages = el("div", { class: "messages", "data-empty": "Connect to see the Stream's messages here" });
        const transport = el("select", { title: "WebSocket can also send input to the Stream" },
            el("option", { value: "websocket" }, "WebSocket"),
            el("option", { value: "sse" }, "Server-Sent Events"));
        transport.value = load("transport") || "websocket";

        let received = 0;
        const updateCount = () => {
            count.textContent = `${received} received`;
            count.hidden = received === 0;
        };

        const addMessage = (message, sent) => {
            const contentType = message.contentType || "";
            const bytes = base64ToBytes(message.content);
            const text = isText(contentType) ? new TextDecoder().decode(bytes) : null;
            const detail = el("div");
            detail.hidden = true;

            const header = el("div", { class: "message-header", title: "Show or hide the content", onclick: () => (detail.hidden = !detail.hidden) },
                el("span", { class: "message-time" }, formatTime(new Date())),
                sent ? el("span", { class: "status sent" }, "Sent") : el("span", { class: statusClass(message.statusCode || 0) }, String(message.statusCode ?? "")),
                el("span", { class: "message-type" }, contentType),
                el("span", { class: "message-preview" }, text !== null ? text.replace(/\s+/g, " ").substring(0, 200) : formatBytes(bytes.length)));

            if (text !== null) detail.append(el("pre", { class: "code-block" }, prettyText(text, contentType)), el("div", { class: "message-actions" }, copyButton(() => prettyText(text, contentType))));
            else if (/^image\//i.test(contentType)) detail.append(el("img", { class: "response-image", src: URL.createObjectURL(new Blob([bytes], { type: contentType })), alt: "Message image" }));
            else detail.append(el("a", { class: "link", href: URL.createObjectURL(new Blob([bytes], { type: contentType })), download: "message" }, `Download (${formatBytes(bytes.length)})`));

            const follow = messages.scrollTop + messages.clientHeight >= messages.scrollHeight - 20;
            messages.append(el("div", { class: `message${sent ? " sent" : ""}` }, header, detail));
            while (messages.childElementCount > MaxMessages) messages.firstElementChild.remove();
            if (follow) messages.scrollTop = messages.scrollHeight;
        };

        const setState = (text, className) => {
            state.textContent = text;
            state.className = `stream-state${className ? ` ${className}` : ""}`;
        };

        const inputType = el("select", null, el("option", { value: "application/json" }, "application/json"), el("option", { value: "text/plain" }, "text/plain"));
        const inputText = el("textarea", { spellcheck: "false", placeholder: "Input to push to the Stream (WebSocket only). Enter sends it, Shift+Enter adds a line" });
        const sendInput = el("button", { type: "button", disabled: true, onclick: () => {
            if (!activeStream?.socket || activeStream.socket.readyState !== WebSocket.OPEN) return;
            const message = { statusCode: 200, contentType: inputType.value, content: textToBase64(inputText.value) };
            activeStream.socket.send(JSON.stringify(message));
            addMessage(message, true);
        } }, "Send");
        onEnter(inputText, () => { if (!sendInput.disabled) sendInput.click(); });

        const connect = () => {
            if (!checkRequired(parameters, item, values)) return;

            save(`values.${item.id}`, values);
            save("transport", transport.value);
            disconnectStream();

            const url = buildUrl(item, values);
            const onMessage = data => {
                try { addMessage(JSON.parse(data), false); }
                catch { addMessage({ statusCode: 0, contentType: "text/plain", content: textToBase64(String(data)) }, false); }
                received++;
                updateCount();
            };

            setState("Connecting..");
            connectButton.disabled = true;
            disconnectButton.disabled = false;

            if (transport.value === "sse") {
                const source = new EventSource(url);
                activeStream = { source };
                source.onopen = () => setState("Connected (Server-Sent Events)", "open");
                source.onmessage = e => onMessage(e.data);
                source.onerror = () => {
                    // EventSource reconnects on its own; a closed source won't
                    if (source.readyState === EventSource.CLOSED) { setState("Closed", "error"); connectButton.disabled = false; disconnectButton.disabled = true; }
                    else setState("Reconnecting..", "error");
                };
            }
            else {
                const socket = new WebSocket(`${location.protocol === "https:" ? "wss" : "ws"}://${location.host}${url}`);
                socket.binaryType = "arraybuffer";
                activeStream = { socket };
                socket.onopen = () => { setState("Connected (WebSocket)", "open"); sendInput.disabled = false; };
                socket.onmessage = e => onMessage(typeof e.data === "string" ? e.data : new TextDecoder().decode(e.data));
                socket.onclose = e => {
                    if (activeStream?.socket !== socket) return; // Disconnected from the page (already shown)
                    activeStream = null;
                    setState(`Closed${e.code && e.code !== 1000 ? ` (${e.code}${e.reason ? ` : ${e.reason}` : ""})` : ""}`, e.code && e.code !== 1000 && e.code !== 1005 ? "error" : null);
                    connectButton.disabled = false;
                    disconnectButton.disabled = true;
                    sendInput.disabled = true;
                };
            }
        };

        const connectButton = el("button", { class: "primary", type: "button", onclick: connect }, "Connect");
        const disconnectButton = el("button", { type: "button", disabled: true, onclick: () => {
            disconnectStream();
            setState("Disconnected");
            connectButton.disabled = false;
            disconnectButton.disabled = true;
            sendInput.disabled = true;
        } }, "Disconnect");

        const urlPreview = el("div", { class: "url-preview", title: "The URL the Stream connects to" });
        const updateUrl = () => (urlPreview.textContent = `${transport.value === "sse" ? location.protocol : (location.protocol === "https:" ? "wss:" : "ws:")}//${location.host}${buildUrl(item, values)}`);
        transport.addEventListener("change", updateUrl);

        const parameters = renderParameters(item, values, () => { if (!connectButton.disabled) connect(); }, updateUrl);
        updateUrl();

        return [
            renderTitle(item),
            renderRequestBar(item, transport, disconnectButton, connectButton),
            el("div", { class: "stream-status" }, urlPreview, state),
            parameters,
            el("div", { class: "section" },
                el("div", { class: "section-header" }, el("span", { class: "section-label" }, "Input"), el("span", { class: "chip" }, "WebSocket only"), el("div", { class: "spacer" }), inputType),
                el("div", { class: "section-body composer" }, inputText, el("div", { class: "composer-actions" }, sendInput))),
            el("div", { class: "section" },
                el("div", { class: "section-header" }, el("span", { class: "section-label" }, "Messages"), count, el("div", { class: "spacer" }),
                    el("button", { type: "button", onclick: () => { messages.replaceChildren(); received = 0; updateCount(); } }, "Clear")),
                el("div", { class: "section-body messages-body" }, messages))
        ];
    }

    function disconnectStream() {
        if (!activeStream) return;
        try { activeStream.socket?.close(1000); } catch { }
        try { activeStream.source?.close(); } catch { }
        activeStream = null;
    }


    // ---- Log ----

    function initLogs() {
        const panel = document.getElementById("logs");
        const list = document.getElementById("logs-list");
        const count = document.getElementById("logs-count");
        const levelSelect = document.getElementById("logs-level");
        const filterInput = document.getElementById("logs-filter");
        let total = 0;

        levelSelect.value = String(load("logLevel") ?? 3);
        const toggle = document.getElementById("logs-toggle");
        const setCollapsed = collapsed => {
            panel.classList.toggle("collapsed", collapsed);
            toggle.title = collapsed ? "Show the log" : "Hide the log";
            save("logsCollapsed", collapsed);
        };
        setCollapsed(!!load("logsCollapsed"));

        const visible = row => Number(row.dataset.level) <= Number(levelSelect.value) &&
            (!filterInput.value || row.textContent.toLowerCase().includes(filterInput.value.toLowerCase()));
        const applyFilter = () => { for (const row of list.children) row.hidden = !visible(row); };

        levelSelect.addEventListener("change", () => { save("logLevel", Number(levelSelect.value)); applyFilter(); });
        filterInput.addEventListener("input", applyFilter);
        document.getElementById("logs-clear").addEventListener("click", () => { list.replaceChildren(); total = 0; count.textContent = ""; });
        toggle.addEventListener("click", () => setCollapsed(!panel.classList.contains("collapsed")));

        // Drag the top edge to resize the log (kept between sessions)
        const savedHeight = load("logsHeight");
        if (savedHeight) panel.style.height = `${savedHeight}px`;
        document.getElementById("logs-resize").addEventListener("pointerdown", e => {
            if (panel.classList.contains("collapsed")) return;
            e.preventDefault();
            const startY = e.clientY;
            const startHeight = panel.getBoundingClientRect().height;
            const move = m => {
                const height = Math.min(Math.max(startHeight + startY - m.clientY, 90), window.innerHeight * 0.75);
                panel.style.height = `${height}px`;
            };
            const up = () => {
                document.removeEventListener("pointermove", move);
                document.removeEventListener("pointerup", up);
                document.body.classList.remove("resizing");
                save("logsHeight", Math.round(panel.getBoundingClientRect().height));
            };
            document.body.classList.add("resizing");
            document.addEventListener("pointermove", move);
            document.addEventListener("pointerup", up);
        });

        const source = new EventSource(LogsUrl);
        source.onmessage = e => {
            let entry;
            try { entry = JSON.parse(e.data); } catch { return; }

            const levelName = (entry.level || "Information").toLowerCase();
            const row = el("div", { class: `log ${levelName}`, "data-level": LogLevels[levelName] ?? 3 },
                el("span", { class: "log-time" }, formatTime(new Date(entry.timestamp))),
                el("span", { class: "log-level" }, entry.level),
                el("span", { class: "log-source" }, entry.source),
                el("span", null, entry.code ? `${entry.code} : ${entry.message}` : entry.message));
            row.hidden = !visible(row);

            const follow = list.scrollTop + list.clientHeight >= list.scrollHeight - 20;
            list.append(row);
            while (list.childElementCount > MaxLogs) list.firstElementChild.remove();
            if (follow) list.scrollTop = list.scrollHeight;

            total++;
            count.textContent = `(${total})`;
        };
    }


    // ---- Sidebar ----

    function initSidebar() {
        const sidebar = document.querySelector(".sidebar");
        const toggle = document.getElementById("sidebar-toggle");
        const setCollapsed = collapsed => {
            sidebar.classList.toggle("collapsed", collapsed);
            toggle.title = collapsed ? "Show the endpoints" : "Hide the endpoints";
            save("sidebarCollapsed", collapsed);
        };

        setCollapsed(!!load("sidebarCollapsed"));
        toggle.addEventListener("click", () => setCollapsed(!sidebar.classList.contains("collapsed")));
    }


    // ---- Start ----

    document.getElementById("search").addEventListener("input", renderNav);
    window.addEventListener("hashchange", () => {
        const item = items.find(o => o.id === decodeURIComponent(location.hash.substring(1)));
        if (item && item !== selected) select(item);
    });
    window.addEventListener("beforeunload", disconnectStream);

    initSidebar();
    initLogs();
    loadDocument().catch(error => {
        document.getElementById("main").replaceChildren(el("div", { class: "empty" }, `Unable to load the OpenAPI document (${OpenApiUrl}) : ${error}`));
    });
})();
