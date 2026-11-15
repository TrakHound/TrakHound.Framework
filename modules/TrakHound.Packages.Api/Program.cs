using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Debug.AspNetCore;
using TrakHound.Volumes;

internal class Program
{
    public static async Task Main(string[] args)
    {
        // Create new TrakHoundClient based on the Instance BaseUrl and Router
        var clientConfiguration = new TrakHoundHttpClientConfiguration("localhost", 8472);

        var client = new TrakHoundHttpClient(clientConfiguration, null);

        var volumePath = Path.Combine(AppContext.BaseDirectory, "volume");
        var volume = new TrakHoundVolume("volume", volumePath);

        var instanceInformation = await client.Instances.GetHostInformation();

        var builder = WebApplication.CreateBuilder(args);

        var app = builder.Build();
        app.MapTrakHoundApiEndpoints(client, volume, instanceInformation?.Id); // Also serves the Api Debugger at /_debug
        app.Run();
    }
}