// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Workers;

namespace TrakHound.Example.Worker
{
    /// <summary>
    /// Reads a simulated sensor at an interval and writes the recent Readings to its Volume, where the Example Api and Function read them
    /// </summary>
    public class Worker : TrakHoundWorker
    {
        private const int DefaultInterval = 5000;
        private const int DefaultHistorySize = 720; // 1 hour at the default interval

        private readonly Random _random = new Random();
        private readonly List<SensorReading> _readings = new List<SensorReading>();

        private CancellationTokenSource _stop;
        private Task _loop;


        // The Instance creates the Worker with this constructor, then sets Client, Volume and Variables
        public Worker(ITrakHoundWorkerConfiguration configuration) : base(configuration) { }


        protected override async Task OnStartAsync()
        {
            // The Parameters set in the Worker's Configuration
            var interval = Configuration.GetParameter<int>("interval");
            if (interval <= 0) interval = DefaultInterval;

            var historySize = Configuration.GetParameter<int>("historySize");
            if (historySize <= 0) historySize = DefaultHistorySize;

            // Continue from the Readings written before a restart
            _readings.AddRange(await SensorReading.ReadAll(Volume));

            _stop = new CancellationTokenSource();
            _loop = Run(TimeSpan.FromMilliseconds(interval), historySize, _stop.Token);

            Log(TrakHoundLogLevel.Information, $"Example Worker STARTED (every {interval}ms, keeping {historySize} Readings)");
        }

        protected override async Task OnStopAsync()
        {
            _stop.Cancel();
            await _loop;
            _stop.Dispose();

            Log(TrakHoundLogLevel.Information, "Example Worker STOPPED");
        }

        private async Task Run(TimeSpan interval, int historySize, CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(interval);

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    try
                    {
                        var reading = ReadSensor();

                        _readings.Add(reading);
                        if (_readings.Count > historySize) _readings.RemoveRange(0, _readings.Count - historySize);

                        await Volume.WriteJson(SensorReading.ReadingsPath, _readings);

                        Log(TrakHoundLogLevel.Debug, $"Example Worker : Reading = {reading.Value}");
                    }
                    catch (Exception ex)
                    {
                        // Log and continue, so one failure doesn't stop the Worker
                        Log(TrakHoundLogLevel.Error, ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// A temperature that rises and falls over 10 minutes, with some noise. A real Worker would read a device here
        /// </summary>
        private SensorReading ReadSensor()
        {
            var now = DateTime.UtcNow;
            var cycle = now.TimeOfDay.TotalMinutes / 10 * 2 * Math.PI;
            var value = 20 + 5 * Math.Sin(cycle) + (_random.NextDouble() - 0.5);

            return new SensorReading { Timestamp = now, Value = Math.Round(value, 2) };
        }
    }
}
