# TrakHound.ApiExplorer.App

A browser for the Apis on a TrakHound Instance. Developers who build applications on the Instance's Apis can see which Apis and endpoints they're allowed to call, read each endpoint's parameters and responses, send Try It requests, copy code samples and download the OpenAPI documents, without access to the Admin interface. Install it on any Instance whose Apis other developers call.

## Package Information
| Property | Value |
|---|---|
| Id | `TrakHound.ApiExplorer.App` |
| Category | `app` |
| Default Name | API Explorer |
| Default Route | `api-explorer` |

Installing the package from the Admin interface (Packages) adds an App configuration with the route `api-explorer`. The CLI (`th packages install`) only installs the files unless you add `--configure`. `TrakHound.Welcome.App` depends on this package, so installing the Welcome page installs the API Explorer too.

The App has no configuration parameters.

## What it shows
The page reads the Instance's Api Catalog, which lists the Apis served over HTTP with only the endpoints the caller is allowed to call.

| Area | What's there |
|---|---|
| Header | The signed-in user (or Anonymous), **Use an API Key**, and a **Sign in** button for each Identity Provider the Catalog offers |
| Api list | **All APIs** and each Api with its route and endpoint count. **Search Endpoints** matches the route, description, method, group, Api name and parameter names (every word must match) |
| Api summary | The Api's name, version, description and URL, **OpenAPI Document** (downloads `<Api name>.openapi.json`) and **Copy OpenAPI URL** |
| Endpoints | Grouped by their Group (or Controller), with the method (`WS` for a Stream), route, description and a lock on endpoints that need authentication |
| Endpoint | Parameters (name, where it goes, type, description, default), Responses (status code, content type, schema), Try It and Code Samples (cURL, JavaScript, C# and Python) |

**Try It** sends the request with the route, query and header parameters and the body you enter. It shows the status, time, size, content type, headers and body (JSON is formatted). Text responses up to 2 MB are shown, and any response can be saved with **Save**. Stream endpoints are WebSockets and can't be tried, so their Code Samples show how to connect (cURL uses `websocat`).

## Routes it reads
The App calls the Instance at its own address:

| Route | Used for | Resource |
|---|---|---|
| `/_developer/catalog` | The Apis, endpoints, identity and sign-in providers | `system:api-catalog` |
| `/_developer/openapi/<apiId>` | The OpenAPI document of an Api | `system:api-catalog` |

The page shows "Sign in, or use an API Key" for a 401, "You don't have access to the API Catalog" for a 403, and says the Instance doesn't provide an API Catalog for a 404.

## Credentials
Requests (the Catalog, OpenAPI documents and Try It) are sent from the visitor's browser, so they're made with the visitor's own sign-in or API Key, the same as their application would be. Each visitor only sees the endpoints they can call.

- **Sign in:** uses the Instance's session cookie.
- **API Key:** paste a key in **Use an API Key**. It's sent in the header the Catalog names (`X-API-Key` when it names none). The key is kept in the browser tab's session storage, so it's gone when the tab closes, and **Clear Key** removes it. If the Instance doesn't accept the key, the page says so and shows only the endpoints that don't need authentication.

Code samples use `<your-api-key>` as a placeholder for endpoints that need authentication. A key can also be sent as `Authorization: Bearer <key>`.

See the Instance User Guide (section 2.11, Api Catalog and API Explorer) for the Catalog and its access control. Endpoint and parameter descriptions come from the Apis themselves: see https://www.trakhound.com/docs/trakhound-framework/apis.

## Related Modules
| Package | Description |
|---|---|
| `TrakHound.Welcome.App` | A home page for development Instances. It depends on this package and links to it |

## Development
`Program.cs` runs the App outside an Instance with `TrakHound.Debug.Apps`. Because the page calls the Api Catalog at its own address, the debug host only shows the layout and a load error. Install the package on an Instance to test it. See https://www.trakhound.com/docs/trakhound-framework/debugging.

```bash
dotnet run --project TrakHound.ApiExplorer.App.csproj
```

The Try It requests are sent by `wwwroot/api-explorer.js`, and the code samples are built in `CodeSamples.cs`.
