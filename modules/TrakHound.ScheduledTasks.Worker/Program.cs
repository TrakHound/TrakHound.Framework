// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients;
using TrakHound.Volumes;
using TrakHound.Workers;

namespace TrakHound.ScheduledTasks
{
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

            var serviceConfiguration = new TrakHoundWorkerConfiguration();

            // Create the Worker (runs until a key is pressed)
            var service = new Worker(serviceConfiguration, client, volume);
            service.InstanceId = instanceInformation?.Id;
            service.LogReceived += ServiceLogReceived;

            await service.Start();

            Console.WriteLine("Press Key to Stop Worker..");
            Console.ReadLine();

            await service.Stop();

            Console.WriteLine("Stopping Worker..");
            await Task.Delay(1000);
        }

        private static void ServiceLogReceived(object sender, TrakHound.Logging.TrakHoundLogItem item)
        {
            Console.WriteLine($"Log : {item.Timestamp.ToLocalDateTime()} : {item.LogLevel} : {item.Code} : {item.Message}");
        }
    }
}