![TrakHound](images/trakhound-guide-header.png)

# TrakHound Instance User Guide

Oct 20, 2026 · Patrick Ritchie

## Contents

1. Overview
2. Using the Admin interface
    - 2.1 Layout and navigation
    - 2.2 Signing in and notifications
    - 2.3 Explorer (2.3.1 Instance, 2.3.2 Apps, 2.3.3 Api, 2.3.4 Functions, 2.3.5 Workers, 2.3.6 Services, 2.3.7 Diagnostics)
    - 2.4 Packages
    - 2.5 Config (Groups, Routers, Variables, Access and Settings)
    - 2.6 Deploy (2.6.1 Installing a deployment at startup, 2.6.2 Workspaces)
    - 2.7 Security (2.7.1 Zones, 2.7.2 Users, 2.7.3 Admin accounts and roles)
    - 2.8 Licenses
    - 2.9 Settings
    - 2.10 MCP Server
    - 2.11 Api Catalog and API Explorer
    - 2.12 Welcome page (development Instances)
3. Configuring the Instance
    - 3.1 Common settings
    - 3.2 Admin accounts
    - 3.3 Logs
    - 3.4 Health checks
    - 3.5 Rate limits
    - 3.6 Custom Initialization Page
    - 3.7 Package verification (3.7.1 Packages approved by a TrakHound Package Manager)
4. Troubleshooting

## 1. Overview

The TrakHound Instance runs your TrakHound packages (Apps, APIs, Workers, Services and Functions). This guide covers using a running Instance: the Admin interface, configuration files, logs and troubleshooting. To install, upgrade or uninstall the Instance, see the TrakHound Instance Installation Guide (`instance-installation-guide.pdf`).

Open the Admin interface at `/_admin` on the Instance's address, ex. `http://localhost:8472/_admin` for a Windows Service on the default port. Section 2.2 covers signing in for the first time.

## 2. Using the Admin interface

The Admin interface at `/_admin` is where you monitor the Instance, install packages, configure Apps, APIs and Workers, and manage security. Every page requires signing in with an admin account, and the account's roles decide which pages it can open and what it can change (section 2.7.3). With a separate Admin port, `/_admin` is served only on that port.

### 2.1 Layout and navigation

| Area | What it holds |
| --- | --- |
| Header | Instance name and ID, base URL, version, status with uptime, and a Health badge that opens Diagnostics > Health. On a narrow window the header hides details (the version first) so its buttons always fit |
| Header buttons | Light/Dark theme, Help (links to the documentation, Getting Started, the TrakHound website, Contact Us and GitHub), Notifications (bell icon, with an unread count), your account (its roles, and **Change Password**), Sign Out |
| Left sidebar | Explorer, Packages, Config, Deploy, Security, Licenses. Only the areas your roles let you view are shown |
| Area header | The row under the header, the same in every area. It shows the area's name, then where the page you're on is (ex. **Configuration > Apis > New**). Click a name to go back to it. An area's tools are on the right (ex. the package source), followed by a help icon (**?**) that opens the documentation for the page you're on. Some Settings sections have their own help icon |
| Area sidebar | The area's sections (ex. Installed, Updates, Browse, Upload and Sources in Packages), with the number of items in each. Each section has its own address, so it can be bookmarked or linked to. In a narrow window the sections are a row above the page. The Explorer's sidebar is a tree instead |

The help links open pages of the TrakHound website in a new tab. The Admin interface only links to the website and never requests anything from it, so an Instance without internet access works the same.

### 2.2 Signing in and notifications

- **Sign in** at `/_admin/login` with your Username and Password. After signing in you return to the page you opened, or to the first area your roles let you view. The first admin account can't be created here: if none exists, the page says how to create one on the machine the Instance runs on (section 3.2). An Owner adds more accounts on Security > **Admin Accounts** (section 2.7.3).
- The session lasts 7 days, survives restarts, and is shared by the browser's tabs. **Sign Out** (in the header) ends it in every tab. After 5 wrong passwords from one address (or 20 for one username from any address), sign in is locked for 1 minute, then twice as long each time it happens again (up to 15 minutes).
- **Notifications** (bell icon) list Instance events, package updates and configuration changes. Use **Mark all read**, **Clear all**, or **Dismiss** on one item.

### 2.3 Explorer

Explorer (`/_admin/explorer`) is the home page. Its tree lists everything on the Instance: Apps, Api, Functions, Workers, Services and Diagnostics. Use **Filter**, **Expand All**, **Collapse All** and Refresh to find an item, then select it to open its page. Apps, Apis, Functions and Workers that are in a Group (section 2.5) are listed in a folder for their Group; filtering by a Group's name shows its Modules. While the Instance is starting, the tree shows only the Modules loaded so far, with a notice that more will appear. It reloads by itself when the Instance has started (also after a Start or Restart).

#### 2.3.1 Instance

Select the Instance at the top of the tree.

