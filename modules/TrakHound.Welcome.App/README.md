# TrakHound.Welcome.App

A home page for development Instances. It shows what is installed on the Instance, where each App and Api is served, and how to start building packages for it. Install it on a developer's machine or a shared development Instance, not in production.

The App's default route is `/`, so it replaces the empty page a new Instance shows at its root. Change the route in its App configuration to put it somewhere else (ex. `welcome`), or if another App already uses `/`.

## What it shows

| Section | Read from | System API Resource |
| --- | --- | --- |
| Signed-in user and Sign In buttons | `/_developer/catalog` | `system:api-catalog` |
| Instance (ID, version, OS, Health) | `/_instances/information/host`, `/_instances/health` | `system:instance` |
| Apps, with links to their routes | `/_apps/information` | `system:apps` |
| Apis (route, HTTP, MCP Tools) | `/_api/information/route` | `system:api-information` |
| Functions | `/_functions/information` | `system:functions` |
| Services and Service Routers (names and packages only) | `/_services/configurations/services`, `/_services/configurations/routers` | `system:services` |
| Packages (loaded Modules and load errors, including Workers) | `/_modules` | `system:modules` |

It also links to the Admin Interface, the API Explorer, `llms.txt`, the MCP Server, the TrakHound website and the Framework documentation, and lists the `dotnet new` commands for the package templates with `--instanceUrl` set to the Instance.

The package depends on `TrakHound.ApiExplorer.App` (any 0.x version), so installing it also installs the API Explorer. Installing from the Admin Interface (Packages) adds an App configuration for both (`/` and `api-explorer`). The CLI (`th packages install`) only installs the files unless you add `--configure`. The API Explorer link is shown when an App configuration uses the API Explorer package.

## Who sees what

The page is read-only. It reads the System API from the visitor's browser, with the visitor's own sign-in, so the Instance decides what each visitor sees, the same as for any other System API call:

- A visitor with the `read` Permission on an area's Resource sees that section.
- When no Security Profile covers a Resource, it can only be read from the Instance's own machine (unless the Instance allows remote access). So on a new local Instance the developer sees everything, and other machines see nothing.
- Visitors who can't read anything see the links, the templates and a notice saying why (sign in, no access, or the System API isn't served at this address).

Service Configuration parameters (which can hold connection strings) are never shown.

The System API must be turned on (Settings > Interfaces > System API). With a separate Admin Port, the System API and Admin Interface are only served on that port, so this page can't show the Instance's details on the HTTP port.

## Debugging

`Program.cs` runs the page outside an Instance, but because the page calls the System API at its own address, the debug host only shows the layout. Install the package on an Instance to test it.
