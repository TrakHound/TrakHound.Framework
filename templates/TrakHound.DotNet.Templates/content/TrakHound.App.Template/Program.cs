using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Radzen;
using TrakHound.Blazor.Apps;
using TrakHound.Debug;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRadzenComponents();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Add the services the Instance provides to Apps (Client, injection services, Javascript and local storage).
// The Client connects to the Instance set in trakhound.debug.json
var host = await TrakHoundDebugHost.Create();
builder.Services.AddTrakHoundDebugHosting(host);

var app = builder.Build();

// Set ServiceProvider for TrakHound Injection Services
app.MapTrakHoundDebugServices();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<UserPackageId.Debug>().AddInteractiveServerRenderMode();
app.Run();
