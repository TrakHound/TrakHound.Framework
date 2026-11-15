using TrakHound.Hosting;

#if (isApi)
// Runs the Api without an Instance. The settings (the Instance, Parameters and API Keys) are the "TrakHound" section of
// appsettings.json, or environment variables (ex. TrakHound__InstanceUrl)
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTrakHound(builder.Configuration.GetSection(TrakHoundHostOptions.SectionName));
builder.Services.AddTrakHoundApiKeyAuthentication();
builder.Services.AddHealthChecks().AddTrakHoundInstance();

// Runs as a Windows Service or a systemd service when started as one, and as a console app otherwise. IIS, containers and App Service need nothing
builder.Services.AddWindowsService();
builder.Services.AddSystemd();

var app = builder.Build();

// The Api's endpoints and streams, for callers that send one of the API Keys in "TrakHound:ApiKeys"
var api = app.MapTrakHoundApi(typeof(Controller).Assembly);
api.Group.RequireAuthorization();

app.MapTrakHoundOpenApi(api); // The OpenAPI document at /openapi.json
app.MapHealthChecks("/health");
app.Run();
#elif (isWorker)
// Runs the Worker without an Instance until the host stops. The settings (the Instance and Parameters) are the "TrakHound" section of
// appsettings.json, or environment variables (ex. TrakHound__InstanceUrl)
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTrakHound(builder.Configuration.GetSection(TrakHoundHostOptions.SectionName));
builder.Services.AddTrakHoundWorker<Worker>();

// Runs as a Windows Service or a systemd service when started as one, and as a console app otherwise
builder.Services.AddWindowsService();
builder.Services.AddSystemd();

builder.Build().Run();
#else
// Runs the Function once and writes its Response to the console. The exit code is 0 when the Run succeeds.
// The input is "TrakHound:Input" (ex. dotnet run -- --TrakHound:Input:name=World), and the other settings are the "TrakHound" section
// of appsettings.json, or environment variables (ex. TrakHound__InstanceUrl)
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTrakHound(builder.Configuration.GetSection(TrakHoundHostOptions.SectionName));

using var host = builder.Build();
return await host.RunTrakHoundFunction<Function>();
#endif
