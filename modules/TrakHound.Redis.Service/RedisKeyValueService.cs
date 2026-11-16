using StackExchange.Redis;
using TrakHound.Services;
using TrakHound.Services.KeyValues;

namespace TrakHound.Redis.Driver
{
    public class RedisKeyValueService :
        RedisService,
        ITrakHoundKeyValueService
    {
        private const int DefaultPort = 6379;
        private const int ScanPageSize = 1000;

        private readonly ConfigurationOptions _options;
        private readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1);
        private ConnectionMultiplexer _connection;


        public RedisKeyValueService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            _options = CreateOptions(configuration);
        }

        protected override void OnDisposed()
        {
            _connection?.Dispose();
            _connectLock.Dispose();
        }

        private static ConfigurationOptions CreateOptions(ITrakHoundServiceConfiguration configuration)
        {
            var server = configuration?.GetParameter("server");
            if (string.IsNullOrEmpty(server)) return null;

            var port = configuration.GetParameter<int>("port");

            var options = new ConfigurationOptions();
            options.EndPoints.Add(server, port > 0 ? port : DefaultPort);
            options.DefaultDatabase = configuration.GetParameter<int>("database");
            options.User = configuration.GetParameter("user");
            options.Password = configuration.GetParameter("password");
            options.Ssl = configuration.GetParameter<bool>("useSsl");
            options.ConnectTimeout = 5000;
            options.AsyncTimeout = 5000;
            options.SyncTimeout = 5000;
            options.AbortOnConnectFail = false;
            return options;
        }

        private async Task<ConnectionMultiplexer> GetConnection()
        {
            if (_connection != null) return _connection;

            await _connectLock.WaitAsync();
            try
            {
                if (_connection == null) _connection = await ConnectionMultiplexer.ConnectAsync(_options);
                return _connection;
            }
            finally
            {
                _connectLock.Release();
            }
        }


        public override async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var response = new TrakHoundServiceAvailabilityResponse();

            if (_options != null)
            {
                try
                {
                    var connection = await GetConnection();
                    await connection.GetDatabase().PingAsync();
                    response.IsAvailable = true;
                }
                catch (Exception ex)
                {
                    response.Message = $"Connect : Connection Error : {ex.Message}";
                }
            }
            else
            {
                response.Message = "Redis Connection Not Configured";
            }

            return TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.Ok(response);
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_options == null) return TrakHoundServiceResponse<IEnumerable<string>>.NotAvailable("Redis Connection Not Configured");

            try
            {
                var redisKeys = await ScanKeys("*", cancellationToken);
                if (redisKeys == null) return TrakHoundServiceResponse<IEnumerable<string>>.NotAvailable("No Redis Server Connected");

                var collections = redisKeys
                    .Select(GetCollectionName)
                    .Where(o => o != null)
                    .Distinct()
                    .OrderBy(o => o, StringComparer.Ordinal)
                    .ToList();

                if (collections.Count > 0)
                {
                    return TrakHoundServiceResponse<IEnumerable<string>>.Ok(collections);
                }
                else
                {
                    return TrakHoundServiceResponse<IEnumerable<string>>.NotFound();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Error<IEnumerable<string>>(ex);
            }
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_options == null) return TrakHoundServiceResponse<IEnumerable<string>>.NotAvailable("Redis Connection Not Configured");

            if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(pattern))
            {
                try
                {
                    var prefix = CreatePrefix(collection);

                    var redisKeys = await ScanKeys(prefix + EscapePattern(pattern), cancellationToken);
                    if (redisKeys == null) return TrakHoundServiceResponse<IEnumerable<string>>.NotAvailable("No Redis Server Connected");

                    // SCAN returns Keys in no particular order, so every Key is read and sorted for the pages to be stable
                    var keys = redisKeys
                        .Select(o => o.Substring(prefix.Length))
                        .OrderBy(o => o, StringComparer.Ordinal)
                        .Skip(Math.Max(skip, 0))
                        .Take(Math.Max(take, 0))
                        .ToList();

                    if (keys.Count > 0)
                    {
                        return TrakHoundServiceResponse<IEnumerable<string>>.Ok(keys);
                    }
                    else
                    {
                        return TrakHoundServiceResponse<IEnumerable<string>>.NotFound();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Error<IEnumerable<string>>(ex);
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

            if (_options == null) return TrakHoundServiceResponse<TrakHoundKeyValueResponse>.NotAvailable("Redis Connection Not Configured");

            if (!string.IsNullOrEmpty(collection) && !keys.IsNullOrEmpty())
            {
                try
                {
                    var database = (await GetConnection()).GetDatabase();

                    var outputKeys = keys.ToArray();
                    var redisKeys = outputKeys.Select(o => new RedisKey(CreateKey(collection, o))).ToArray();

                    var redisValues = await database.StringGetAsync(redisKeys);
                    if (!redisValues.IsNullOrEmpty())
                    {
                        var values = new Dictionary<string, string>();

                        for (var i = 0; i < outputKeys.Length; i++)
                        {
                            var key = outputKeys[i];
                            var value = redisValues[i];

                            if (!string.IsNullOrEmpty(key) && !values.ContainsKey(key))
                            {
                                values.Add(key, value);
                            }
                        }

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
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Error<TrakHoundKeyValueResponse>(ex);
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

            if (_options == null) return TrakHoundServiceResponse<int>.NotAvailable("Redis Connection Not Configured");

            if (!string.IsNullOrEmpty(collection))
            {
                try
                {
                    var redisKeys = await ScanKeys(CreatePrefix(collection) + "*", cancellationToken);
                    if (redisKeys == null) return TrakHoundServiceResponse<int>.NotAvailable("No Redis Server Connected");

                    return TrakHoundServiceResponse<int>.Ok(redisKeys.Count);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Error<int>(ex);
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

            if (_options == null) return TrakHoundServiceResponse.NotAvailable("Redis Connection Not Configured");

            if (!string.IsNullOrEmpty(collection) && !entries.IsNullOrEmpty())
            {
                try
                {
                    var database = (await GetConnection()).GetDatabase();

                    bool success;
                    if (ttl > TimeSpan.Zero)
                    {
                        // Set with the Value, since a Key set separately from its expiration would never expire if the second call failed
                        var results = await Task.WhenAll(entries.Select(o => database.StringSetAsync(CreateKey(collection, o.Key), o.Value, ttl)));
                        success = results.All(o => o);
                    }
                    else
                    {
                        var redisValues = entries.Select(o => new KeyValuePair<RedisKey, RedisValue>(CreateKey(collection, o.Key), o.Value)).ToArray();
                        success = await database.StringSetAsync(redisValues);
                    }

                    if (success)
                    {
                        return TrakHoundServiceResponse.Ok();
                    }
                    else
                    {
                        return TrakHoundServiceResponse.InternalError("Error During Add");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Error(ex);
                }
            }
            else
            {
                return TrakHoundServiceResponse.BadRequest();
            }
        }

        public async Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_options == null) return TrakHoundServiceResponse.NotAvailable("Redis Connection Not Configured");

            if (!string.IsNullOrEmpty(collection) && !keys.IsNullOrEmpty())
            {
                try
                {
                    var database = (await GetConnection()).GetDatabase();

                    var redisKeys = keys.Select(o => new RedisKey(CreateKey(collection, o))).ToArray();

                    var count = await database.KeyDeleteAsync(redisKeys);
                    return TrakHoundServiceResponse.Ok($"{count} Keys Deleted Successfully");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Error(ex);
                }
            }
            else
            {
                return TrakHoundServiceResponse.BadRequest();
            }
        }


        // 503 when Redis can't be reached and 504 when it doesn't answer in time, so that a Service Router can try its next Target
        private static TrakHoundServiceResponse Error(Exception ex) => ex switch
        {
            RedisTimeoutException => TrakHoundServiceResponse.Timeout(ex.Message),
            RedisConnectionException => TrakHoundServiceResponse.NotAvailable(ex.Message),
            _ => TrakHoundServiceResponse.InternalError(ex.Message)
        };

        private static TrakHoundServiceResponse<T> Error<T>(Exception ex) => ex switch
        {
            RedisTimeoutException => TrakHoundServiceResponse<T>.Timeout(ex.Message),
            RedisConnectionException => TrakHoundServiceResponse<T>.NotAvailable(ex.Message),
            _ => TrakHoundServiceResponse<T>.InternalError(ex.Message)
        };


        /// <summary>
        /// Reads the Keys that match the pattern from every primary server. Returns null if none are connected
        /// </summary>
        private async Task<HashSet<string>> ScanKeys(string pattern, CancellationToken cancellationToken)
        {
            var connection = await GetConnection();

            var servers = connection.GetServers().Where(o => o.IsConnected && !o.IsReplica).ToList();
            if (servers.Count < 1) return null;

            var database = connection.GetDatabase().Database;

            var keys = new HashSet<string>();
            foreach (var server in servers)
            {
                await foreach (var key in server.KeysAsync(database, pattern, ScanPageSize).WithCancellation(cancellationToken))
                {
                    keys.Add(key);
                }
            }
            return keys;
        }

        // Collection names are escaped so that a ":" can't move the boundary between the Collection and the Key,
        // and so that they contain no pattern characters when used to scan
        private static string CreatePrefix(string collection) => $"{Uri.EscapeDataString(collection)}:";

        private static string CreateKey(string collection, string key) => CreatePrefix(collection) + key;

        private static string GetCollectionName(string redisKey)
        {
            var i = redisKey.IndexOf(':');
            return i > 0 ? Uri.UnescapeDataString(redisKey.Substring(0, i)) : null;
        }

        // "*" is the only wildcard that Key Value Services support, so the others Redis would match on are escaped
        private static string EscapePattern(string pattern)
        {
            return pattern
                .Replace("\\", "\\\\")
                .Replace("?", "\\?")
                .Replace("[", "\\[")
                .Replace("]", "\\]");
        }
    }
}
