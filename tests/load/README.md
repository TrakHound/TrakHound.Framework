# Load Tests

[k6](https://k6.io) scenarios for measuring an Instance under load, with `dotnet-counters` recorded while each one runs.

| Scenario | What it does |
| --- | --- |
| `api.js` | Ramps to 50 virtual users calling `GET /functions/list` and `GET /functions/{functionId}` (Functions Api) |
| `app.js` | Ramps to 20 virtual users loading an App page (`APP_PATH`, default `/`). This measures the server prerender; HTTP tools can't drive the Blazor circuit that follows |
| `streams.js` | Holds 500 WebSocket subscriptions to a Function Run's status open at once (the same Stream Endpoint as Server-Sent Events) |

## Requirements

- k6 (`winget install GrafanaLabs.k6`)
- The .NET diagnostics tools: `dotnet tool install -g dotnet-counters dotnet-trace dotnet-gcdump`
- Python 3 for `summarize.py`

## Setting up an Instance

1. Build the Instance in Release (`dotnet build instances/TrakHound.Instance/TrakHound.Instance.csproj -c Release`). Debug builds aren't representative.
2. Create a `loadtest` configuration profile in the Release output (`bin/Release/net10.0/config/loadtest`), for example by copying a Debug profile, and set its `httpPort` (the scripts default to `8495`).
3. Install the packages the scenarios use into the Release output's `packages` folder: the Functions Api (with a Function, `FUNCTION_ID`, default `TrakHound.Example.Function`) and an App for `app.js`.
4. Rate Limits are off unless enabled (`rateLimits.enabled`). Leave them off, or the scenarios mostly measure 429 responses.
5. If the Apis require Authentication, pass an API key with `-e API_KEY=...` (sent as `X-API-Key`).
6. Run the Instance with `ASPNETCORE_ENVIRONMENT=Production` and `TRAKHOUND_CONFIGURATION_PROFILE=loadtest`.

Run the load generator on another machine when possible. On the same machine it competes with the Instance for CPU, so the numbers are best compared between runs rather than read as absolute capacity.

## Running

```bash
tests/load/run.sh <instance pid> <output directory> [base url]
```

`run.sh` warms the Instance up, then runs each scenario and writes `<scenario>.k6.json`, `<scenario>.k6.txt` and `<scenario>.counters.csv` to the output directory. `SCENARIOS="api streams"` runs a subset.

To compare runs (ex. two builds run with the same profile and packages), summarize their output directories side by side:

```bash
python tests/load/summarize.py <run directory> [<run directory> ...]
```

It reports requests per second and latency (or Streams opened and connect time), CPU (cores used), allocation rate and allocation per request, GC counts and pause time, lock contentions, working set and thread pool threads.

To see where the time goes, record a trace while a scenario runs: `dotnet-trace collect -p <pid> --duration 00:00:30` (open the `.nettrace` in PerfView or Visual Studio).
