// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;

namespace TrakHound.Services
{
    class RouteRequest
    {
        protected readonly List<string> _queries = new List<string>();
        private readonly object _lock = new object();


        public string Id { get; set; }

        public string Name { get; set; }

        public IEnumerable<string> Queries => _queries;

        /// <summary>
        /// Cancels the request. Targets are not tried once the request is cancelled
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Receives the result of each Service call made for the request (from the Router that created it)
        /// </summary>
        public ITrakHoundServiceMetricsRecorder MetricsRecorder { get; set; }


        public RouteRequest()
        {
            Id = null;
            Name = null;
        }

        public RouteRequest(string name, string id, string query = null)
        {
            Name = name;
            Id = !string.IsNullOrEmpty(id) ? id : Guid.NewGuid().ToString();

            AddQuery(query);
        }

        public RouteRequest(string name, string id, IEnumerable<string> queries)
        {
            Name = name;
            Id = !string.IsNullOrEmpty(id) ? id : Guid.NewGuid().ToString();

            AddQueries(queries);
        }


        public void AddQuery(string query)
        {
            if (!string.IsNullOrEmpty(query))
            {
                lock (_lock) _queries.Add(query);
            }
        }

        public void AddQueries(IEnumerable<string> queries)
        {
            if (!queries.IsNullOrEmpty())
            {
                lock (_lock)
                {
                    foreach (var query in queries)
                    {
                        if (query != null)
                        {
                            _queries.Add(query);
                        }
                    }
                }
            }
        }
    }
}
