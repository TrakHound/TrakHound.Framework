using System;
using System.Collections.Generic;
using TrakHound.Debug;

// Test the Service without an Instance by calling its operations directly. Its Parameters are "parameters" in trakhound.debug.json
var host = await TrakHoundDebugHost.Create();
using var service = host.CreateService<Service>();

var availability = await service.CheckAvailability();
Console.WriteLine($"CheckAvailability : {availability.StatusCode} : {availability.Result?.IsAvailable} : {availability.Result?.Message}");

var entries = new Dictionary<string, string>();
entries["line1/status"] = "running";
entries["line1/count"] = "42";

var publish = await service.Publish("debug", entries);
Console.WriteLine($"Publish : {publish.StatusCode}");

var keys = await service.ListKeys("debug", "line1/*", 0, 100);
Console.WriteLine($"ListKeys : {keys.StatusCode} : {string.Join(", ", keys.Result ?? Array.Empty<string>())}");

var read = await service.Read("debug", new[] { "line1/status", "line1/count" });
if (read.Result?.Values != null)
{
    foreach (var value in read.Result.Values) Console.WriteLine($"Read : {value.Key} = {value.Value}");
}

var count = await service.Count("debug");
Console.WriteLine($"Count : {count.StatusCode} : {count.Result}");

var delete = await service.Delete("debug", new[] { "line1/count" });
Console.WriteLine($"Delete : {delete.StatusCode}");
