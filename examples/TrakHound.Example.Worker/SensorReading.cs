// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Volumes;

namespace TrakHound.Example
{
    /// <summary>
    /// A value read from the simulated sensor. Shared (as a linked file) by the Example Worker, Api, Function and App
    /// </summary>
    public class SensorReading
    {
        /// <summary>
        /// The file in the shared Volume ("trakhound.example") that the Worker writes the recent Readings to (oldest first)
        /// </summary>
        public const string ReadingsPath = "readings.json";


        public DateTime Timestamp { get; set; }

        public double Value { get; set; }


        /// <summary>
        /// Read the recent Readings from the Volume (oldest first). Returns an empty list if there are none yet
        /// </summary>
        public static async Task<List<SensorReading>> ReadAll(ITrakHoundVolume volume)
        {
            if (volume == null) return new List<SensorReading>();

            try
            {
                return await volume.ReadJson<List<SensorReading>>(ReadingsPath) ?? new List<SensorReading>();
            }
            catch
            {
                // The Worker may be writing the file
                return new List<SensorReading>();
            }
        }
    }
}