- **Start**, **Stop** and **Restart** the Instance. Stop and Restart ask you to confirm. Restart applies settings that need a restart.
- Set a display name, or copy the Instance ID.
- Tabs: **Overview**, **Information** (admin sign-in, Admin port and System API status, and why the previous run stopped if it didn't shut down cleanly), **Monitor**, **Boots** and **Log**.
- The **Log** tabs of the Instance, an API, an Endpoint and a Worker, and Explorer > Diagnostics > Logs, work the same way. **Live** shows new lines as they're written (it starts when the tab opens; **Unsubscribe** pauses it). **History** shows the lines kept in Log History, newest first, as soon as it opens, and runs again when you change its **Minimum Level**, **Range** (Last hour to Last 30 days, or Custom for a From and To), or search. **Refresh** moves a range like Last hour up to now. Click a level's count to show only that level, or a line's Source to show only that Source (remove it with its **x**). The button on a line's time shows every line a minute either side of it. **Load 500 More** is after the last line, and **Download** saves the lines shown as a `.log` file. Live and History have their own Minimum Level. History only offers the levels Log History keeps (Information and above by default, set in Settings > Logs), so Debug lines (ex. an API's Request lines) show in Live only.

**Monitor** shows the Instance process's CPU and memory, and **CPU by Module**: the CPU used by Workers, Functions, APIs, Apps and the Admin Interface, a chart of the last 10 minutes, and a table of every Module with its current CPU and total CPU time.

- **Unattributed** is the process CPU not measured for any Module: the HTTP server, Services' own background work (ex. a database connection pool) and the .NET Garbage Collector. A large Unattributed value points at work outside the Modules.
- A Module's CPU includes the Services it calls. When one Module calls another (ex. a Worker calls an API), the time spent in the called Module is counted to that Module, not the caller.
- An App's CPU is the work its pages do in the browser's connection to the Instance (events, rendering and JavaScript calls). Pages that aren't part of an App are counted as **Admin**.
- CPU by Module is measured when Settings > Metrics > **Measure CPU Usage** is on. API and App Metrics also show each API's and App's CPU, and API Metrics shows it for each Endpoint.

**Boots** lists the last 100 runs of the Instance process, newest first. Each row shows how the run ended, when it started, its boot time (from the process starting until the HTTP server and the Runtime were running), the warnings and errors logged while it started, its Runtime Starts and its uptime. Click a row to see:

- Each startup step with its duration (Read Configuration, Create Host, Initialize Instance, Configure Services, Start HTTP Server), and the step that failed, with the exception.
- Each Runtime Start: the first, then one for each Restart. A Runtime Start shows each of its steps (ex. Load Packages, Load Workers), whether it started, and when it was stopped.
- The first 20 warnings and errors logged during startup and during each Runtime Start, with the logger that wrote each one.

| Status | Meaning |
| --- | --- |
| Starting | The process is starting |
| Running | The process started and is still running (marked **Current** for this process) |
| Stopped | The process shut down normally. **Ended** shows why (ex. a Windows Service stop or Ctrl+C) |
| Failed | The process couldn't start (ex. the HTTP port is in use) |
| Crashed | An unhandled exception ended the process |
| Unclean | The process ended without shutting down (ex. killed, power loss or a Stack Overflow). The next run finds this and fills in the reason, as on the **Information** tab |

A Runtime Start that fails (the Runtime shows Error) doesn't fail the Boot, because the process keeps running and the Runtime can be started again. The Boot shows it as a failed Runtime Start. **Overview** shows this run's boot time.

To download the Boots, use the buttons above the list:

- **JSON** has everything recorded for each Boot: the steps, the Runtime Starts, the log entries and the exception. To download one Boot (ex. to attach to a support request), use **JSON** in its details.
- **CSV** has one summary row per Boot, for a spreadsheet: the status, the start, ready and end times, the boot time and uptime (in milliseconds), the version, host and process ID, the warning and error counts, the Runtime Starts (and how many failed), the reason it ended and the exit code. Times are in UTC.

Both have only what the Boot recorded. The log entries are the first 20 warnings and errors, and only the first line of each. Use the **Log** tab for the full log.

The Boots are kept in `boots.json` in the configuration profile folder, not in Log History, so they're recorded even when Log History is off. Deployments and backups leave the file out. The System API serves them at `/_instances/boots` (`take` sets how many, up to 100) and `/_instances/boots/<bootId>`, controlled by the `system:instance` resource.

#### 2.3.2 Apps

| Page | What you can do |
| --- | --- |
| App list | See each App's Route, Version, Status, Views and Last Viewed. Open an App, or use Actions: Identify, Notify Users, Send Message, Reload Configuration, Reload Pages, Access Log, Modules, Edit Configuration |
| App | Package and repository details and the App's pages (Route, Description, ID). The toolbar has the App list's Actions: Refresh, Open, Access Log, Connections, Configuration, Identify, Notify Users, Send Message, Reload Pages and Reload Configuration |
| App page | **Information**, and **Preview** to view the page inside the Admin interface |

Send Message shows a message to everyone using the App: choose Information or Warning, and whether it closes automatically or when the user closes it.

#### 2.3.3 Api

| Page | What you can do |
| --- | --- |
| API list | See each API's Route, Package, Endpoints, Requests, Req/s, Avg Time, Errors and Last Request |
| API | **Reload** the API. Tabs: **Endpoints**, **ReadMe**, **Logs** (Live and History, for all of the API's Endpoints), **Volume** |
| Endpoint | Tabs: **Run** (below), **Information**, **Logs** (Live and History, for this Endpoint) |

The endpoint's **Run** tab sends a request to the endpoint:

- **Run** (or **Subscribe** for a Stream endpoint) is at the top, beside a status bar. The bar shows the status, the method, and the response's Status Code, Content Type, Size and Duration. Errors, such as missing required parameters, show in the bar too. **Cancel** stops a request in progress and **Unsubscribe** stops a Stream.
- The request URL is under the bar. **Copy URL** copies it, and **Copy as curl** copies a curl command with the method, URL, Content Type and body (a file body is sent with `--data-binary @<filename>`).
- The Request Parameters are in a table on the right: each parameter's Name, where it goes (Route, Query or Body), its DataType and its Value. Hover over a parameter's name to see its description. A parameter with a default value shows "Default: <value>"; a required one is marked with `*`. For a JSON body, **Format** indents the JSON and **Example** fills in an example of the body's type. A binary body can be typed as text or uploaded with **Browse..**.
- The collapse button at the top of the Request Parameters hides them, so the response gets the full width. Click the strip to show them again. The browser remembers whether they're hidden, and they are shown when a required parameter is missing.
- Drag the left edge of the Request Parameters to change their width (double-click the edge to reset it). The browser remembers the width for every endpoint.
- The parameter values are saved in the browser for each endpoint and filled in when you open it again. **Clear** empties them.
- The response is on the left: its parameters and its content. For a Stream endpoint, the last 100 messages are listed with the time they were received, newest first. Select a message to see its content. The list is kept after **Unsubscribe**, until you subscribe again.
- On a narrow window the Request Parameters are shown above the response.

If the System API is turned off, these pages say so. Turn it on in Settings > Interfaces.

#### 2.3.4 Functions

| Page | What you can do |
| --- | --- |
| Function list | Tabs **Functions** and **History**. See Runs, Avg Time, Errors and Last Run for each Function |
| Summary | Run metrics for every Function (Runs, Success Rate, Avg Duration, Errors, Missed Runs, Next Run) and for each of its Scheduled Tasks, with a run timeline. **Manage** opens the Function's Schedule |
| Schedule Board | Schedule Functions by drag and drop (below) |
| Function | Tabs **Information**, **Run**, **History**, **Jobs**, **Schedule** and **Volume** (below) |
| Run | One run. The top shows its Status, Status Code and message, then when it started, its Duration, CPU time, what started it (Manual, API or a Scheduled Task), its Job, the package version that ran (marked when a different version is installed now) and its Run ID. Tabs **Logs** (filter by Level; Warnings and Errors are highlighted, and times are since the run started, with the full time on hover), **Input** and **Output**: JSON values are indented, long values are collapsed (**Show all**), and each value can be copied. **Run Again** opens the Function's Run tab with the run's Job and Input Parameters. Input Parameters are saved in the database with each run (like a Job's), so don't pass secrets as Parameters. Older runs may not show an Input, Job, CPU time or version |

A Function's tabs:

| Tab | What you can do |
| --- | --- |
| Run | **Run** is at the top beside the run's Status, Duration, start time, CPU time and message (**View Run** opens the recorded run), and becomes **Cancel** while a run is active. The left side shows **Logs** (filter by Level; times are since the run started, with the full time on hover; the log follows new lines only while scrolled to the bottom) and **Output** (shown when a run succeeds). The right side holds what a run uses: a **Job** (to run with its Parameters), the Input Parameters (a Function that describes its Parameters gets a field for each, with its description, default and choices, and a missing Required Parameter is reported before the run starts), **Save as Job** (a new Job, or the selected one), and the last 5 runs from this page (**Load** puts a run's Job and Input back). Drag the right side's left edge to change its width (double-click the edge to reset it); the width is kept for every Function. On a narrow window the Input is above the Logs. Without the `TrakHound.Functions.Api` package, the Function is run directly and the run isn't recorded |
| History | Query past runs by date |
| Jobs | **Add Job**, **Edit** or **Delete** a Job: a named set of Parameters (Name, Description, Parameters) to run the Function with. Parameters the Function describes get their own fields, as on the Run tab. Each Job has an ID made from its Name (or given when it's added) that is the same on every Instance, so Scheduled Tasks copied to another Instance still find their Job. Schedules shows the Scheduled Tasks that run each Job. Edits apply to the Job's next run, and a Job used by a Scheduled Task can't be deleted |
| Schedule | **Add Task** or **Edit** a Scheduled Task: Description, Schedule (a CRON expression), Time Zone, whether to run a missed occurrence when the Scheduled Tasks Worker starts, and the Job. A Task with no Job runs the Function without Parameters. The grid shows each Task's Last Run, Next Run, Runs (24h), Success and Missed runs. **Delete** removes a Task |

The **Schedule Board** has a column per schedule. Drag a Function, or one of its Jobs, from the palette onto a column to schedule it (**Add Column** for a new schedule), and drag a card to another column to move it. Each card can be paused, resumed or deleted, and the last change can be undone with **Undo** (a deleted Task can't be restored). The top of the Board shows the Scheduled Tasks Worker's status: whether it is running, its busy Run Slots and overlap policy, what is **Running Now**, waiting and **Upcoming**, and **Recent Events**. A card warns when its runs take longer than its schedule interval, so occurrences are skipped.

**Export** and **Import** (on the Schedule Board, and on a Function's Schedule tab for that Function) copy Jobs and Scheduled Tasks between Instances as a `.json` file, which can also be written by hand. Export saves the Tasks with the Jobs they use. Import opens a file and checks it first without writing anything (a dry run), listing what would be added, replaced or kept, and any Task whose Job is missing. Choose what happens **When a Job or Task already exists**: **Don't import (show the differences)**, **Keep the existing one** or **Replace it**, then click **Import**.

The Summary, Jobs, Schedule and Schedule Board need the `TrakHound.Functions.Api` package, and scheduling also needs `TrakHound.ScheduledTasks.Api` and `TrakHound.ScheduledTasks.Worker`.

#### 2.3.5 Workers

| Page | What you can do |
| --- | --- |
| Worker list | See each Worker's Version, Status, CPU and Last Updated, and Start or Stop it |
| Worker | **Information**, **Log** (Live and History) and **Volume** |

#### 2.3.6 Services

Select **Services** to see every Service on the Instance. A card for each service type shows how many Services support it, and the list shows each Service's Types, Description, Package, Version and Configuration ID. Click a type to open that Service's page for it. **Service Metrics** opens its request metrics (section 2.3.7).

Each service type lists its configurations (Name, Description, Package, Version, Configuration ID). Click **View** to browse its data:

| Service type | What you can do |
| --- | --- |
| Blobs | Browse collections; **Upload**, **Download**, **Link** and **Delete** blobs |
| Key Values | **Publish**, **Edit**, **Copy** and **Delete** keys |
| Messages | Browse the message tree |
| Message Queues | **Publish**, **Pull**, **Subscribe**, **Acknowledge All**, **Reject All**, **Clear** |
| Tables | Write a query in the database's own SQL (Ctrl+Enter runs it), pick a table, and view the results grid |
| TimeSeries | Browse Measurements; pick a Series, Fields and time range (in UTC) and see a chart and table of the Points or a Function, such as a Mean, with a **Window** that sets how much time each value covers (**Auto** picks one from the range; a Window that is too short for the range uses the next longer one); **Write** a test Point |

#### 2.3.7 Diagnostics

| Page | What it shows |
| --- | --- |
| Health | The status of every health check. **Check Now** re-runs them; **Settings** opens the Critical and Ignore lists (section 2.9) |
| Modules | The package modules the Instance loaded. Filter by Loaded, Failed or Warnings; select one to see its types, folder and errors |
| Metrics: Api, Apps, HTTP, Functions, Services, Workers, Streams, MCP, Buffers | Request rates, timings and errors, page views, connections, and buffer throughput. Streams covers WebSocket and Server-Sent Events connections; MCP covers MCP Server Tool calls and clients. A metric turned off in Settings > Metrics shows a notice instead |
| Logs | Errors, Warnings and lines logged by each logger over the Last hour, 24 hours or 7 days, with when each last logged (below). Select a logger in the tree to see its log |
| Connections | **App Pages**: browsers connected to Apps (Client, App, Version, Status, Path), with **Notify All**, **Send Message** and **Reload**. It flags clients on an old App version and when a reload is safe. **Api Requests**, **Api Streams** and **HTTP Requests** list recent traffic (see below) |

Use Modules first when an App, API or Worker shows as not loaded: it shows the load error.

**Logs** lists the loggers (grouped by package, ex. `api/TrakHound.Functions.Api`, or by system logger) with the most Errors and Warnings first. Click **Errors** or **Warnings** to show only the loggers with them, and click a logger to open its History. The line at the top says what Log History keeps (levels, days and size) and links to Settings > Logs. A logger's page names it in the header and breadcrumbs (click a breadcrumb to see its parent), selects it in the tree, starts Live by itself, and links to its Instance, API or Worker (**Open Api**, **Open Worker**). The Log tabs of the Instance, an API, an Endpoint and a Worker link back with **Open in Diagnostics**.

**HTTP Requests** lists the most recent HTTP Requests the selected Instance received (100 by default; change it in Settings > Metrics > Recent Requests). It updates every second.

- **Pause** stops the updates so the rows stay where they are. Updates also stop while a Request is open.
- Filter by status (**2xx**, **3xx**, **4xx**, **5xx**, **Errors**), by **Area**, by a **Slower than** time in milliseconds, and hide **Streams**.
- WebSocket and Event Stream Requests are tagged. Their duration is how long the connection was open, not a response time, so they have no duration bar.
- Click a Request to see its times, sizes, Route and Query parameters. **View Endpoint in HTTP Metrics** opens Metrics > HTTP filtered to that Endpoint.
- The values of sensitive Query parameters (ex. `token`, `apiKey`, `password`, `code`) are replaced with `***` before the Request is kept.

**API Requests** lists the most recent Requests to the APIs on the selected Instance, from any source: the Instance, Apps, other APIs, Functions, Workers and HTTP clients (100 by default, for all APIs together; change it in Settings > Metrics > Recent Requests). It updates every second, and **Pause** works as it does for HTTP Requests.

- Each row shows the Path that was requested (ex. `/runs/123`); hover over it to see the Endpoint's Route (ex. `runs/{id}`).
- Filter by status, by **API**, by **Source** type, by a **Slower than** time in milliseconds, and hide **Streams**. A Stream has no status code, and its duration is the time to open it.
- Click a Request to see its times, sizes, Package and version, Source, Query parameters and **Request ID**. For a status of 400 or higher it also shows the **Error** the API returned (ex. the exception message for a 500). Search the Endpoint's Logs for the Request ID to find its log entries.
- **API Metrics** opens Metrics > Api for the Request's API, and **Endpoint** opens the Endpoint in the Explorer (where you can run it).
- Sensitive Query parameter values are replaced with `***`, as they are for HTTP Requests.

**API Streams** lists the Streams open to the APIs on the selected Instance, from any source. Metrics > Streams only sees Streams opened over HTTP (WebSockets and Server-Sent Events); this page also shows Streams opened inside the Instance by Apps, Workers, Functions and other APIs. **Closed** shows the most recently closed Streams (100 by default; change it in Settings > Metrics > Recent Requests).

- Each Stream shows its Source, API, Path, how long it has been open, the **Messages** and **Data** it delivered, and when its **Last Message** was. A Stream open for 10 minutes or more without a Message is highlighted.
- Filter by **API**, by **Source** type, and by **Idle longer than** a number of minutes (to find Streams that are open but not delivering anything).
- A closed Stream shows why it closed: **Closed by Source** (ex. the client disconnected or the App stopped subscribing), **Closed by Admin**, **Error** (with the error message) or **Stopped** (the Instance stopped).
- Click a Stream to see its times, Package and version, Source, User Agent, Query parameters, Stream ID and Request ID, with links to its **API Metrics** and **Endpoint**.
- **Close Stream** (click it twice to confirm) closes an open Stream; its Source sees the Stream end. It needs write permission for the System API Apis resource.

### 2.4 Packages

Packages (`/_admin/packages`) installs, updates and removes packages. Choose the package source in the area header.

| Section | What you can do |
| --- | --- |
| Installed | Search installed packages and see Category, Version, Used by, Size and Latest. **Remove** versions, **Revert** to the previous version, or install an update. **Clean Unused** removes packages no configuration uses |
| Updates | The installed packages that have a newer version |
| Browse | Packages on the source, by category, with **Install** |
| Upload | Drop `.thp` package files, then click **Install** |
| Sources | **Add Source** (Name, Base URL, Organization, Access Token Name, Access Token, TrakHound Package Manager), **Edit**, **Copy** (opens a new Source with the same settings, including the Access Token), **Remove** and **Test Connection** |

**TrakHound Public:** on first run the Instance adds the TrakHound public Package Server (`https://packages.trakhound.com`, Organization `Public`) as a source. If it was removed, add it back with **Add TrakHound Public** (shown when there are no sources) or **Use TrakHound Public** in the Add Source dialog, which fills in the form so an Access Token or TrakHound Package Manager can still be set. Both are hidden while a source with that Base URL and Organization exists.

Installing a package doesn't run it. Add a configuration that uses it (section 2.5).

**TrakHound Package Manager sources:** tick **TrakHound Package Manager** on a source that is a TrakHound Package Manager feed (ex. `https://pm.contoso.com/trakhound/package-manager/api/feed`, with the access token it gave this Instance). The list of sources marks it **Package Manager**. For these sources only, the Instance:

- Reports its installed packages to the Package Manager, so it keeps a record of what each Instance runs for IT reports and to find Instances whose packages differ. It sends every installed version, used or not, with its UUID, content hash, install time, whether a configuration uses it, its verification state, publisher and the repository and commit it was built from, plus the Instance ID, name, host name, TrakHound version and operating system. It reports when the Instance starts, about 30 seconds after packages are installed, updated or removed (once for a group of changes), and on a heartbeat (Settings > Notifications > **Package Inventory**, every 6 hours by default).
- Trusts the publishers the Package Manager verified, so its packages show as `Verified` rather than `Untrusted` (section 3.7).
- Reads the Package Manager's signed list of the builds approved for the Instance, for the `RequireApproval` verification mode (section 3.7.1).

Saving a source applies the change right away. If a report fails, the Instance logs a warning once (ex. "the Access Token was refused (401 Unauthorized)") rather than on every attempt, and sends the packages with the next report. A Package Manager that can't be reached never delays startup or an install.

**Versions and prereleases:** versions are listed in semantic versioning order, so a prerelease (ex. `0.4.5-dev.20261114153000`) comes before its release (`0.4.5`) and after the versions below it. **Latest** is the version `*` selects: the highest stable version, or the highest prerelease if there's no stable version. A prerelease is offered as an update (Updates, the update count and the update notification) only for a package that a configuration includes prereleases for (section 2.5), and the configuration list offers it only to those configurations. Browse shows a package as installed when that version is installed, including a prerelease.

A package's Dependencies show their version and any minimum version (ex. `1.* (>= 1.2.0)`). The Installed section flags a Dependency whose installed versions don't satisfy it, and the Packages health check reports it.

### 2.5 Config

Config (`/_admin/configuration`) holds everything a deployment carries: the configurations that run packages (Apps, Api, Functions, Workers, Services, Routers and Identity providers) and the Groups that organize them, **Access** (Security Profiles, Zones and the Zone Map; sections 2.7 and 2.7.1) and **Instance** (the Instance Settings, section 2.9, and the Variables, section 2.5). Each list shows a configuration's Status: Running, Not loaded or Package missing. A workspace (section 2.6.2) has the same pages, except Security Profiles.

To add a configuration:

1. Open the category, ex. **Apps**, and click **Add App**.
2. Choose a package, either **From Source** or **Installed**. Tick **Use Latest Version** to follow updates. It uses the newest stable version, or a prerelease if no stable version is installed. Also tick **Include Prereleases** to use the newest version even when it's a prerelease (the configuration's version is saved as `*-*` instead of `*`). The list shows these configurations as **Latest (prereleases)**. Saving a configuration whose version isn't installed downloads the version it selects from the source.
3. Fill in **General**: Name, Description, and the fields the category uses, such as Route, Serve over HTTP, Function ID, Volume ID, Group or Router.
4. Fill in **Settings** (provided by the package) and **Parameters** (name and value pairs; secret values stay hidden).
5. Click **Add**. Later, use **Edit** to change it or **Remove** to delete it, optionally with its package.

**Groups** (`/_admin/configuration/groups`) organize Apis, Apps, Functions and Workers, ex. the Api, App, Functions and Workers of one line. Click **Add Group** and give it an ID (Modules and Zones refer to it by ID), then on its page set a Name, Description and **Router**. The Group's Router is used by its Modules that don't name their own; a Zone with a Router still overrides both. Add Modules from the Group's page (**Add Module**) or pick the Group on a Module's configuration page. The Group's page also lists the Zones that refer to it. Removing a Group takes its Modules out of it. Groups are stored in the `group` configuration category.

**Routers** (`/_admin/configuration/routing`) is a diagram editor for Routers. A Router sends each Request to the Targets (Services, or other Routers) of the Routes whose Pattern matches the kind of Service (`Blobs`, `KeyValues`, `Messages`, `MessageQueues`, `Tables` or `TimeSeries`, or `*` for all of them). Requests that don't name a Router use the one named `default`.

A Route's Targets are tried in order. The Request goes on to the next Target only when a Target's result is one it **Falls Back On**: by default `NotFound`, `NotAvailable`, `Timeout` and `InternalError`. Requests that were wrong or not allowed (ex. Bad Request or Forbidden) aren't sent on. A Target can also fall back on `Empty` (no results), in which case the empty result is returned if no later Target has any. A Target that doesn't handle the Request is always skipped. For example, a `KeyValues` Route with a cache Service followed by a database Service reads from the database when the cache doesn't have the key.

1. Click **Router** or **Route** in the list on the left, or a Service or Router to add a Target. With an item selected, the new item is connected to it (ex. click a Service while a Route is selected). Items can also be dragged onto the canvas.
2. To connect two items, drag from the right edge of one to the left edge of the other. Only allowed connections can be made: Router → Route → Target.
3. A Service Target shows the package of its Service. Select an item to edit it in the panel on the right (Name, Patterns, the order of a Target and the results it **Falls Back On**, ...). A Route's Pattern box lists `*` and the kinds of Service to pick from; you can also type a Regular Expression. Patterns are case-sensitive, so a Pattern that matches none of them (ex. `keyvalues`) is marked with a warning. With nothing selected, the panel lists the problems found. Errors (ex. two Routers with the same Name, a Route without a Pattern, or Router Targets that loop) must be fixed before saving. Items that aren't connected to a Router aren't saved.
4. Click **Save**. Only the Routers that changed are written. **Discard** loads the saved Routers again, **Arrange** lays the items out in columns and **Fit** zooms to show them all.

Apis, Apps, Functions, Workers and Identity Providers each name the Router their Service calls go through. An Api, App, Function or Worker that doesn't name one uses the Router of its Group (the `group` configuration its `groupId` names), and otherwise the `default` Router. An Identity Provider that doesn't name one uses the `default` Router. A Zone with a Router sends its Members' calls through that Router instead. Each Router node shows how many Modules use it, and a selected Router lists them under **Used By**. Modules find a Router by its ID or its Name, so renaming or deleting a Router they use by Name stops their Service calls: the page warns about it before you save, and about Modules that name a Router that doesn't exist.

**Map** shows where Service calls would go, including changes that aren't saved yet: the Modules on the left, then the Routers they use, their Routes, the Targets in the order they are tried, and the Services. Each Route is split into the kinds of Request its Patterns match (ex. a `*` Route into `Blobs`, `KeyValues`, `Messages`, `MessageQueues`, `Tables` and `TimeSeries`), and each kind is linked only to the Targets that handle it. A kind that no Target handles is marked with a warning. Dashed lines go to another Router. Pick a kind of Request (ex. `KeyValues`) to fade the Routes and Services it can't reach, and click a node to follow its Requests. A selected Router or Module lists, for each kind, the Targets a Request is tried against and the ones that are skipped (ex. a Service that doesn't handle `KeyValues`). Routers and Services that nothing uses are marked **Not used**. What a Service handles is read from the running Service, so a Service that isn't running shows **Kinds unknown**. Calls a Module sends to a different Router in its own code aren't shown.

**Variables** (`/_admin/configuration/variables`) holds values shared by every configuration in the profile. Reference one in a Parameter value as `${name}`, ex. `${dataPath}/files`. `${env:NAME}` reads an environment variable of the Instance process, and `$${name}` is the literal text `${name}`.

- Add a variable with a Name and Value, then click **Save** (or **Discard**). Each variable shows how many configurations use it, or **Unused**; the page also lists references to variables that aren't defined.
- Variables named like a secret (containing `password`, `token` or `secret`, or ending in `key`) are encrypted when saved and never shown again; type a new value to replace one.
- Variables are read when the Instance starts. After saving, the page offers **Restart Instance** to apply them.
- They are stored in `variables.config.yaml` in the configuration folder and included in Deployments (section 2.6).

### 2.6 Deploy

Deploy (`/_admin/deployments`) copies this Instance's configurations and packages, and optionally its package data (Volumes and Package Variables), to other Instances.

| Section | What you can do |
| --- | --- |
| Create | Choose **Full** or **Incremental**, a Version and Description, the configuration profile, the **Configurations** to include (all, or only some categories, ex. Api, Functions, Services, or the profile's Instance Settings), which packages (all installed, only those used by configurations, or **None**), which **Volumes** and **Package Variables** to include (none by default) and whether to **Include Secrets** (the admin login, Users and API Keys, and secret values; without them the target keeps its own). **Create Deployment** gives a download link that works for 1 hour. **Recent Deployments** lists the deployments created on this Instance, with **Download** |
| Install | Drop a deployment file, review the changes (including the licenses a deployment for this device adds, and an error if it's for another device), optionally back up first, then click **Stop Instance and Install**. **Installed at Startup** lists the deployments installed when the Instance started (section 2.6.1) and any files waiting for the next start |
| Backups | **Restore** a backup (stops the Instance) or **Delete Backup** |
| Workspaces | Create, open, **Export** and delete workspaces, which hold the configurations and settings for another Instance (section 2.6.2) |

To copy only settings from one Instance to another, include only the categories you want and choose **None** for packages. Installing a deployment replaces only the configuration categories it includes, and one without packages doesn't change the package versions on the target.

A deployment includes the profile's Variables (section 2.5), which are kept in `variables.config.yaml` at the root of the profile. They're part of the Instance Settings configurations, so a deployment that leaves out Instance Settings leaves out the Variables too. A deployment made with Include Secrets carries the secret values. Store and send the file securely.

**Volumes and Package Variables:** a deployment can also carry package data, which is useful for settings a package keeps in its Volume. Choose the Volumes (folders in the `volumes` folder, ex. a configuration's ID) and the Package Variables (the values packages keep with `Variables.Set`, one collection per configuration ID) to include.

- Installing adds or replaces the deployment's Volume files. Other files in the target's Volumes are kept, and nothing is deleted.
- Each Package Variables collection replaces the target's collection with the same ID.
- Incremental deployments include only the Volume files and collections changed since the last deployment.
- Backups include the Volume files and collections the install replaces, and restoring a backup also removes the ones it added.
- They're copied as they are. Include Secrets doesn't apply to them, so don't include Volumes that hold passwords or keys unless you would include secrets.

#### 2.6.1 Installing a deployment at startup

The Instance installs deployment files from its `deployments` folder (next to the program files) when it starts, before it reads any configuration. Use this to set up a new Instance with a known set of configurations and packages: the installer's `/DEPLOYMENT` option copies a file there (see the TrakHound Instance Installation Guide), or copy a `.thd` file there yourself and restart the Instance.

- Files are installed in name order (ex. `01-base.thd` before `02-site.thd`). Only files directly in the folder are read.
- Each install creates a backup first, which you can restore from **Backups**.
- If an install fails, the Instance restores the backup and starts with its previous configuration and packages.
- Afterwards the file is moved to `deployments\installed` or `deployments\failed`, and a notification shows the result.
- Each file is installed only once. The Instance records its hash in `deployment.startup.json` in the configuration folder, so the same file isn't installed again even if it is copied back. To retry a failed file, fix it and install it from the **Install** tab.

To read deployments from another location, set the `TRAKHOUND_DEPLOYMENT_PATH` environment variable (or the `--deploymentPath` option) to a folder or to a single `.thd` file. A single file is left where it is (ex. a read-only Docker mount); the record keeps it from being installed again.

Startup deployments are best made without Include Secrets. Put values that differ per site (ex. host names or connection strings) in Variables, and set them on each Instance after it starts.

#### 2.6.2 Workspaces

A workspace holds the configurations, Variables, Zones and Instance Settings for another Instance, so you can set it up without changing this one. Create one on Deploy > **Workspaces** with an ID (letters, numbers, `_`, `.` and `-`), then open its **Configuration**, **Zones** or **Settings**.

- A workspace's pages are the same pages as this Instance's, at `/_admin/workspaces/<id>/...` (ex. `/_admin/workspaces/site-b/configuration`). A banner at the top shows which workspace you're editing.
- Nothing in a workspace runs on this Instance. Saving a configuration doesn't install its package, removing one doesn't uninstall a package, and the Module status shows **Package Missing** for packages that aren't installed here.
- The pages only show what belongs to the workspace : Settings leaves out this Instance's ID, Health checks, Log History status and package verification results, and the Security menu shows only Zones and the Zone Map.
- Workspaces are kept in the `workspaces` folder next to the `config` folder. They aren't part of this Instance's deployments or backups.
- Secret values (ex. passwords) saved in a workspace are encrypted with this Instance's key, like this Instance's own configurations.

**Export** creates a deployment file from a workspace, to install on the other Instance (section 2.6, or at startup, section 2.6.1).

- **Target Profile** is the configuration profile the workspace replaces on the target (usually `default`).
- The deployment replaces the target profile's Apis, Apps, Functions, Workers, Services, Identity Providers, Routers, Groups and Zones with the workspace's, including the ones the workspace has none of. The target's other configurations (ex. Security Profiles) are kept.
- The workspace's Variables and Instance Settings (Settings page) are included, so a deployment can set up an Instance without an Admin interface (ex. a device with no screen). Its Instance and Log settings replace the target's, including its port and address. For settings that differ on each device, use **Variable** on the Settings page (section 2.9). A workspace that never saved its Settings carries none, and the target keeps its own.
- The dialog lists the package versions the configurations use. A version installed on this Instance is copied, and one that isn't is downloaded from the selected Package Source (nothing is installed here). For "latest", the Package Source's latest version is used. Their dependencies are added when exported, and the target verifies every package when it installs them. Export isn't available while a package isn't installed here or on the Package Source.
- Installing adds or updates these package versions on the target. Its other package versions are kept.
- **Include Secrets** works as it does for this Instance's deployments: without it, secret values are left out and the target keeps its own.

**Licenses and Target Device ID.** Licenses are issued for one device, so a deployment only carries them when it's for one device:

- **Licenses** on a workspace holds the license files you received for the devices it's exported for (paste them or choose a file). One file can hold licenses for several devices. They're kept in the `workspaces\.licenses` folder, aren't used on this Instance, and aren't part of the workspace's configuration.
- Enter the device's ID in **Target Device ID** when you export (`TrakHound.Instance device-id` shows it on a device without the Admin interface; TrakHound Instance Installation Guide, section 6.4). The deployment then includes the workspace's licenses for that device (and any licenses for any device), and the dialog shows how many.
- A deployment with a Target Device ID can only be installed on that device. On another device the install fails before anything is changed (Install section, or at startup, where the file is moved to `deployments\failed`).
- Installing adds its licenses to the device's own. The device's other licenses are kept.
- A deployment never sets a device's ID. Without a Target Device ID, a deployment is for any device and carries no licenses.

**Devices.** To set up several devices from one workspace, list them on its **Devices**: a Label (ex. "Line 3 Press") and the Device ID for each, and the values of the workspace's Variables that differ on that device (ex. a PLC address). An empty value uses the workspace's, and secret values are encrypted like the workspace's Variables. The list is kept in the `workspaces\.devices` folder.

- **Export** then offers **A deployment for each device**. Choose the devices, and it creates one deployment for each, for its Device ID, with its licenses and its Variables. They're downloaded together in one `.zip` file, named `<deployment>-<label>-<version>.thd` inside.
- Everything else (configurations, settings and packages) is the same in each. A device without a license for a product the workspace has licenses for is shown in red.
- Variables are used by configuration Parameters (ex. `${plcAddress}`) and by Instance Settings set to a **Variable** (ex. the port as `${port}`; section 2.9).

### 2.7 Security

Security (`/_admin/security`) manages who can sign in to and call this Instance: Identity Providers, Users and API Keys, the Admin Accounts for this Admin interface, Trusted Publishers, and the tools (What is Public?, Access Check, Audit Log). The rules for who can reach what, the Security Profiles, are in Config > Access with the Zones, since deployments carry them.

Sign in with an Identity Provider's username and password (ex. LDAP) is locked for a while after repeated wrong passwords, the same as the Admin sign in (section 2.2). While it's locked, the sign in page returns `429 Too Many Requests` with a `Retry-After` header and the Identity Provider isn't called. An Identity Provider can use its own sign in page with the same lockout (see the developer guide). When that page is in an App, the response is a redirect to the page with a `Retry-After` header, not a `429`. Sign ins, failures and problems with a sign in page are written to the `security` log.

APIs and Apps that no Profile covers are public. System Resources (`system:*`) and Service operations that no Profile covers can only be used from the same machine as the Instance: requests from other machines get `403 Forbidden`. To use them from another machine (ex. a client, another Instance's HTTP Service, or an MCP client), add a Profile that covers them, or allow **Unprotected Remote Access** in Settings > Interfaces on a trusted network. Behind a reverse proxy on the same machine, set Trusted Proxies (section 2.9) so requests are seen as coming from the client, not the proxy.

| Page | What you can do |
| --- | --- |
| Security Profiles (Config > Access) | **Add Profile** (Profile ID, Description, Identity Provider, Priority). Open a profile to add Assignments, which grant roles and permissions on resources, then **Save**. An Identity Provider of **None** makes the profile's resources public, whatever other profiles require, ex. an App's sign in page |
| API Keys | **Create API Key** with a Name, Description, Roles (pick from the roles that Profiles use, as for Users) and Expiration (30 days, 90 days, 1 year or Never). The key is shown only once, so copy it then. **Revoke** a key to disable it |
| Users | **Add User** with a Username, Name, Password and Roles, for the built in Users identity provider (section 2.7.2). **Edit** a user to change their Name, Roles or Password, or clear **Enabled** to stop them signing in. **Sign Out** a user, or **Sign Out All**, to make them sign in again. **Delete** removes a user. **Sign In Page** chooses the page users sign in on |
| Providers | Identity providers: API Keys and Users (built in), OpenID Connect and LDAP. **Add Identity Provider**, **Configure**, and **Test Sign In**. The page shows the Redirect URI to register with your provider |
| Admin Accounts | The accounts that sign in to the Admin interface and their roles (section 2.7.3). Only an Owner sees this page |
| What is Public? | Every endpoint that works without signing in, and why. **This machine only** lists the System API endpoints that work without signing in only from the same machine |
| Access Check | Test whether an identity, roles or an API key can reach a resource, and see the reason |
| Zones (Config > Access) | Limit what packages can reach through the Instance (section 2.7.1) |
| Zone Map (Config > Access) | The Zones drawn as a diagram (section 2.7.1) |

**Audit Log** (Security > Audit Log) records changes to packages and configuration files: when, what changed, who made the change and the file hash before and after. File contents aren't stored, since configuration files can hold secrets. The first start adds one **Baseline** record, and changes are recorded from then on.

| Source | Meaning |
| --- | --- |
| Admin | Made in the Admin interface, with the signed in username |
| SystemApi | Made through the System API (or MCP Server), with the identity if the caller had one |
| Module | Made by a package using a Module Token |
| Instance | Made by the Instance itself, ex. a package update |
| Detected | Found by comparing the files on disk, at startup and when files change, so who made the change isn't known. This includes files edited directly, deployments, and settings saved without going through the configuration profile (ex. Security Profiles and Log settings) |

Each record includes the hash of the record before it. **Verify** checks this chain and reports the first line that fails, which shows if a record was edited or removed. It can't detect records removed from the end of the file, or the whole file being replaced, by someone with full access to the machine. To protect against that, copy the log somewhere else regularly. **Download** saves the whole log, whatever the search shows.

The log is `audit.jsonl` in the configuration profile folder, and `audit.snapshot.json` holds the last known state. Records are never removed automatically, and Log History settings don't affect them. Deployments and backups leave these files out. If a record can't be written, the error is logged and the change still goes through. The System API serves the log at `/_instances/audit` (`take`, up to 10,000, and `search`) and the check at `/_instances/audit/verify`, controlled by the `system:audit` resource.

#### 2.7.1 Zones

Profiles control what people and API keys can reach. Zones control what *packages* can reach: a Zone lists Member Modules (Apis, Apps, Functions, Workers and Services) and Rules for what they can call through the Instance: APIs, Functions, Workers, Service operations, Volumes and management. A call that no Rule allows is denied. A Module that isn't in any Zone isn't restricted.

Zones guard against a package reaching something by accident, ex. one in development or written with an AI coding tool. They aren't a security boundary against deliberately hostile code: a package can still use the file system, network or processes directly. The Zone page flags Members whose packages do this (**Reaches outside its Client**).

A package that runs code in its own process (ex. a Python worker) can give that process a Module Token, so its calls to the System API are made as the package and its Zone applies to them too. Module Tokens are only accepted from the same machine and stop working when the Instance restarts. Behind a reverse proxy on the same machine, list the proxy in **Trusted Proxies** (section 2.9), or requests through it count as coming from the same machine.

Open Config > **Zones** and click **Add Zone**, then on the Zone's page:

| Section | What you can do |
| --- | --- |
| Settings | Name, Description, **Mode** and **Service Router**. **Enforce** denies calls that aren't allowed; **Audit** allows them but records them as "Would Deny", to learn what a package needs before enforcing. A Service Router sends every Service call the Members make through that Router (ex. one pointed at test data), whatever Router they ask for. **Members can call each other without a Rule** is on by default |
| Members | **Add Member**: pick a Module or a Group (`group:<id>`, every Module in the Group, including ones added to it later), or enter a pattern (ex. `worker:dev-*`). A Module belongs to only one Zone: the first by ID, and a Zone that lists the Module itself comes before a Zone that lists its Group |
| Rules | **Add Rule**: a Resource (ex. `api:entities:/objects/*`, `function:send-report`, `service:redis:key-values:*`, `volume:files:/reports/*`), its Permissions (`read`, `write`, `run`; none allows any) and why it's needed. A Resource ending in `*` matches by prefix |
| Allowed Callers | The Modules (or `zone:<id>` for every Member of another Zone, or `group:<id>` for every Module in a Group) that can call this Zone's Members. Empty allows any Module |
| Activity | Calls that were denied, or would be in Audit Mode, with counts. **Grant** adds a Rule for exactly that Resource, **Grant All** a broader one, **Allow Caller** adds the caller to Allowed Callers, and **Dismiss** removes the entry |
| Test a Call | Choose a Caller, Permission and Resource, then **Check** to see whether it's allowed and why |

A good way to start is to add a package to a Zone in **Audit** mode, use it normally, grant what shows in Activity, then switch to **Enforce**. A denied call returns 403 ("Zone Access Denied") to the package and is logged.

**Zone Map** draws Callers on the left, Zones with their Members in the middle, and what their Rules allow on the right. Denied calls (red) and Audit calls (amber) appear as they happen; click a line to see its calls and Grant them. Drag a Module from **Not in a Zone** onto a Zone to add it, drag a Member to another Zone to move it, or onto Not in a Zone to remove it. **Arrange**, **Fit** and **Save Layout** position the diagram, and clicking a Zone's name opens it.

Each Module's configuration page shows the Zone it's in. Zones are stored in the `zone` configuration category and can be included in Deployments.

An admin account's role can be limited to Zones, so the account only manages those Zones' modules (section 2.7.3).

#### 2.7.2 Users

The built in Users identity provider (ID `users`) signs people in with a username and password kept on the Instance. Use it for Apps that only need a few users or have low security needs. For single sign on, MFA or many users, use an OpenID Connect or LDAP identity provider.

1. On Security > **Users**, **Add User** with a username, an optional name, a password (at least 8 characters) and roles. Under the Roles box are the roles that Profiles for this identity provider already use: click one to add or remove it. A role with a shield gives access to the System API or Services, and only an admin account that is an Owner can give it (section 2.7.3).
2. On Config > **Security Profiles**, add a Profile with the `users` identity provider and Assignments that grant those roles permissions on the App or API.
3. People open the App and sign in on the Instance's sign in page. Wrong passwords are locked out the same as other identity providers.

Usernames match without regard to case and can't be renamed. Changing a user's password or roles, disabling them or deleting them ends their sessions, so they sign in again (with the new roles). **Sign Out** on a user's row, or **Sign Out All** in the toolbar, ends sessions without changing anything else. Sign outs take effect on the user's next request. They don't affect API keys, other identity providers or the Admin interface. Changing only the name doesn't: the new name shows the next time they sign in. Users can't reset or change their own password; an admin sets it with **Edit** on the Users page.

**Sign In Page** (in the Users toolbar) chooses how the sign in page looks:

- **Product Name** and **Logo** add a header to the Instance's own form. The logo is a path in the Instance that doesn't require signing in, ex. `_init/assets/logo.svg`.
- **App Page** uses a page in one of your Apps instead, ex. `/sign-in`. The page must be public, so if a Profile covers the App, make the page public with a Profile that has no identity provider (see the developer guide, section 4.6). The dialog warns when no App page matches the route or the page requires signing in. In either case the Instance's own form is shown, so users can always sign in.

**Preview** opens the saved page in a new tab. These are the same settings (`signInProduct`, `signInLogo` and `signInRoute`) that a configured identity provider sets in its parameters.

Each user is a file in the configuration profile's `security/users` folder, with a salted PBKDF2 hash of the password (never the password). The sign in page settings are in `_settings.yaml` in the same folder. On Linux and macOS the files can only be read by the Instance's user.

#### 2.7.3 Admin accounts and roles

Admin accounts sign in to the Admin interface. Each account has one or more roles, and a role says which areas the account can view and which it can change. An account with several roles can do what any of them allows. Roles apply to the Admin interface only: the System API and the MCP Server are controlled by Security Profiles.

Roles guard against changing something by accident, or something you weren't asked to manage. They aren't a barrier against a person who can install packages, since a package is code that runs in the Instance.

These roles are built in. An Owner can add more (see **Admin Roles** below).

| Role | What it can do |
| --- | --- |
| Owner | Everything, including the Admin Accounts and Admin Roles |
| Packages | Install, update and uninstall packages. Configure Apps, APIs, Functions and Workers, and use them in the Explorer (run Functions, start and stop Workers, Volume files). View the Instance, logs, package sources and Groups. Nothing else: no Services or their data, Routers, Settings, Variables, Deploy, Security or Licenses |
| Security Operator | Add and change Users and API Keys. View Security Profiles, Identity Providers and the Audit Log, but not change them |
| Viewer | View everything except the Admin Accounts. Can't change anything |

- A Packages account can only install a package of a kind it can configure, ex. not a Service or Identity package. If a package it installs depends on one of those, the dependency is installed but gets no configuration, so it doesn't start running.
- A Security Operator can't hand out access to the Instance itself. A role that a Security Profile gives permissions on the System API (`system:*`) or on Services (`service:*`) can only be given to a User or an API Key by an Owner. The same goes for changing a User or Key that already has such a role: setting its password, disabling, deleting or revoking it. If a Profile gives those permissions to every identity (the role `*`), every User or Key of that identity provider counts.
- For the same reason, only an Owner can add, change or remove a Security Profile assignment on a System or Service resource (including a pattern that matches them, ex. `*`), or delete a Profile that has one.
- The pages of a Deploy **Workspace** (section 2.6.2) need access to Deploy, whichever page it is, since they edit a deployment and not this Instance.

On Security > **Admin Accounts** (Owners only):

| Action | What it does |
| --- | --- |
| **Add Account** | A username (at least 4 characters), an optional name, a password (at least 4 characters) and one or more roles. A role can be limited to Zones (see below) |
| **Edit** | Change the name, the roles or the password, or clear **Enabled** to stop the account signing in. A new password or a disabled account is signed out straight away. A change of roles applies to what the account does next, without signing in again |
| **Sign Out** | Ends the account's sessions, so it signs in again |
| **Delete** | Removes the account and signs it out |
| **Roles** | Opens Security > Admin Roles |

On Security > **Admin Roles** (Owners only), **View** shows what a built in role allows, and **Add Role** creates your own:

- A name and an ID (lowercase letters, numbers and `-`; it can't be changed later).
- For each area of the Admin interface: **None**, **View** or **Change** (Change includes View). The Admin Accounts area is only for the Owner role, so a role you add can never manage admin accounts or roles.
- **Roles it can give Users and API Keys**: role names, or patterns ending with `*` (ex. `operators, line-1-*`). Leave it blank for any role. An admin with the role can then only give those roles, and can't change a User or Key that has a role outside the list. If the admin holds another role that can change Users or API Keys without a list, that role's wider access applies.

**Edit** changes a role you added, and the change applies to what its accounts do next. **Delete** removes one that no account holds. The built in roles can't be changed. Roles you add are files in `security/admin-roles` in the configuration profile folder, and changes to them are in the Audit Log.

**Limiting a role to Zones.** When you tick a role on an account, a box under it takes Zone IDs (ex. `dev, line-1`). Leave it blank for the role to apply everywhere. With Zones, the role only applies to:

- The Apps, APIs, Functions and Workers that belong to those Zones (section 2.7.1), in the Explorer and in Config. The lists only show those, and another module's configuration page shows **No Access**.
- The Service data and Volume files those Zones' Rules allow, which is what the Zones' own modules can reach.

It gives nothing on the other areas, even if the role includes them: the Instance pages, Logs, Packages, Groups, Routers, Settings, Variables, Deploy, Security and Licenses. So an account that only has roles limited to Zones can't install packages or change the Zones it is limited to. To let it view more, also give it a role that isn't limited, ex. Viewer.

- A new configuration must land in one of the account's Zones, or saving it is refused: give it an ID the Zone lists (ex. a Zone member `worker:dev-*`), or put it in a Group the Zone lists. Taking a module out of that Group is refused the same way.
- A module belongs to one Zone (section 2.7.1). A module that isn't in any Zone is outside every limited role.
- A Zone ID that doesn't exist gives nothing until a Zone with that ID is added. The page warns you when you save one.
- The Owner role can't be limited, and neither can a role with nothing about a module (ex. Security Operator).
- By its address, a limited account can still open the Explorer page of a module outside its Zones and read what it shows. It can't change or run it, and can't open its configuration page or read Service data or Volume files outside its Zones.

There is always an enabled Owner: the only one can't be deleted, disabled or given other roles. If no Owner can sign in (ex. a forgotten password), run `set-admin` on the machine the Instance runs on (section 3.2).

What a restricted account sees:

- Areas and pages its roles don't cover aren't in the navigation. Opening one by its address shows **No Access**.
- On a page it can view but not change, the buttons that change something (ex. Add, Edit, Save, Delete, Install) aren't shown.
- If an action is still shown and the account can't use it, using it shows **Not Allowed** and nothing is changed. This can happen on a page that covers more than one area, ex. the Volume files on a Function's page.
- Click your name in the header to see your roles and change your own password. Changing it signs you out everywhere.

Changes to admin accounts are in the Audit Log with the Owner who made them. Accounts created with `set-admin` or at startup show as **Detected**.

### 2.8 Licenses

Licenses (`/_admin/licenses`) manages product licenses. They are checked on this server without contacting the publisher, so a server without internet access can be licensed: request the license from another computer and bring the license file over.

- Copy the **Device ID** (in the area header) when you request a license for this server. It comes from the machine name, so renaming the server needs new licenses. On an Instance without the Admin interface, run `TrakHound.Instance device-id` instead (TrakHound Instance Installation Guide, section 6.4).
- **Add License** with a Publisher ID, Product ID, License Code and an optional Description, or **Import Licenses** from a license file. A license issued for a different Device ID is refused.
- **Request a License** opens the TrakHound license request page, for a trial or subscription. The Licensing Guide is linked from the page when there are no licenses yet.
- The **Licenses** section shows each license's Product (with its Description), Publisher, Status and Expires date, with **Edit Description**, **Renew** (replace the code with a renewed one, keeping its assignments and description) and **Remove**.
- A Description is a label for telling licenses apart (ex. "Line 3 machines" or an order number). A license file can include one; importing a license that is already here doesn't replace a description you set. The Assignments' license list shows it instead of the masked code.
- The **Assignments** section shows what each license is used by, up to the license's limit; **Unassign** frees it.
- Licenses are saved in the configuration profile's `licenses` folder, one file per publisher. Deployments leave that folder out (a license is only for its device) and never remove licenses. A deployment exported from a workspace for this device's ID adds its licenses here (section 2.6.2).

In a container, set `TRAKHOUND_DEVICE_ID` to a stable value (ex. the deployment's name) before requesting licenses, since the container's hostname changes when it is recreated. It is only used when the server is running in a container; otherwise the page shows a warning and uses the machine name.

### 2.9 Settings

Open Config > **Settings**. Change values, then click **Save Changes** (or **Discard**). Settings marked as needing a restart apply after **Restart** (section 2.3.1).

| Section | Settings |
| --- | --- |
| General | Name and Description. Shows the Instance ID, version, configuration file and profile |
| Interfaces | HTTP Address, Port and Base Path; Trusted Proxies (the reverse proxies whose `X-Forwarded-For` and `X-Forwarded-Proto` headers are used, applied when saved; see the Installation Guide, section 5.3); Admin Access > Allowed Addresses (section 3.2); Separate Admin Port, Admin Port and Admin Address (not under IIS or Docker); Serve Apps over HTTP; Serve APIs over HTTP; Initialization Page and Custom Initialization Page (section 3.6); Error Page; Enable System API; Unprotected Remote Access (section 2.7); MCP Server (section 2.10) |
| Logs | Provider, Path and minimum levels; Retention, Max Size and Archive settings; **Run Maintenance** |
| Rate Limits | Limit Requests, and the Requests per Window and Concurrent Requests for the APIs, the System API and MCP Tools (section 3.5). Applied when saved |
| Notifications | Check for Package Updates, and how often. Package Inventory **Heartbeat** and its interval: how often the installed packages are reported to TrakHound Package Manager sources when nothing has changed (section 2.4) |
| Metrics | Collect Metrics, per-area toggles (API, Apps, HTTP, Functions, Services, Workers, Streams, MCP), how many Recent HTTP and API Requests and closed API Streams to keep (0 keeps none, up to 10,000 each) and CPU usage by Module (Workers, Functions, APIs, Apps and the Admin Interface). Applied after a restart |
| Health | **Critical** and **Ignore** lists, one pattern per line, `*` as a wildcard. Add items from the current checks list |

These are the same settings as in `instance.config.yaml` and `logs.config.yaml` (section 3).

**Variable** next to a setting (Name, Description, Address, Port, Base Path, Max Request Size, Admin Port and Admin Address) sets it from a Variable instead of a value, ex. `${port}` or `${env:PORT}`. Use it in a workspace for settings that are different on each device: each device's Variables (section 2.6.2, Devices) or environment give the value. A Variable that isn't defined leaves the setting at its default, with a warning in the log. Click **Variable** again to use a value.

### 2.10 MCP Server

The Instance can serve an MCP (Model Context Protocol) Server at `/_mcp`, so AI clients such as Claude can use the Instance: list and run Functions, read Logs, query Tables, read and set Key Values, read and write Time Series, publish and wait for Messages, read Volume files, and describe the Apis. It's off by default.

1. Turn it on in Settings > Interfaces > **MCP Server** (the System API must be on too). It applies when saved.
2. Connect a client to `http://<host>:<port>/_mcp`. The MCP Server is served on the HTTP port, and on the Admin port when there is one. For example, with Claude Code:

   ```
   claude mcp add --transport http trakhound http://localhost:8472/_mcp --header "X-API-Key: thk_..."
   ```

Access works like the rest of the System API (section 2.7): Identity Profiles decide who can use it. **Without a Profile, every Tool, including running Functions and SQL, can be used by anyone on the same machine as the Instance**, and by anyone who can reach it when Unprotected Remote Access is allowed. Add a Profile before connecting clients from other machines.

| Resource | Permission | Allows |
| --- | --- | --- |
| `system:mcp` | read | Connecting, and the Tools that only read |
| `system:mcp` | write | Also the Tools that change something: `functions_run`, `functions_cancel`, `tables_query` (runs any SQL), `keyvalues_set`, `timeseries_write` and `messages_publish` |

Each Tool also needs the Permission for what it uses, for example `system:functions` (write to run), `system:logs`, `system:volumes`, `system:api-information` or `system:instance` (for `instance_boots` and `instance_boot`, which read the Boots, section 2.3.1). Key Value, Table, Time Series and Message Tools are authorized by the Service, the same as the Services API. Clients are only shown the Tools the caller can use.

**Api Tools:** API endpoints can be Tools too. Choose which per API in Config > Api > **MCP Tools** (`mcpTools` in the API configuration):

| Value | Tools |
| --- | --- |
| `Off` | None |
| `Marked` (default) | The endpoints the package author marked for AI clients (`[ApiMcpTool]`), with names and descriptions written for them |
| `All` | Every endpoint. Unmarked ones are named after their method and described by their route; GET and stream endpoints are read only, and the rest are treated as changing data (clients ask before calling them) |

**Included MCP Tools** (`mcpInclude`) and **Excluded MCP Tools** (`mcpExclude`) limit them, one pattern per line. A pattern is a route relative to the API's route, with `*` matching anything (ex. `/orders/*`), optionally after a method list and a space: `GET *` (every GET), `GET,HEAD /parts/*`, `STREAM *` (stream endpoints) or `DELETE *`. A pattern without methods matches every method. **Read-only Tools only** (`mcpReadOnly`) is on by default, so only the Tools that don't change anything are served: GET and stream endpoints, plus endpoints the package author marked read only (ex. a POST search). Turn it off (`mcpReadOnly: false`) to also serve endpoints that change data, such as PUT, POST and DELETE. The patterns still apply on top of it. Use them with `All` on large APIs: AI clients choose tools less reliably when there are many. Tool names start with the API's route (ex. `api_orders_get_order`). Calling one is authorized like an HTTP request to the endpoint (its `api:...` resource), and endpoints that change something also need `write` on `system:mcp`. Tools follow the loaded APIs, so clients see changes the next time they list the Tools.

**Resources and prompts:** besides Tools, the MCP Server offers:

- Resources: the Instance's `llms.txt` (below) as `trakhound://llms.txt`, and the OpenAPI document of each Api as `trakhound://apis/<apiId>/openapi`. Like the Api Catalog, they need `read` on `system:api-catalog` and only include the endpoints the caller can call.
- Prompts: starting points that tell the client which Tools to use: `instance_overview`, `diagnose_function`, `explore_api` and `query_tables`.

**llms.txt:** the Instance serves `/llms.txt` (https://llmstxt.org), a Markdown index for AI agents: what the Instance is, how to connect (the MCP Server and API keys), and each Api with its endpoints, descriptions and OpenAPI document link. It's generated for each caller from the Api Catalog, so it's served and protected like `/_developer/catalog` (`system:api-catalog`, on the HTTP port, when Apis are served over HTTP).

Diagnostics > Metrics > **MCP** shows Tool calls, errors, denied calls, durations and the clients that connected.

To stop an AI client that loops or retries quickly from slowing down the Instance, turn on Rate Limits (section 3.5). Tool calls over the limit return a Tool error that says when to retry, and count as denied calls in the MCP Metrics.

### 2.11 Api Catalog and API Explorer

Developers who build applications on the Instance's APIs usually can't open the Admin interface, and the System API exposes every API with internal details. The Api Catalog lets them see only what they can use. It's served on the HTTP port:

| Route | Returns |
| --- | --- |
| `/_developer/catalog` | The APIs served over HTTP, with only the endpoints the caller (signed-in session, API key or Bearer token) is allowed to call, and which of them need authentication |
| `/_developer/openapi/<apiId>` | That API's OpenAPI document, filtered the same way, with the server URL and the API key and Bearer security schemes |

Access is controlled by the `system:api-catalog` resource (section 2.7).

The System API itself is described by OpenAPI documents too, served with the System API (on the Admin port when there is one) and controlled by the `system:instance` resource: `/_instances/openapi` for the whole System API, `/_instances/openapi/areas` for the list of areas, and `/_instances/openapi/<area>` (ex. `volumes`) for one area.

The **TrakHound.ApiExplorer.App** package is a browser for the Catalog. Install it and add an App configuration (default route `api-explorer`). Developers can browse the APIs, search their endpoints, read each endpoint's parameters, send **Try It** requests from their browser (using their own credentials), copy code samples (cURL, JavaScript, C# and Python) and download the OpenAPI documents.

### 2.12 Welcome page (development Instances)

A new Instance shows nothing at its root (`/`). On a development Instance, install the **TrakHound.Welcome.App** package to show a home page there instead. Its App configuration uses the route `/` by default. Change the route if another App already uses `/`. The package depends on **TrakHound.ApiExplorer.App** (section 2.11), so installing it from Packages also installs the API Explorer and adds its App configuration (route `api-explorer`).

The page shows:

- The Instance's ID, version, operating system and Health, with any failing health checks
- The Apps (with links to their routes), Apis (route, HTTP and MCP Tools), Functions, Services and Service Routers
- The loaded packages (including Workers) and any load errors
- Links to the Admin interface, the API Explorer (when it's configured), `llms.txt` and the MCP Server
- Links to the TrakHound website, the Framework documentation and the Quick Start
- The `dotnet new` commands for the package templates, with `--instanceUrl` set to this Instance

The page doesn't change anything. It reads the System API from the visitor's browser, with the visitor's own sign-in, so each section is shown only to visitors with the `read` permission on its resource (ex. `system:apps` for Apps, section 2.7). Resources that no security profile covers can only be read from the Instance's own machine, so on a new local Instance the developer sees everything and other machines see only the links. Service configuration parameters, which can hold connection strings, are never shown.

The System API must be turned on (Settings > Interfaces). With a separate Admin port, the System API is only served on that port, so the page can't show the Instance's details.

The page is meant for development. Remove the package, or its App configuration, before using the Instance in production.

## 3. Configuring the Instance

Instance settings live in `instance.config.yaml`, in the configuration folder. Edit it with a text editor, save it in place, then restart the Instance. Each save writes a backup to the `backup` folder next to the program files.

You can also change most of these settings in the Admin interface under Settings (section 2.9), which saves them to the same files.

A setting's value can reference a Variable (section 2.5) or an environment variable, ex. `httpPort: ${port}` or `name: ${env:DEVICE_NAME}`. The Instance resolves them when it starts. A reference that isn't defined (or doesn't give a valid value, ex. a port that isn't a number) leaves the setting at its default, with a warning in the log. Saving settings keeps a reference while its setting keeps the value it resolved to; a changed value (ex. `th instance set httpPort 9000`) replaces it.

The Instance ID isn't one of these settings: it's kept in `instance.id` in the configuration folder, which deployments and backups leave out, so a deployment never gives every device the same ID. A file saved by an earlier version has `instanceId` in `instance.config.yaml`; it moves to `instance.id` when the Instance starts.

| Hosting | Configuration folder | Restart |
| --- | --- | --- |
| Windows Service | `C:\ProgramData\TrakHound\Instance\config\default` | `Restart-Service TrakHound-Instance` in an administrator PowerShell, or Services |
| IIS Application | `<site folder>\config\default` | Recycle the site's Application Pool |

### 3.1 Common settings

| Setting | Default | What it does |
| --- | --- | --- |
| `httpPort` | 8472 | HTTP port (Windows Service only) |
| `maxRequestBodySizeMb` | 256 | The largest request body accepted, in megabytes (ex. an upload to an API or a Blob). Larger requests get `413 Payload Too Large`. 0 = no limit. Applied when saved (Settings > Interfaces > Max Request Size) |
| `httpAddress` | All addresses | IP address to listen on, ex. `127.0.0.1` for local access only |
| `adminHttpPort` | 0 (off) | Serves the Admin interface and System API on a separate port, so you can firewall them apart from Apps and APIs. Windows Service only |
| `adminHttpAddress` | Same as `httpAddress` | IP address for the Admin port |
| `adminAllowedAddresses` | Empty (every address) | The client IP addresses or networks (ex. `10.0.0.25`, `192.168.1.0/24`) the Admin interface is served to. This computer (`127.0.0.1`, `::1`) is always allowed. Other clients get `404 Not Found` (section 3.2). Applied when saved |
| `adminInterfaceEnabled` | true | Turns the Admin interface (`/_admin`) on or off |
| `adminAuthenticationEnabled` | true | Requires the admin sign-in. Leave on |
| `adminDefaultTheme` | Dark | Default Admin theme |
| `systemApiEnabled` | true | Turns the System API on or off. The Admin interface's remote tools use it |
| `systemApiUnprotectedRemoteAccess` | false | Allows other machines to use System Resources and Service operations that no Identity Profile covers. When off, they get `403 Forbidden` until a Profile protects the Resource (section 2.7). Only turn it on for a trusted network |
| `mcpEnabled` | false | Serves the MCP Server at `/_mcp` (section 2.10). Needs the System API |
| `appsHttpEnabled` | true | Serves Apps over HTTP |
| `apiHttpEnabled` | true | Serves APIs over HTTP |
| `initializationPageEnabled` | true | Shows a startup page while the Instance starts; when off, clients get 503 until it is ready |
| `initializationPagePath` | Empty (built-in page) | The folder of a custom startup page, relative to the configuration profile folder or absolute (section 3.6). Applied when saved |
| `errorPageEnabled` | true | Shows the error page for unhandled errors and for pages not found within the Admin interface; when off, returns a plain status code. Routes that are not configured (including App pages that don't exist) always get a plain status code |
| `packageUpdateCheckInterval` | 6 (hours) | How often package sources are checked for updates. 0 turns it off |
| `packageInventoryReportInterval` | 6 (hours) | How long after the last report the installed packages are reported again to TrakHound Package Manager sources (the heartbeat, section 2.4). They're also reported at startup and after packages change. 0 turns the heartbeat off |
| `packageVerification` | `Warn` | Checks packages against their manifest and signature when they're installed and when the Instance loads them. `Warn` logs problems and uses the package anyway. `Require` only uses packages whose files match and that are signed by a trusted publisher. `RequireApproval` also only uses the builds a TrakHound Package Manager approved (section 3.7.1). `Off` doesn't check. Applied when the Instance is restarted (section 3.7) |

Example (separate Admin port, local Admin access only):

```yaml
httpPort: 8472
adminHttpPort: 8473
adminHttpAddress: 127.0.0.1
```

### 3.2 Admin accounts

Each admin account is a file in `security/admins` in the configuration profile folder, with the password hashed. The first account, an Owner, is created by the Windows installer, by the `set-admin` command, or from the `TRAKHOUND_ADMIN_USERNAME` and `TRAKHOUND_ADMIN_PASSWORD_FILE` environment variables when the Instance starts (Installation Guide, section 6). It can't be created from the Admin interface, so whoever reaches a new Instance first can't claim it. After that, an Owner adds and changes accounts on Security > **Admin Accounts** (section 2.7.3).

`set-admin` also gets you back in when no Owner can sign in. Run it on the machine the Instance runs on. It asks for the password:

```
TrakHound.Instance set-admin <username> --configurationPath <configuration folder>
```

- With a new username it adds an Owner. The other accounts aren't changed.
- With the username of an account that exists it sets that account's password, enables it and makes it an Owner. Its sessions are signed out.

It takes effect within a few seconds, without restarting the Instance.

Deployments leave the admin accounts out unless they're created with **Include Secrets**. Setting `adminAuthenticationEnabled` to `false` (section 3.1) turns sign in off: everyone who can reach the Admin interface is then an Owner.

The session is an HttpOnly cookie that scripts on the page can't read. It's encrypted with the keys in `.local/data-protection` in the configuration profile folder, so it survives restarts. Signed-out sessions are kept in `.local/admin-sessions-revoked.json` until they would have expired. The `.local` folder belongs to this Instance only: deployments and backups never include it, and installing or restoring one never changes it (earlier versions kept these files in `security`; they move when the Instance starts).

To limit the Admin interface to the computers that manage the Instance, list their addresses in Settings > Interfaces > Admin Access > **Allowed Addresses** (`adminAllowedAddresses`), one IP address or network per line. Other clients get `404 Not Found` for the Admin interface pages, sign in and downloads, the same as when it is turned off. Apps, APIs, the System API and the MCP Server are not affected (Identity Profiles protect the System API, section 2.7). The list applies when saved. If your own address isn't in the list, the Settings page warns you before you save.

- This computer (`127.0.0.1` and `::1`) is always allowed, so you can't lock yourself out: sign in on the Instance's own computer and change the list, or edit `instance.config.yaml` there.
- Behind a reverse proxy, add the proxy to **Trusted Proxies** (Installation Guide, section 5.3). Otherwise every request comes from the proxy, and a proxy on the same computer looks like loopback, so every client is allowed.
- A list with only invalid entries allows only this computer.
- This doesn't replace Admin Authentication. For a firewall rule instead, use a separate Admin port (section 3.1).

### 3.3 Logs

Log history is written to the `logs` folder next to the program files, and you can view it in the Admin interface under Explorer > Diagnostics > Logs. Settings are in `logs.config.yaml` in the configuration folder:

| Setting | Default |
| --- | --- |
| `provider` | File (one JSON Lines file per logger per day); or Sqlite |
| `minimumLevel` | Information |
| `retentionDays` | 30 |
| `archiveMode` | Compress (gzip, still searchable); or Delete |
| `archiveRetentionDays` | 365 |
| `maxSizeMB` | 1024 |

The error page shows a Request ID for unhandled errors. To find the error, search the log history for that ID: it is logged with the request's path and the exception under the `Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware` logger.

When an App page throws an exception, the App shows its error message and a Request ID. The exception is logged with that ID, the page's path and the stack trace under the `Microsoft.AspNetCore.Components.Web.ErrorBoundary` logger. The stack trace is shown on the page only when the Instance runs in the Development environment.

Changes to `provider`, `path` or the levels apply after a restart.

### 3.4 Health checks

Two public endpoints suit load balancers and monitoring:

| URL | Returns |
| --- | --- |
| `/_health` | 200 when Healthy or Degraded, 503 when Unhealthy, with `{"status": ...}` |
| `/_health/live` | 200 while the process is running |

### 3.5 Rate limits

Rate limits stop one caller (a script polling too fast, or an AI client in a loop) from slowing down the Instance, or the databases behind its APIs, for everyone else. They're off by default. Turn them on in Settings > **Rate Limits**; changes apply when saved.

Each caller is counted separately: by Identity (ex. an API key or a signed-in user), or by IP address for anonymous requests. Behind a reverse proxy, add the proxy to Settings > Interfaces > **Trusted Proxies** so anonymous requests are counted by the client's address instead of the proxy's. Calls made inside the Instance (by Functions, Workers and Apps, or with a Module Token) are not limited, and neither are Apps, the Admin interface or health checks.

| Area | Limits |
| --- | --- |
| `api` | HTTP requests to the APIs' endpoints |
| `systemApi` | HTTP requests to the System API (not the MCP Server) |
| `mcpTools` | MCP Tool calls. Listing Tools, Resources and Prompts is not limited |

Each area has three settings:

| Setting | What it does |
| --- | --- |
| `permitLimit` | The most requests in each window (0 = no limit). A caller can use them in a burst; they come back evenly over the window |
| `window` | The window, in seconds (1 to 3,600) |
| `concurrencyLimit` | The most requests running at the same time (0 = no limit). WebSocket and Server-Sent Events streams don't count |

HTTP requests over a limit get `429 Too Many Requests` with a `Retry-After` header (in seconds). They show under that status code in Diagnostics > Metrics > HTTP. MCP Tool calls over a limit get a Tool error instead.

Defaults in `instance.config.yaml`:

```yaml
rateLimits:
  enabled: false
  api:
    permitLimit: 600
    window: 60
    concurrencyLimit: 20
  systemApi:
    permitLimit: 600
    window: 60
    concurrencyLimit: 20
  mcpTools:
    permitLimit: 120
    window: 60
    concurrencyLimit: 4
```

Rate limits protect against mistakes, not attacks. Use a reverse proxy or gateway in front of the Instance to block heavy traffic from the internet.

### 3.6 Custom Initialization Page

While the Instance starts, restarts or is stopped, browsers that request an App are sent to the Initialization Page (`/_init`). It reloads the App every 5 seconds until the Instance is running. You can replace the built-in page with your own, ex. a branded loading screen for kiosk displays.

1. Create a folder with an `index.html`, ex. `init` in the configuration profile folder (`config/<profile>/init`). Put its images and stylesheets in the same folder.
2. Set Settings > Interfaces > **Custom Initialization Page** (or `initializationPagePath`) to the folder: `init`, or an absolute path.
3. Click **Preview the Initialization Page** under the setting to check it while the Instance is running. The preview doesn't reload.

The page is plain HTML: the Instance's stylesheets and Blazor aren't loaded. The Instance reloads it with a `Refresh` header, so it doesn't need a reload script. Other files in the folder are served at `/_init/assets/...` without signing in, so don't put anything private there.

These tokens are replaced in `index.html` (HTML encoded):

| Token | Value |
| --- | --- |
| `{{assets}}` | The path of the folder's files, ex. `<img src="{{assets}}/logo.png" />`. Relative, so it works with a Base Path |
| `{{status}}` | `Starting`, `Stopping`, `Stopped` or `Error` (the Instance didn't start) |
| `{{title}}`, `{{description}}` | The built-in page's text for the status |
| `{{route}}` | The page the browser will load when the Instance is running |
| `{{timestamp}}` | When the page was loaded |
| `{{retrySeconds}}` | How often the page reloads |

To show something different when the Instance didn't start, use `{{status}}` with CSS:

```html
<body data-status="{{status}}">
    <p class="error-only">Contact IT.</p>
</body>
```

```css
.error-only { display: none; }
body[data-status="Error"] .error-only { display: block; }
```

If `index.html` is missing or can't be read, the built-in page is shown, and the setting shows a warning.

### 3.7 Package verification

The Instance checks each package when it's installed and whenever it loads packages. It checks that the files match the package's manifest, and who signed it. `packageVerification` in `instance.config.yaml` decides what happens next:

| Mode | What it does |
| --- | --- |
| `Off` | Packages aren't checked |
| `Warn` (default) | Every package is used. A package that doesn't match its manifest logs a warning. An unsigned package logs an information message |
| `Require` | Only packages whose files match and that are signed by a trusted publisher are used. Anything else is refused and logs an error |
| `RequireApproval` | Everything `Require` checks, and the build must be on the signed attestation list of a TrakHound Package Manager source, so a package copied onto the Instance by hand is refused (section 3.7.1). Only offered when a source is a TrakHound Package Manager |

To turn on `Require`:

1. Add each publisher's public key in Security > **Trusted Publishers**, or with `th trust add <public-key-file> --name <name>`. The list is kept in `security/trusted-publishers.json` in the configuration profile folder. Confirm with the publisher that the key is theirs, since anyone holding the matching private key will be trusted. Changes in the Admin interface apply right away. After `th trust`, click **Verify All**.
   Publishers from a TrakHound Package Manager source (section 2.4) are trusted too. The Instance reads them when it starts and every hour, and keeps the last copy in `security/package-manager-publishers.json` so they're still trusted when the Package Manager can't be reached. Trusted Publishers lists them under **From TrakHound Package Managers** with the source they came from. They can't be changed there: change them on the Package Manager. A publisher you add yourself with the same key is used instead, with your name and package IDs. When the synced publishers change, the Instance checks its packages again. Removing the source, or clearing **TrakHound Package Manager** on it, stops trusting its publishers right away.
2. Click **Verify All** on Packages > Installed. Each package shows `Verified`, `Unsigned`, `No Manifest`, `Untrusted`, `Modified` or (under `RequireApproval`) `Not Approved`, and expanding it shows who signed each version, the commit it was built from and any changed files. Anything other than `Verified` would be refused. Settings > **Package Verification** lists them when you choose `Require`.
3. Set the mode in Settings > **Package Verification**, or with `th instance set packageVerification Require`, then restart the Instance.

**Before installing:** Packages > Browse shows who signed each package, without downloading it, if the Package Source provides manifests:

| Badge | Meaning |
| --- | --- |
| `Signed by <publisher>` | A trusted publisher signed the package's manifest. Its files haven't been checked |
| `Untrusted signature` | It's signed by a key that isn't in your trusted publishers |
| `Unsigned` | It has a manifest, but nobody signed it |
| `No Manifest` | It has no manifest |
| `Invalid` | The manifest or signature is bad, or the manifest doesn't match what the source lists for this package. Hover over the badge to see why |

Under `Require`, every badge except `Signed by` also says this Instance would refuse the package. Under `RequireApproval`, so does `Signed by` when the build isn't on a valid attestation list. No badges means the source doesn't provide manifests, or they couldn't be read.

A badge isn't the same as `Verified`, because it doesn't look at the files. A server could offer a genuine manifest with different files. To check the files, select the package and click **Check Package**. The Instance downloads that version and shows its status, who signed it, the commit and build it came from and the NuGet packages in its SBOM. Nothing is installed, and this works in any mode. Installing a package always checks its files, unless the mode is `Off`.

Only trust the publisher named in the badge. The "by" name beside a package ID, and the rest of what Browse lists, come from the package's source and aren't proven. The details under **Verification** come from the signed manifest when the badge says `Signed by`.

A version uploaded again before its server was updated can show `Invalid` because the server still lists its old build date. Upload it again to fix this.

**Installed packages:** expanding a package on Packages > Installed also shows what built each version and its build configuration and SDK. **SBOM** lists the version's NuGet packages. The Instance doesn't compare a package with its source, because building runs code from the repository. Run `th packages rebuild <file.thp>` on a build machine instead.

**Startup time:** unless the mode is `Off`, the Instance reads every file of each package that has a manifest when it starts, so that can add to startup time, especially after a reboot or update. Packages without a manifest aren't read. Boot records show the time as the Load Packages step.

A package that's changed or refused raises one Notification that lists them. The System API reports the same results, for IT tools to collect:

| Endpoint | Returns |
| --- | --- |
| `GET /_instances/packages/verification` | The mode in use, and the last result for every package on disk, including its `uuid`, `contentHash` (the hash recorded when that build was packed, which tells two builds of one version apart), `installed` (when its folder was created) and `inUse` (a configuration uses it or the configuration profile applies it) |
| `GET /_instances/packages/verification/{packageId}/{version}` | The last result for one version |

These report the last check and don't read the files. They're protected by the `Instance` System Resource (section 2.7).

**Under `Require`:**

- An install from the Admin interface or `th packages install` fails with the reason, and nothing is written. A package whose dependency fails isn't installed either.
- A package that reaches the packages folder another way, ex. a Deployment or a copied folder, is refused when the Instance loads it. A package that's already running stops if its `package.json` is replaced by one that fails.
- A refused package stays in the packages folder but can't be used, and can be removed on Packages > Installed. If an older version passes, modules that use the latest version get that one. **Verify** and **Verify All** apply the result right away: a package that now passes is used again, and one that fails stops.
- Packages without a signature are refused, including ones built before signing was added. Rebuild them with a signing key first.

**What it doesn't do:**

- Files are checked when packages load or install. A file changed while the Instance is running isn't noticed until the next restart or **Verify All**, unless the package's `package.json` changes too. Run `th packages verify` on a schedule to find these sooner.
- Whoever can write to the configuration folder can change what the Instance trusts, so protect that folder with file permissions.
- A signature shows who published a package and that it hasn't changed. It doesn't show that the code is safe, or that it was built from the commit it names. To check that, run `th packages rebuild <file.thp>` (Developer Guide, section 9), which builds the commit again and compares the files.

#### 3.7.1 Packages approved by a TrakHound Package Manager

Under `Require`, a package signed by a trusted publisher is used however it reached the Instance, including a build copied onto it by hand. `RequireApproval` also requires the build to be approved by a TrakHound Package Manager source (section 2.4). It's only offered in Settings > **Package Verification** when a source is a TrakHound Package Manager.

**Server key:** the Package Manager signs its lists with its server key. The first time the Instance reads the key, it pins it in `security/package-manager-key.json` (one per source). Security > Trusted Publishers shows the pinned **Key ID** under **Package Manager Approvals**: confirm it matches the key ID the Package Manager shows. If the Package Manager later sends a different key, the Instance refuses it and its lists, logs an error and raises a Notification, and keeps using the last list signed by the pinned key. If the Package Manager's key was replaced on purpose, confirm the new key ID with the Package Manager's administrator, then click **Clear Pinned Key**. The Instance pins the key the Package Manager sends next. Removing the source keeps its pinned key, so adding it back doesn't trust a different key.

**Attestation list:** the Instance reads the list of builds approved (or deprecated) in its channel when it starts, every hour, and before it installs a package (at most once a minute). A list is only used if its signature matches the pinned key and it hasn't expired (7 days after it was issued, by default). The last valid list is kept in `security/package-manager-attestations.json`, so the Instance still has it when it starts without the Package Manager. When a new list arrives, the Instance checks its packages again: a build that was revoked stops, and one approved since starts.

A package is used under `RequireApproval` if its files match its manifest, it's signed by a trusted publisher, and its content hash, ID and version are on the list of a Package Manager source. Anything else is refused like under `Require`: a signed build that isn't on the list shows `Not Approved` ("Not approved by the Package Manager"), and is reported to the Package Manager with that state. A package without a manifest is refused.

| Situation | What happens |
| --- | --- |
| The list expires (the Instance has been cut off from the Package Manager for longer than the list's lifetime) | Packages on the last list keep working, so the site doesn't stop, but nothing new is installed. The Instance logs a warning every hour and shows a Notification until it reads a new list. A revoked build can't be removed from a cut-off Instance, since it can't read the new list |
| There's no valid list (the key or list was never read, or the key was cleared) | Every package is refused. Trusted Publishers says why |
| The Package Manager can't be reached | The last valid list is used. Startup and installs don't wait for it |

`th packages install` uses the last list the Instance kept, since the CLI doesn't read the Package Manager itself.

## 4. Troubleshooting

Start with the logs: Explorer > Diagnostics > Logs in the Admin interface, or the `logs` folder next to the program files. For problems installing or starting the Instance, see the troubleshooting section of the TrakHound Instance Installation Guide.

| Problem | What to do |
| --- | --- |
| An App, API, Function or Worker shows Not loaded | Open Explorer > Diagnostics > Modules for the load error. If Config shows Package missing, install the package (section 2.4) |
| Changes to `instance.config.yaml` don't apply | Restart the Instance (section 2.3.1). Settings saved in the Admin interface say when they need a restart |
| A client gets `413 Payload Too Large` | The request body is larger than **Max Request Size** (Settings > Interfaces, 256 MB by default). Raise it, or set 0 for no limit. Under IIS, the site's `web.config` limit applies too |
| The log says "Invalid HTTP Address" and the Instance is only reachable from the same computer | `httpAddress` isn't an IP address, so the Instance listens on localhost only. Correct it in Settings > Interfaces (or `instance.config.yaml`) and restart |
| A client gets `429 Too Many Requests`, or an MCP Tool returns "Rate Limit Exceeded" | The caller went over a Rate Limit (section 3.5). Wait for the `Retry-After` time, slow the client down, or raise the limit in Settings > Rate Limits |
| The log says "A secret Configuration value was saved as plain text" | The key for encrypted values (`.secretkey` in the configuration folder) couldn't be read or created, so a password or token was saved unencrypted. Check that the Instance's user can read and write the file and its folder, then save the value again |
| Sign in says "Too many failed sign in attempts" | Too many wrong passwords were entered from this address, or for this username (section 2.2). Wait the time shown; a restart of the Instance also clears it |
| Signed out after the Instance restarts | Sessions survive restarts unless the keys in `.local/data-protection` were deleted or replaced, or your admin account's password was changed or the account was disabled (in Security > Admin Accounts, or with `set-admin`). Sign in again |
| The Admin interface returns 404 from another computer, but works on the Instance's computer | That computer's address isn't in Settings > Interfaces > **Allowed Addresses** (section 3.2). Behind a reverse proxy, check that the proxy is a Trusted Proxy, so the client's own address is checked |
| Admin pages load but don't respond | The browser can't keep a WebSocket open. Under IIS, install the WebSocket Protocol feature; behind a proxy or load balancer, allow WebSockets |
| Health shows Degraded or Unhealthy | Open Explorer > Diagnostics > Health to see which check failed. Mark expected items as Ignore in Settings > Health (section 2.9) |
| A metrics page shows a notice instead of data | That metric is turned off. Turn it on in Settings > Metrics, then restart |
| A "Previous Run did not Shut Down Cleanly" notification | The Instance process stopped without shutting down. Open the Instance's **Information** tab: **Last Shutdown** shows the reason, and **Shutdown Details** shows the exception (for an unhandled exception) and, on Windows, the Event Log entries about the stopped process. These explain crashes the Instance can't log itself, such as a Stack Overflow (`.NET Runtime`), an access violation (`Application Error`, with the faulting module and exception code), or IIS ending the worker process (`WAS` in the System log, ex. no response to a ping or Rapid-Fail Protection). The same entries are in the log. A Stack Overflow isn't written to the Event Log, only to the console: under IIS, set `stdoutLogEnabled="true"` in `web.config` and **Shutdown Details** also shows the end of the stopped process's stdout log (each start makes a new file in `logs`, so delete old ones). If the reason is still Unknown, the process was killed or the machine lost power. If the Instance's user can't read the System log, the log says so. The Instance's **Boots** tab shows how often this has happened |
| The Instance takes a long time to start, or logs errors while starting | Open the Instance's **Boots** tab (section 2.3.1) and click the run. Each startup and Runtime step shows how long it took, the step that failed, and the warnings and errors logged while starting |
| Workers stop after a while (IIS) | The Application Pool is idling or recycling. See the Installation Guide, section 4.1 |
| A changed Variable doesn't apply | Variables are read when the Instance starts. Click **Restart Instance** on Config > Variables (section 2.5) |
| A parameter contains `${name}` text instead of a value | The variable (or environment variable) isn't defined, so the reference is left as it is and a warning is logged. Config > Variables lists undefined references |
| A Scheduled Task doesn't run | Open Explorer > Functions > Schedule Board and check the Worker status and whether the Task is Paused. The `TrakHound.ScheduledTasks` Worker must be configured and running |
| Scheduled runs are skipped | The runs take longer than the schedule interval, or all Run Slots are busy. The Schedule Board shows the skipped count; use a longer schedule |
| A Scheduled Task runs without Parameters | Tasks run with their Job's Parameters. Choose a Job for the Task on the Function's Schedule tab |
| A client on another machine gets 403 "Remote access to an unprotected System Resource is not allowed" | No Security Profile covers the System Resource or Service operation, so it can only be used from the same machine. A request through a Docker port mapping comes from another machine too, even from the Docker host. Add a Profile that covers it on Config > Security Profiles (ex. for the API Keys provider) and send an API key, or allow Unprotected Remote Access in Settings > Interfaces (section 2.7) |
| A package gets 403 "Zone Access Denied", or the log shows it | The package is in a Zone whose Rules don't allow the call. Open the Zone's **Activity** (or the Zone Map) and **Grant** it, or put the Zone in Audit mode while you find what the package needs (section 2.7.1) |
| A Route doesn't try its next Target | The next Target is only tried for the results the first one **Falls Back On** (by default `NotFound`, `NotAvailable`, `Timeout` and `InternalError`). Errors such as Bad Request or Forbidden are returned to the caller. Select the Target on Config > Routers and add the results it should fall back on, ex. `Empty` (section 2.5) |
| A Service call returns 503 | The Service reports that its backend isn't available (ex. the database or broker can't be reached). Earlier versions returned 404 for this. Check the Service's connection settings, or add a fallback Target to the Route (section 2.5) |
| A Service call returns 400 "Invalid Router ID" | The `routerId` isn't a configured Router, or (without one) the Instance has no default Router. Earlier versions answered Blob and Key Value calls with an invalid status line (`HTTP/1.1 0`) that clients couldn't read. Add a Router on Config > Routers (section 2.5) |
| Saving a configuration said the package "could not be downloaded" | Earlier versions couldn't download the latest version of a package that wasn't installed yet. Update the Instance, or install the package first (section 2.4). Package Source Organizations now match regardless of case |
| An import on the Schedule Board says a Task's Job is missing | Export the Tasks together with their Jobs, or add a Job with that ID on the Function's Jobs tab first (section 2.3.4) |
| An App or the Admin interface shows a reconnecting message | The browser lost its connection to the Instance and retries automatically, showing a countdown. Click **Retry Now**, or reload the page; if it doesn't reconnect, check that the Instance is running |
| Forgot an admin password | An Owner sets a new one on Security > Admin Accounts > **Edit**. If no Owner can sign in, run `set-admin` with that username on the machine the Instance runs on (section 3.2) |
| The login page says "No Admin Account" | No admin account exists. Create one with `set-admin`, or with the installer or environment variables (section 3.2), then reload the page |
| Adding or changing a User or API Key says "These Roles give access to the System API or Services" | A Security Profile gives one of the roles permissions on System or Service resources, and only an Owner can give or change those (section 2.7.3). Use a role that only reaches APIs and Apps, or ask an Owner |
| A page shows "No Access", or an action shows "Not Allowed" | Your admin account's roles don't cover it (section 2.7.3). Nothing was changed. An Owner can change your roles on Security > Admin Accounts |
| Adding or changing a User or API Key says "Your Admin Account can't give this Role" | One of your admin roles lists the roles it can give, and this one isn't in the list (section 2.7.3). An Owner can change the list on Security > Admin Roles |
| An action says "Your Admin Account can only do this in its Zones" | One of your admin roles is limited to Zones, and the module isn't in one of them (section 2.7.3). For a new configuration, use an ID or a Group the Zone lists. An Owner can change the role's Zones on Security > Admin Accounts |
| A module or Service is missing from the Explorer or Config lists | Your admin role is limited to Zones (section 2.7.3). The lists show the modules in those Zones, and the Services their Rules allow |
| An area is missing from the sidebar (ex. Security or Deploy) | Your admin account's roles don't let you view it (section 2.7.3) |
| Installing a package says your account doesn't have permission | The Packages role only installs packages of the kinds it can configure (Apps, APIs, Functions and Workers). An Owner installs Service and Identity packages |
| A deployment file in the `deployments` folder wasn't installed | Check **Installed at Startup** on Deploy > Install and the notifications. The file must be directly in the folder with the `.thd` extension, and a file that was already installed (or failed) isn't installed again (section 2.6.1). The Instance installs it only when it starts, so restart it |
