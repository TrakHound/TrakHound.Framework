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

// The page reads the System API from the browser (at this host's address), so this debug host only shows the layout.
// Install the package on an Instance to see its information
app.UseStaticFiles();

app.UseAntiforgery();
app.MapRazorComponents<TrakHound.Welcome.App.Debug>().AddInteractiveServerRenderMode();
app.Run();
