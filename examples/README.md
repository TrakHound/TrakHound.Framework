# TrakHound Examples

Example packages that work together, so a new Instance has something running to look at and copy from. Each one shows one kind of package:

| Package | Category | What it shows |
| --- | --- | --- |
| [TrakHound.Example.Worker](TrakHound.Example.Worker) | worker | A background loop that reads a (simulated) sensor at an interval and writes the recent readings to its Volume |
| [TrakHound.Example.Function](TrakHound.Example.Function) | function | A Function with a described input Parameter that summarizes the readings and returns the result as Response Parameters |
| [TrakHound.Example.Api](TrakHound.Example.Api) | api | Described endpoints (marked as MCP Tools), a stream, running a Function through the Client, and Variables |
| [TrakHound.Example.App](TrakHound.Example.App) | app | A Blazor page that calls the Api through the Client and refreshes on a timer |

```
Worker ──writes──> Volume "trakhound.example" <──reads── Api <──Client.Api── App
                              ^                           │
                              └──reads── Function <───────┘ Client.Functions.Run
```

The Worker, Api and Function share data through the `trakhound.example` Volume (each package's `.defaultVolumeId`), so their configurations must use the same Volume ID. `SensorReading.cs` (in the Worker project) is linked into the other projects, so they all read the same file format.

## Run them on an Instance

Install `TrakHound.Example.App` from Packages in the Admin interface. It depends on the other three, so the Instance installs all four and adds a configuration for each:

| Configuration | Route / ID | Volume |
| --- | --- | --- |
| App | `example` | |
| Api | `example/api` | `trakhound.example` |
| Function | `TrakHound.Example.Function` | `trakhound.example` |
| Worker | | `trakhound.example` |

Then open `/example` on the Instance's HTTP port. The Api is at `/example/api` (ex. `/example/api/readings/latest`), and the API Explorer lists its endpoints.

`th packages install TrakHound.Example.App --configure` installs the four packages with the same configurations. Without `--configure` it only installs the files, so add the configurations yourself (Config in the Admin interface, or `th config set`). The Instance installer can include them as default packages, with their configurations (Default Packages page).

If you change the Api's route, set the App's `apiRoute` parameter to it. If you give the Function a different ID, set the Api's `functionId` parameter to it.

## Debug them without an Instance

Each project runs on its own with `dotnet run` (or F5), using the settings in its `trakhound.debug.json`. They all set `volumePath` to `examples/volume`, so the Worker, Api and Function read and write the same files when debugging, like they share the Volume on an Instance.

- Worker: runs until Ctrl+C and writes a reading every `interval` milliseconds.
- Function: runs once with the `input` in `trakhound.debug.json` (or the JSON in the first argument) and prints the Response.
- Api: serves the endpoints and the Api Debugger at `/_debug`. `summary` runs the Function on the Instance in `instanceUrl`, so it needs a running Instance with the Function installed.
- App: serves the page. It calls the Api on the Instance in `instanceUrl`.

## Build and publish

```bash
th packages build examples
```

builds each project in `trakhound.build.json` and publishes it to the Publish Target in the nearest `trakhound.package.publish.json`. Each example's version is the prefix in its `trakhound.package.json` (`0.1`) plus the git height, like the modules. To only build a package file, run `th packages dotnet --output ./out` in a project directory.

## Run an Instance in Docker

[docker](docker) has Docker Compose files for running an Instance from the `trakhound/instance` image, and for developing packages with the `trakhound/instance-dev` image, which these examples can be pushed to (see its README).
