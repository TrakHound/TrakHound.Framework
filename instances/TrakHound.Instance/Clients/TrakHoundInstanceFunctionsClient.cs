// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Functions;
using TrakHound.Logging;
using TrakHound.Zones;

namespace TrakHound.Clients
{
    public class TrakHoundInstanceFunctionsClient : ITrakHoundFunctionsClient, IDisposable
    {
        private readonly TrakHoundInstanceClient _baseClient;
        private readonly TrakHoundFunctionManager _functionManager;
        private readonly Dictionary<string, TrakHoundStream<TrakHoundFunctionStatusResponse>> _statusConsumers = new Dictionary<string, TrakHoundStream<TrakHoundFunctionStatusResponse>>(); // ConsumerId => Consumer
        private readonly Dictionary<string, TrakHoundStream<IEnumerable<TrakHoundLogItem>>> _logConsumers = new Dictionary<string, TrakHoundStream<IEnumerable<TrakHoundLogItem>>>(); // ConsumerId => Consumer
        private readonly ListDictionary<string, string> _consumerRunIds = new ListDictionary<string, string>(); // RunId => ConsumerId
        private readonly Dictionary<string, TrakHoundLogLevel> _minimumLogLevels = new Dictionary<string, TrakHoundLogLevel>(); // ConsumerId => Minimum Log Level
        private readonly object _lock = new object();
        private int _consumerCount;


        public TrakHoundInstanceFunctionsClient(TrakHoundInstanceClient baseClient, TrakHoundFunctionManager functionManager)
        {
            _baseClient = baseClient;
            _functionManager = functionManager;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_consumerCount > 0) Unsubscribe();
                _consumerCount = 0;
            }
        }

        // The Function Manager's events are only subscribed to while there are Consumers, so a Client that isn't disposed (ex. one created per Request) isn't kept alive by them
        private void AddConsumer()
        {
            if (_consumerCount++ == 0)
            {
                _functionManager.StatusUpdated += StatusUpdated;
                _functionManager.LogUpdated += LogUpdated;
            }
        }

        private void RemoveConsumer()
        {
            if (_consumerCount > 0 && --_consumerCount == 0) Unsubscribe();
        }

        private void Unsubscribe()
        {
            _functionManager.StatusUpdated -= StatusUpdated;
            _functionManager.LogUpdated -= LogUpdated;
        }


        public async Task<IEnumerable<TrakHoundFunctionInformation>> GetInformation()
        {
            return _functionManager.GetInformation();
        }

        public async Task<TrakHoundFunctionInformation> GetInformation(string functionId)
        {
            return _functionManager.GetInformation(functionId);
        }

        public async Task<IEnumerable<TrakHoundFunctionInformation>> GetInformation(IEnumerable<string> functionIds)
        {
            return _functionManager.GetInformation(functionIds);
        }


        public Task<IEnumerable<TrakHoundFunctionMetrics>> GetMetrics()
        {
            return Task.FromResult(_functionManager.GetMetrics());
        }

        public Task<TrakHoundFunctionMetrics> GetMetrics(string functionId)
        {
            return Task.FromResult(_functionManager.GetMetrics(functionId));
        }


        public async Task<TrakHoundFunctionResponse> Run(string functionId, IReadOnlyDictionary<string, string> inputParameters = null, string runId = null, long timestamp = 0)
        {
            if (!string.IsNullOrEmpty(functionId))
            {
                // Functions are Run by their Function ID, but Zones refer to them by their Configuration ID
                var configurationId = _functionManager.GetConfiguration(functionId)?.Id;
                if (configurationId != null)
                {
                    var zoneDecision = _baseClient.AuthorizeZone(
                        TrakHoundZoneResources.GetMemberId(TrakHoundZoneResources.FunctionPrefix, configurationId),
                        TrakHoundZoneResources.GetFunctionResourceId(configurationId),
                        TrakHoundZoneResources.RunPermission);

                    if (!zoneDecision.IsAllowed) return new TrakHoundFunctionResponse(403, zoneDecision.Message);
                }

                return await _functionManager.Run(functionId, inputParameters, runId, timestamp);
            }

            return default;
        }

        public Task<bool> CancelRun(string functionId, string runId)
        {
            return Task.FromResult(_functionManager.CancelRun(runId));
        }

        public async Task<ITrakHoundStream<TrakHoundFunctionStatusResponse>> SubscribeRunStatus(string runId)
        {
            if (!string.IsNullOrEmpty(runId))
            {
                var consumer = new TrakHoundStream<TrakHoundFunctionStatusResponse>();

                lock (_lock)
                {
                    _statusConsumers.Add(consumer.Id, consumer);
                    _consumerRunIds.Add(runId, consumer.Id);
                    AddConsumer();
                }

                consumer.OnDisposed = () =>
                {
                    lock (_lock)
                    {
                        if (_statusConsumers.Remove(consumer.Id)) RemoveConsumer();
                        _consumerRunIds.Remove(runId, consumer.Id);
                    }
                };

                return consumer;
            }

            return default;
        }

        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeRunLog(string runId, TrakHoundLogLevel minimumLogLevel  = TrakHoundLogLevel.Trace)
        {
            if (!string.IsNullOrEmpty(runId))
            {
                var consumer = new TrakHoundStream<IEnumerable<TrakHoundLogItem>>();

                lock (_lock)
                {
                    _logConsumers.Add(consumer.Id, consumer);
                    _minimumLogLevels.Add(consumer.Id, minimumLogLevel);
                    _consumerRunIds.Add(runId, consumer.Id);
                    AddConsumer();
                }

                consumer.OnDisposed = () =>
                {
                    lock (_lock)
                    {
                        if (_logConsumers.Remove(consumer.Id)) RemoveConsumer();
                        _minimumLogLevels.Remove(consumer.Id);
                        _consumerRunIds.Remove(runId, consumer.Id);
                    }
                };

                return consumer;
            }

            return default;
        }


        private void StatusUpdated(string runId, TrakHoundFunctionStatusType status)
        {
            IEnumerable<string> consumerIds;
            lock (_lock) consumerIds = _consumerRunIds.Get(runId);
            if (!consumerIds.IsNullOrEmpty())
            {
                var response = new TrakHoundFunctionStatusResponse();
                response.Status = status;
                response.Timestamp = UnixDateTime.Now;

                foreach (var consumerId in consumerIds)
                {
                    TrakHoundStream<TrakHoundFunctionStatusResponse> consumer;
                    lock (_lock) consumer = _statusConsumers.GetValueOrDefault(consumerId);
                    if (consumer != null)
                    {
                        consumer.Push(response);
                    }
                }
            }
        }

        private void LogUpdated(string runId, TrakHoundLogItem item)
        {
            IEnumerable<string> consumerIds;
            lock (_lock) consumerIds = _consumerRunIds.Get(runId);
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
    }
}
