using TrakHound.Services;
using TrakHound.Services.KeyValues;

namespace TrakHound.Memory.Service
{
    public class MemoryKeyValueService :
        MemoryService,
        ITrakHoundKeyValueService
    {
        private static readonly TimeSpan _defaultTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan _defaultInterval = TimeSpan.FromMinutes(1);

        private readonly ListDictionary<string, string> _collectionKeys = new ListDictionary<string, string>(); // Collection => Key
        private readonly Dictionary<string, string> _keys = new Dictionary<string, string>(); // Id => Key
        private readonly Dictionary<string, string> _collections = new Dictionary<string, string>(); // Id => Collection
        private readonly CacheDictionary<string, string> _values; // Id => Value
        private readonly object _lock = new object();


        public MemoryKeyValueService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            var ttl = GetDuration(configuration, "ttl", _defaultTtl);
            var interval = GetDuration(configuration, "expirationInterval", _defaultInterval);

            _values = new CacheDictionary<string, string>(ToMilliseconds(ttl), ToMilliseconds(interval));
            _values.KeyExpired += ValuesKeyExpired;
        }

        protected override void OnDisposed()
        {
            _values.Dispose();
        }

        private static TimeSpan GetDuration(ITrakHoundServiceConfiguration configuration, string parameter, TimeSpan defaultValue)
        {
            var duration = configuration?.GetParameter(parameter).ToTimeSpan() ?? TimeSpan.Zero;
            return duration > TimeSpan.Zero ? duration : defaultValue;
        }

        private static int ToMilliseconds(TimeSpan timeSpan)
        {
            return (int)Math.Clamp(timeSpan.TotalMilliseconds, 1, int.MaxValue);
        }

        private void ValuesKeyExpired(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                lock (_lock)
                {
                    var key = _keys.GetValueOrDefault(id);
                    var collection = _collections.GetValueOrDefault(id);

                    if (key != null && collection != null)
                    {
                        _keys.Remove(id);
                        _collections.Remove(id);
                        _collectionKeys.Remove(collection, key);
                    }
                }
            }
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IEnumerable<string> collections;
            lock (_lock) collections = _collectionKeys.Keys.ToList();
            if (!collections.IsNullOrEmpty())
            {
                return TrakHoundServiceResponse<IEnumerable<string>>.Ok(collections);
            }
            else
            {
                return TrakHoundServiceResponse<IEnumerable<string>>.NotFound();
            }
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(pattern))
            {
                IEnumerable<string> keys;
                lock (_lock) keys = _collectionKeys.Get(collection).ToList();
                if (!keys.IsNullOrEmpty())
                {
                    if (pattern != "*")
                    {
                        var p = pattern.EndsWith("*") ? pattern.Substring(pattern.Length - 1) : pattern;

                        keys = keys.Where(o => o.ToLower().StartsWith(p));
                    }

                    return TrakHoundServiceResponse<IEnumerable<string>>.Ok(keys);
                }
                else
                {
                    return TrakHoundServiceResponse<IEnumerable<string>>.NotFound();
                }
            }
            else
            {
                return TrakHoundServiceResponse<IEnumerable<string>>.BadRequest();
            }
        }

        public async Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(collection) && !keys.IsNullOrEmpty())
            {
                var values = new Dictionary<string, string>();
                lock (_lock)
                {
                    foreach (var key in keys)
                    {
                        if (!string.IsNullOrEmpty(key))
                        {
                            _values.TryGet(CreateId(collection, key), out var value);
                            values.Add(key, value);
                        }
                    }
                }

                if (!values.IsNullOrEmpty())
                {
                    var response = new TrakHoundKeyValueResponse();
                    response.Collection = collection;
                    response.Values = values;
                    return TrakHoundServiceResponse<TrakHoundKeyValueResponse>.Ok(response);
                }
                else
                {
                    return TrakHoundServiceResponse<TrakHoundKeyValueResponse>.NotFound();
                }
            }
            else
            {
                return TrakHoundServiceResponse<TrakHoundKeyValueResponse>.BadRequest();
            }
        }

        public async Task<TrakHoundServiceResponse<int>> Count(string collection, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(collection))
            {
                var count = _collectionKeys.GetCount(collection);
                if (count >= 0)
                {
                    return TrakHoundServiceResponse<int>.Ok(count);
                }
                else
                {
                    return TrakHoundServiceResponse<int>.NotFound();
                }
            }
            else
            {
                return TrakHoundServiceResponse<int>.BadRequest();
            }
        }

        public async Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(collection) && !entries.IsNullOrEmpty())
            {
                lock (_lock)
                {
                    foreach (var entry in entries)
                    {
                        _collectionKeys.Add(collection, entry.Key);
                        
                        var id = CreateId(collection, entry.Key);
                        
                        if (!_keys.ContainsKey(id)) _keys.Add(id, entry.Key);
                        if (!_collections.ContainsKey(id)) _collections.Add(id, collection);

                        if (ttl > TimeSpan.Zero) _values.Add(id, entry.Value, ttl.Value);
                        else _values.Add(id, entry.Value);
                    }
                }

                return TrakHoundServiceResponse.Ok();
            }
            else
            {
                return TrakHoundServiceResponse.BadRequest();
            }
        }

        public async Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(collection) && !keys.IsNullOrEmpty())
            {
                var count = 0;

                lock (_lock)
                {
                    foreach (var key in keys)
                    {
                        _collectionKeys.Remove(collection, key);

                        var id = CreateId(collection, key);
                        _keys.Remove(id);
                        _collections.Remove(id);
                        _values.Remove(id);
                        count++;
                    }
                }

                return TrakHoundServiceResponse.Ok($"{count} Keys Deleted Successfully");
            }
            else
            {
                return TrakHoundServiceResponse.BadRequest();
            }
        }

        private static string CreateId(string collection, string key)
        {
            return $"{collection}:{key}".ToLower();
        }
    }
}
