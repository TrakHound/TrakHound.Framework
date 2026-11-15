// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Logging;
using TrakHound.Workers;
using TrakHound.Zones;

namespace TrakHound.Clients
{
    public class TrakHoundInstanceWorkersClient : ITrakHoundWorkersClient, IDisposable
    {
        private const string _all = "ALL";

        private readonly TrakHoundInstanceClient _baseClient;
        private readonly TrakHoundWorkerManager _workerManager;
        private readonly ITrakHoundLogProvider _logProvider;
        private readonly Dictionary<string, TrakHoundStream<TrakHoundWorkerStatusResponse>> _statusConsumers = new Dictionary<string, TrakHoundStream<TrakHoundWorkerStatusResponse>>(); // ConsumerId => Consumer
        private readonly Dictionary<string, TrakHoundStream<IEnumerable<TrakHoundLogItem>>> _logConsumers = new Dictionary<string, TrakHoundStream<IEnumerable<TrakHoundLogItem>>>(); // ConsumerId => Consumer
        private readonly ListDictionary<string, string> _consumerWorkerIds = new ListDictionary<string, string>(); // WorkerId => ConsumerId
        private readonly Dictionary<string, TrakHoundLogLevel> _minimumLogLevels = new Dictionary<string, TrakHoundLogLevel>(); // ConsumerId => Minimum Log Level
        private readonly object _lock = new object();
        private int _consumerCount;


