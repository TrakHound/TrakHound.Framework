using TrakHound.Debug;
using TrakHound.Example.Worker;

// Run the Worker without an Instance until Ctrl+C. Its Parameters are "parameters" in trakhound.debug.json
var host = await TrakHoundDebugHost.Create();
await host.RunWorker<Worker>();
