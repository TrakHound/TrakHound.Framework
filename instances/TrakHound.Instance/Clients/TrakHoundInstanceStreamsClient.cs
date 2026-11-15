// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Instances;

namespace TrakHound.Clients
{
    /// <summary>
    /// Reads the Stream Metrics directly from the Instance running in this process
    /// </summary>
    public class TrakHoundInstanceStreamsClient : ITrakHoundStreamsClient
    {
        private readonly ITrakHoundInstance _instance;


        public TrakHoundInstanceStreamsClient(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        public Task<TrakHoundStreamMetrics> GetMetrics()
        {
            return Task.FromResult(_instance?.StreamMetrics?.GetMetrics());
        }
    }
}