        public TrakHoundInstanceWorkersClient(TrakHoundInstanceClient baseClient, TrakHoundWorkerManager workerManager, ITrakHoundLogProvider logProvider)
        {
            _baseClient = baseClient;
            _workerManager = workerManager;
            _logProvider = logProvider;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_consumerCount > 0) Unsubscribe();
                _consumerCount = 0;
            }
        }

        // The Worker Manager's events are only subscribed to while there are Consumers, so a Client that isn't disposed (ex. one created per Request) isn't kept alive by them
        private void AddConsumer()
        {
            if (_consumerCount++ == 0)
            {
                _workerManager.StatusReceived += StatusUpdated;
                _workerManager.LogReceived += LogUpdated;
            }
        }

        private void RemoveConsumer()
        {
            if (_consumerCount > 0 && --_consumerCount == 0) Unsubscribe();
        }

        private void Unsubscribe()
        {
            _workerManager.StatusReceived -= StatusUpdated;
            _workerManager.LogReceived -= LogUpdated;
        }


        public async Task<IEnumerable<TrakHoundWorkerInformation>> GetInformation()
        {
            return _workerManager.GetInformation();
        }

        public async Task<TrakHoundWorkerInformation> GetInformation(string workerId)
        {
            return _workerManager.GetInformation(workerId);
        }

        public async Task<IEnumerable<TrakHoundWorkerInformation>> GetInformation(IEnumerable<string> workerIds)
        {
            return _workerManager.GetInformation(workerIds);
        }

        public async Task<IEnumerable<TrakHoundWorkerCpuSample>> GetCpuHistory(string workerId, DateTime? from = null)
        {
            return _workerManager.GetCpuHistory(workerId, from);
        }


        public Task<IEnumerable<TrakHoundWorkerMetrics>> GetMetrics()
        {
            return Task.FromResult(_workerManager.GetMetrics());
        }

        public Task<TrakHoundWorkerMetrics> GetMetrics(string workerId)
        {
            return Task.FromResult(_workerManager.GetMetrics(workerId));
        }

        public async Task<ITrakHoundStream<TrakHoundWorkerInformation>> Subscribe()
        {
            var consumer = new TrakHoundStream<TrakHoundWorkerInformation>();

            TrakHoundWorkerStatusHandler handler = async (id, status) =>
            {
                var information = await GetInformation(id);
                await consumer.Push(information);
            };

            _workerManager.StatusReceived += handler;
            consumer.OnDisposed = () => _workerManager.StatusReceived -= handler;

            return consumer;
        }

        public async Task<ITrakHoundStream<TrakHoundWorkerInformation>> Subscribe(string workerId)
        {
            if (!string.IsNullOrEmpty(workerId))
            {
                var consumer = new TrakHoundStream<TrakHoundWorkerInformation>();

                TrakHoundWorkerStatusHandler handler = async (id, status) =>
                {
                    if (id == workerId)
                    {
                        var information = await GetInformation(workerId);
                        await consumer.Push(information);
                    }
                };

                _workerManager.StatusReceived += handler;
                consumer.OnDisposed = () => _workerManager.StatusReceived -= handler;

                return consumer;
            }

            return null;
        }

        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeToLog(string workerId, TrakHoundLogLevel minimumLevel = TrakHoundLogLevel.Information)
        {
            if (!string.IsNullOrEmpty(workerId))
            {
                var worker = _workerManager.GetEngine(workerId);
                if (worker != null)
                {
                    var logFilter = new[] { $"workers/{worker.PackageId}/{workerId}/*" };
                    return TrakHoundInstanceLogSubscription.Subscribe(_logProvider, logFilter, minimumLevel);
                }
            }

            return null;
        }

        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeToLog(string workerId, string filter, TrakHoundLogLevel minimumLevel = TrakHoundLogLevel.Information)
        {
            if (!string.IsNullOrEmpty(workerId) && !string.IsNullOrEmpty(filter))
            {
                var worker = _workerManager.GetEngine(workerId);
                if (worker != null)
                {
                    var logFilter = new[] { $"workers/{worker.PackageId}/{workerId}/{filter}" };
                    return TrakHoundInstanceLogSubscription.Subscribe(_logProvider, logFilter, minimumLevel);
                }
            }

            return null;
        }


        public async Task<IEnumerable<string>> Retrieve(int skip = 0, int take = 100, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
		{
			return _workerManager.Engines?.Select(o => o.EngineId);
		}

		public async Task<IEnumerable<TrakHoundWorkerStatus>> GetStatus(IEnumerable<string> workerIds)
        {
            if (!workerIds.IsNullOrEmpty())
            {
                var engines = _workerManager.Engines?.Where(o => workerIds.Contains(o.EngineId));
				if (!engines.IsNullOrEmpty())
				{
					var statuses = new List<TrakHoundWorkerStatus>();
					foreach (var engine in engines)
					{
						statuses.Add(new TrakHoundWorkerStatus(engine.EngineId, engine.Status, null));
					}
					return statuses;
				}
			}

			return null;
        }

        public async Task<TrakHoundWorkerStatus> StartWorker(string workerId)
        {
            if (!string.IsNullOrEmpty(workerId))
            {
				var engine = _workerManager.GetEngine(workerId);
				if (engine != null && IsRunAllowed(workerId))
				{
					engine.Start();
					return new TrakHoundWorkerStatus(engine.EngineId, engine.Status, null);
				}
            }

            return default;
        }

        public async Task<TrakHoundWorkerStatus> StopWorker(string workerId)
        {
            if (!string.IsNullOrEmpty(workerId))
            {
                var engine = _workerManager.GetEngine(workerId);
                if (engine != null && IsRunAllowed(workerId))
                {
                    await engine.Stop();
                    return new TrakHoundWorkerStatus(engine.EngineId, engine.Status, null);
                }
            }

            return default;
        }

        // Workers are Started and Stopped by their Worker ID, but Zones refer to them by their Configuration ID
        private bool IsRunAllowed(string workerId)
        {
            var configurationId = _workerManager.GetConfiguration(workerId)?.Id;
            if (configurationId == null) return true;

            return _baseClient.AuthorizeZone(
                TrakHoundZoneResources.GetMemberId(TrakHoundZoneResources.WorkerPrefix, configurationId),
                TrakHoundZoneResources.GetWorkerResourceId(configurationId),
                TrakHoundZoneResources.RunPermission).IsAllowed;
        }


        public async Task<IEnumerable<string>> QueryLogNames(string workerId)
        {
            var worker = _workerManager.GetEngine(workerId);
            if (worker != null)
            {
                var pattern = $"workers/{worker.PackageId}/{workerId}/*";
                var loggerIds = _logProvider.QueryLoggerIds(pattern);
                if (!loggerIds.IsNullOrEmpty())
                {
                    return loggerIds.Select(o => GetLogName(worker.PackageId, workerId, o));
                }
            }

            return null;
        }

        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogs(string workerId, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            var worker = _workerManager.GetEngine(workerId);
            if (worker != null)
            {
                var loggerName = $"workers/{worker.PackageId}/{workerId}/*";
                return _logProvider.QueryLogs(loggerName, from, to, skip, take, sortOrder);
            }

            return null;
        }

        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogsByMinimumLevel(string workerId, TrakHoundLogLevel minimumLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            var worker = _workerManager.GetEngine(workerId);
            if (worker != null)
            {
                var loggerName = $"workers/{worker.PackageId}/{workerId}/*";
                return _logProvider.QueryLogsByMinimumLevel(loggerName, minimumLevel, from, to, skip, take, sortOrder);
            }

            return null;
        }

        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogsByLevel(string workerId, TrakHoundLogLevel level, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            var worker = _workerManager.GetEngine(workerId);
            if (worker != null)
            {
                var loggerName = $"workers/{worker.PackageId}/{workerId}/*";
                return _logProvider.QueryLogsByLevel(loggerName, level, from, to, skip, take, sortOrder);
            }

            return null;
        }

        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogs(string workerId, string logName, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            var worker = _workerManager.GetEngine(workerId);
            if (worker != null)
            {
                var loggerName = $"workers/{worker.PackageId}/{workerId}/{logName}/*";
                return _logProvider.QueryLogs(loggerName, from, to, skip, take, sortOrder);
            }

            return null;
        }

        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogsByMinimumLevel(string workerId, string logName, TrakHoundLogLevel minimumLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            var worker = _workerManager.GetEngine(workerId);
            if (worker != null)
            {
                var loggerName = $"workers/{worker.PackageId}/{workerId}/{logName}/*";
                return _logProvider.QueryLogsByMinimumLevel(loggerName, minimumLevel, from, to, skip, take, sortOrder);
            }

            return null;
        }

        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogsByLevel(string workerId, string logName, TrakHoundLogLevel level, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            var worker = _workerManager.GetEngine(workerId);
            if (worker != null)
            {
                var loggerName = $"workers/{worker.PackageId}/{workerId}/{logName}/*";
                return _logProvider.QueryLogsByLevel(loggerName, level, from, to, skip, take, sortOrder);
            }

            return null;
        }


        public async Task<ITrakHoundStream<TrakHoundWorkerStatusResponse>> SubscribeStatus()
        {
            var consumer = new TrakHoundStream<TrakHoundWorkerStatusResponse>();

            lock (_lock)
            {
                _statusConsumers.Add(consumer.Id, consumer);
                _consumerWorkerIds.Add(_all, consumer.Id);
                AddConsumer();
            }

            consumer.OnDisposed = () =>
            {
                lock (_lock)
                {
                    if (_statusConsumers.Remove(consumer.Id)) RemoveConsumer();
                    _consumerWorkerIds.Remove(_all, consumer.Id);
                }
            };

            return consumer;
        }

        public async Task<ITrakHoundStream<TrakHoundWorkerStatusResponse>> SubscribeStatus(string workerId)
        {
            if (!string.IsNullOrEmpty(workerId))
            {
                var consumer = new TrakHoundStream<TrakHoundWorkerStatusResponse>();

                lock (_lock)
                {
                    _statusConsumers.Add(consumer.Id, consumer);
                    _consumerWorkerIds.Add(workerId, consumer.Id);
                    AddConsumer();
                }

                consumer.OnDisposed = () =>
                {
                    lock (_lock)
                    {
                        if (_statusConsumers.Remove(consumer.Id)) RemoveConsumer();
                        _consumerWorkerIds.Remove(workerId, consumer.Id);
                    }
                };

                return consumer;
            }

            return default;
        }

        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeLog(TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace)
        {
            var consumer = new TrakHoundStream<IEnumerable<TrakHoundLogItem>>();

            lock (_lock)
            {
                _logConsumers.Add(consumer.Id, consumer);
                _minimumLogLevels.Add(consumer.Id, minimumLogLevel);
                _consumerWorkerIds.Add(_all, consumer.Id);
                AddConsumer();
            }

            consumer.OnDisposed = () =>
            {
                lock (_lock)
                {
                    if (_logConsumers.Remove(consumer.Id)) RemoveConsumer();
                    _minimumLogLevels.Remove(consumer.Id);
                    _consumerWorkerIds.Remove(_all, consumer.Id);
                }
            };

            return consumer;
        }

        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeLog(string workerId, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace)
        {
            if (!string.IsNullOrEmpty(workerId))
            {
                var consumer = new TrakHoundStream<IEnumerable<TrakHoundLogItem>>();

                lock (_lock)
                {
                    _logConsumers.Add(consumer.Id, consumer);
                    _minimumLogLevels.Add(consumer.Id, minimumLogLevel);
                    _consumerWorkerIds.Add(workerId, consumer.Id);
                    AddConsumer();
                }

                consumer.OnDisposed = () =>
                {
                    lock (_lock)
                    {
                        if (_logConsumers.Remove(consumer.Id)) RemoveConsumer();
                        _minimumLogLevels.Remove(consumer.Id);
                        _consumerWorkerIds.Remove(workerId, consumer.Id);
                    }
                };

                return consumer;
            }

            return default;
        }


        private void StatusUpdated(string workerId, TrakHoundWorkerStatusType status)
        {
            var consumerIds = GetConsumerIds(workerId);
            if (!consumerIds.IsNullOrEmpty())
            {
                var response = new TrakHoundWorkerStatusResponse();
                response.WorkerId = workerId;
                response.Status = status;
                response.Timestamp = UnixDateTime.Now;

                foreach (var consumerId in consumerIds)
                {
                    TrakHoundStream<TrakHoundWorkerStatusResponse> consumer;
                    lock (_lock) consumer = _statusConsumers.GetValueOrDefault(consumerId);
                    if (consumer != null)
                    {
                        consumer.Push(response);
                    }
                }
            }
        }

        private void LogUpdated(string workerId, TrakHoundLogItem item)
        {
            var consumerIds = GetConsumerIds(workerId);
            if (!consumerIds.IsNullOrEmpty())
            {
                var items = new TrakHoundLogItem[] { item };

                foreach (var consumerId in consumerIds)
                {
                    TrakHoundLogLevel minimumLogLevel;
                    TrakHoundStream<IEnumerable<TrakHoundLogItem>> consumer;

                    lock (_lock)
                    {
                        minimumLogLevel = _minimumLogLevels.GetValueOrDefault(consumerId);
                        consumer = _logConsumers.GetValueOrDefault(consumerId);
                    }

                    if (consumer != null && item.LogLevel <= minimumLogLevel)
                    {
                        consumer.Push(items);
                    }
                }
            }
        }

        private IEnumerable<string> GetConsumerIds(string workerId)
        {
            var consumerIds = new List<string>();

            lock (_lock)
            {
                var all = _consumerWorkerIds.Get(_all);
                if (!all.IsNullOrEmpty()) consumerIds.AddRange(all);

                var matched = _consumerWorkerIds.Get(workerId);
                if (!matched.IsNullOrEmpty()) consumerIds.AddRange(matched);
            }

            return consumerIds;
        }


        private string GetLogName(string packageId, string workerId, string loggerId)
        {
            if (!string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(loggerId))
            {
                var prefix = _logProvider.GetLoggerId($"workers/{packageId}/{workerId}");
                if (loggerId != prefix)
                {
                    return loggerId.Substring(prefix.Length + 1);
                }
                else
                {
                    return "main";
                }
            }

            return null;
        }
    }
}
