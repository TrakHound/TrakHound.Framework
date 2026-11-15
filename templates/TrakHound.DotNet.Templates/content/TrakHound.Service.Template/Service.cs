using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;
using TrakHound.Services.KeyValues;

// A Key Value Service (Client.Services.KeyValues) that stores Values in memory.
// Replace the dictionaries with calls to your backend, and implement other Service interfaces
// (ex. ITrakHoundBlobService, ITrakHoundMessageService) to provide more Service families
public class Service : TrakHoundService, ITrakHoundKeyValueService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _collections = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);


    // The Instance creates the Service with exactly this constructor
    public Service(ITrakHoundServiceConfiguration configuration) : base(configuration) { }


    public override Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
    {
        // Check that the backend is reachable (ex. open a connection)
        var response = new TrakHoundServiceAvailabilityResponse();
        response.IsAvailable = true;
        response.Message = "Available";

        return Task.FromResult(Ok(response));
    }


    public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(CancellationToken cancellationToken = default)
    {
        IEnumerable<string> collections = _collections.Keys.OrderBy(o => o).ToList();
        return Task.FromResult(Ok(collections));
    }

    public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(collection)) return Task.FromResult(BadRequest<IEnumerable<string>>("Collection is required"));
        if (!_collections.TryGetValue(collection, out var values)) return Task.FromResult(NotFound<IEnumerable<string>>());

        // The pattern uses "*" as a wildcard (ex. "line1/*")
        var regex = new Regex("^" + Regex.Escape(pattern ?? "*").Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase);

        IEnumerable<string> keys = values.Keys.Where(o => regex.IsMatch(o)).OrderBy(o => o).Skip(Math.Max(skip, 0));
        if (take > 0) keys = keys.Take(take);

        return Task.FromResult(Ok<IEnumerable<string>>(keys.ToList()));
    }

    public Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(collection) || keys == null) return Task.FromResult(BadRequest<TrakHoundKeyValueResponse>("Collection and Keys are required"));
        if (!_collections.TryGetValue(collection, out var values)) return Task.FromResult(NotFound<TrakHoundKeyValueResponse>());

        var result = new Dictionary<string, string>();
        foreach (var key in keys.Where(o => !string.IsNullOrEmpty(o)).Distinct())
        {
            result[key] = values.GetValueOrDefault(key);
        }

        var response = new TrakHoundKeyValueResponse();
        response.Collection = collection;
        response.Values = result;
        return Task.FromResult(Ok(response));
    }

    public Task<TrakHoundServiceResponse<int>> Count(string collection, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(collection)) return Task.FromResult(BadRequest<int>("Collection is required"));

        var count = _collections.TryGetValue(collection, out var values) ? values.Count : 0;
        return Task.FromResult(Ok(count));
    }

    public Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(collection) || entries == null || entries.Count == 0) return Task.FromResult(BadRequest("Collection and Entries are required"));

        // This Service does not support a time to live (ttl), so Values are kept until they are Deleted
        var values = _collections.GetOrAdd(collection, _ => new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.Key)) values[entry.Key] = entry.Value;
        }

        return Task.FromResult(Ok());
    }

    public Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(collection) || keys == null) return Task.FromResult(BadRequest("Collection and Keys are required"));
        if (!_collections.TryGetValue(collection, out var values)) return Task.FromResult(NotFound());

        foreach (var key in keys)
        {
            if (!string.IsNullOrEmpty(key)) values.TryRemove(key, out _);
        }

        return Task.FromResult(Ok());
    }
}
