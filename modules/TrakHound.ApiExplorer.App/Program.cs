using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Radzen;
using TrakHound.Apps;
using TrakHound.Blazor.Services;
using TrakHound.Clients;
using TrakHound.Variables;
using TrakHound.Volumes;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRadzenComponents();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var clientConfiguration = new TrakHoundHttpClientConfiguration("localhost", 8480);

var clientProvider = new TrakHoundHttpClientProvider(clientConfiguration);
builder.Services.AddSingleton<ITrakHoundClientProvider>(clientProvider);
builder.Services.AddSingleton<ITrakHoundClient>(clientProvider.GetClient());

var volumeProvider = new TrakHoundVolumeProvider();
builder.Services.AddSingleton<ITrakHoundVolumeProvider>(volumeProvider);

// Provided by the Instance (along with the "_download/{key}" endpoint) when the App is installed
builder.Services.AddSingleton<TrakHoundDownloadService>();
builder.Services.AddScoped<JavascriptService>();
builder.Services.AddScoped<TrakHoundAppService>();

var variableProvider = new TrakHoundVariableProvider();

var injectionServiceManager = new TrakHoundAppInjectionServiceManagerDebug(clientProvider, volumeProvider, variableProvider);
builder.Services.AddSingleton<ITrakHoundAppInjectionServiceManager>(injectionServiceManager);

builder.Services.AddTransient<TrakHoundTransientAppInjectionServices>();
builder.Services.AddScoped<TrakHoundScopedAppInjectionServices>();
builder.Services.AddSingleton<TrakHoundSingletonAppInjectionServices>();
builder.Services.AddTransient<ITrakHoundAppInjectionServices, TrakHoundAppInjectionServices>();

var app = builder.Build();

// Set ServiceProvider for TrakHound Injection Services
injectionServiceManager.ServiceProvider = app.Services;

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();

app.MapGet("_download/{key}", (string key, TrakHoundDownloadService downloadService) =>
{
    var item = downloadService.GetItem(key);
    if (item?.Content == null) return Results.NotFound();
    return Results.File(item.Content, item.ContentType ?? "application/octet-stream", item.Filename);
});

app.UseAntiforgery();
app.MapRazorComponents<TrakHound.ApiExplorer.App.Debug>().AddInteractiveServerRenderMode();
app.Run();
