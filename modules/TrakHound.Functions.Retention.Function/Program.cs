using TrakHound.Debug;

// Run the Function once and print the Response. The input Parameters are the JSON in the first argument
// (ex. dotnet run -- "{\"days\":\"30\"}"), or "input" in trakhound.debug.json. Parameters that aren't set use their defaults
var host = await TrakHoundDebugHost.Create();
return await host.RunFunction<Function>(args);
