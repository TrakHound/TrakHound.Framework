using TrakHound.Debug;
using TrakHound.Example.MqttToTimeSeries;

// Run the Worker without an Instance until Ctrl+C, using the Message and Time Series Services of the Instance in trakhound.debug.json.
// Its Parameters are "parameters" in trakhound.debug.json
var host = await TrakHoundDebugHost.Create();
await host.RunWorker<Worker>();
