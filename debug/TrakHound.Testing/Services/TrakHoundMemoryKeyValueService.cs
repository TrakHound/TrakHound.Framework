// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TrakHound.Services;
using TrakHound.Services.KeyValues;

namespace TrakHound.Testing.Services
{
    /// <summary>
    /// A Key Value Service that keeps its values in memory (the default for Client.Services.KeyValues in TrakHoundTestClient).
    /// Entries published with a TTL expire after it
    /// </summary>
    public class TrakHoundMemoryKeyValueService : TrakHoundService, ITrakHoundKeyValueService
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (string Value, DateTime? Expires)>> _collections = new(StringComparer.OrdinalIgnoreCase);


        public TrakHoundMemoryKeyValueService() : base(new TrakHoundServiceConfiguration { Id = "memory-keyvalues" }) { }


        public override Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Ok(new TrakHoundServiceAvailabilityResponse { IsAvailable = true, Message = "Available" }));
        }

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(CancellationToken cancellationToken = default)
        {
            IEnumerable<string> collections = _collections.Where(o => GetValues(o.Value).Any()).Select(o => o.Key).OrderBy(o => o).ToList();
            return Task.FromResult(Ok(collections));
        }

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection)) return Task.FromResult(BadRequest<IEnumerable<string>>("Collection is required"));
            if (!_collections.TryGetValue(collection, out var values)) return Task.FromResult(NotFound<IEnumerable<string>>());

            // The pattern uses "*" as a wildcard (ex. "line1/*")
            var regex = new Regex("^" + Regex.Escape(pattern ?? "*").Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase);

            IEnumerable<string> keys = GetValues(values).Keys.Where(o => regex.IsMatch(o)).OrderBy(o => o).Skip(Math.Max(skip, 0));
            if (take > 0) keys = keys.Take(take);

            return Task.FromResult(Ok<IEnumerable<string>>(keys.ToList()));
        }

        public Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || keys == null) return Task.FromResult(BadRequest<TrakHoundKeyValueResponse>("Collection and Keys are required"));
            if (!_collections.TryGetValue(collection, out var values)) return Task.FromResult(NotFound<TrakHoundKeyValueResponse>());

            var current = GetValues(values);
            var result = new Dictionary<string, string>();
            foreach (var key in keys.Where(o => !string.IsNullOrEmpty(o)).Distinct())
            {
                if (current.TryGetValue(key, out var value)) result[key] = value;
            }

            if (result.Count < 1) return Task.FromResult(NotFound<TrakHoundKeyValueResponse>());
            return Task.FromResult(Ok(new TrakHoundKeyValueResponse { Collection = collection, Values = result }));
        }

        public Task<TrakHoundServiceResponse<int>> Count(string collection, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection)) return Task.FromResult(BadRequest<int>("Collection is required"));

            var count = _collections.TryGetValue(collection, out var values) ? GetValues(values).Count : 0;
            return Task.FromResult(Ok(count));
        }

        public Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || entries == null) return Task.FromResult(BadRequest("Collection and Entries are required"));

            var expires = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : (DateTime?)null;
            var values = _collections.GetOrAdd(collection, _ => new ConcurrentDictionary<string, (string, DateTime?)>(StringComparer.OrdinalIgnoreCase));
            foreach (var entry in entries.Where(o => !string.IsNullOrEmpty(o.Key)))
            {
                values[entry.Key] = (entry.Value, expires);
            }

            return Task.FromResult(Ok());
        }

        public Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || keys == null) return Task.FromResult(BadRequest("Collection and Keys are required"));

            if (_collections.TryGetValue(collection, out var values))
            {
                foreach (var key in keys.Where(o => !string.IsNullOrEmpty(o))) values.TryRemove(key, out _);
            }

            return Task.FromResult(Ok());
        }


        private static Dictionary<string, string> GetValues(ConcurrentDictionary<string, (string Value, DateTime? Expires)> values)
        {
            var now = DateTime.UtcNow;
            return values.Where(o => o.Value.Expires == null || o.Value.Expires > now).ToDictionary(o => o.Key, o => o.Value.Value, StringComparer.OrdinalIgnoreCase);
        }
    }
}
