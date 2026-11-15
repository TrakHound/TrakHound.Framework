using TrakHound.Debug;

// Run the Function once without an Instance and print the Response. The input Parameters are the JSON in the
// first argument (ex. dotnet run -- "{\"name\":\"value\"}"), or "input" in trakhound.debug.json
var host = await TrakHoundDebugHost.Create();
return await host.RunFunction<Function>(args);
