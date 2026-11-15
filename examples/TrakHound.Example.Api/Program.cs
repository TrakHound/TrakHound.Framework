using Microsoft.AspNetCore.Builder;
using TrakHound.Debug;
using TrakHound.Debug.AspNetCore;

// Serve the Api's endpoints without an Instance. The Client, Volume, Variables and Parameters come from trakhound.debug.json
var host = await TrakHoundDebugHost.Create();

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();
app.MapTrakHoundApiEndpoints(host); // Also serves the Api Debugger at /_debug
app.Run();
