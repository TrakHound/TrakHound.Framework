// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;
using TrakHound.Zones;

namespace TrakHound.Clients
{
    public class TrakHoundInstanceInstancesClient : ITrakHoundInstancesClient, IDisposable
    {
        private readonly TrakHoundInstanceClient _baseClient;
        private readonly ITrakHoundInstance _instance;
        private readonly Dictionary<string, TrakHoundStream<TrakHoundInstanceInformation>> _statusStreams = new Dictionary<string, TrakHoundStream<TrakHoundInstanceInformation>>(); // StreamId => Stream
        private readonly ListDictionary<string, string> _streamInstanceIds = new ListDictionary<string, string>(); // InstanceId => StreamId
        private readonly object _lock = new object();


        public TrakHoundInstanceInstancesClient(TrakHoundInstanceClient baseClient, ITrakHoundInstance instance)
        {
            _baseClient = baseClient;
            _instance = instance;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_statusStreams.Count > 0) _instance.StatusUpdated -= StatusUpdated;
                _statusStreams.Clear();
            }
        }


        private void StatusUpdated(string instanceId, TrakHoundInstanceStatus status)
        {
            IEnumerable<string> consumerIds;
            lock (_lock) consumerIds = _streamInstanceIds.Get(instanceId);
            if (!consumerIds.IsNullOrEmpty())
            {
                var instanceInformation = _instance.Information;

                foreach (var consumerId in consumerIds)
                {
                    TrakHoundStream<TrakHoundInstanceInformation> consumer;
                    lock (_lock) consumer = _statusStreams.GetValueOrDefault(consumerId);
                    if (consumer != null)
                    {
                        consumer.Push(instanceInformation);
                    }
                }
            }
        }

        public async Task<TrakHoundInstanceInformation> GetHostInformation()
        {
            return _instance.Information;
        }

        public Task<TrakHoundInstanceHealth> GetHealth()
        {
            return Task.FromResult(_instance.Health?.GetHealth());
        }

        public Task<TrakHoundCpuUsage> GetCpuUsage()
        {
            return Task.FromResult(TrakHoundModuleCpuMonitors.Default.GetUsage());
        }

        public Task<IEnumerable<TrakHoundInstanceBoot>> GetBoots(int take = 100)
        {
            var history = TrakHound.Instance.TrakHoundInstanceLifetimeMonitor.BootHistory;
            if (history == null) return Task.FromResult<IEnumerable<TrakHoundInstanceBoot>>(Enumerable.Empty<TrakHoundInstanceBoot>());

            return Task.FromResult<IEnumerable<TrakHoundInstanceBoot>>(history.GetBoots().Take(Math.Max(0, take)).ToList());
        }

        public Task<TrakHoundInstanceBoot> GetBoot(string bootId)
        {
            return Task.FromResult(TrakHound.Instance.TrakHoundInstanceLifetimeMonitor.BootHistory?.GetBoot(bootId));
        }


        public async Task<ITrakHoundStream<TrakHoundInstanceInformation>> SubscribeInformation(string instanceId)
        {
            if (!string.IsNullOrEmpty(instanceId))
            {
                if (instanceId == _instance.Id)
                {
                    var consumer = new TrakHoundStream<TrakHoundInstanceInformation>();

                    lock (_lock)
                    {
                        // Only subscribed to while there are Streams, so a Client that isn't disposed (ex. one created per Request) isn't kept alive by the Instance
                        if (_statusStreams.Count == 0) _instance.StatusUpdated += StatusUpdated;
                        _statusStreams.Add(consumer.Id, consumer);
                        _streamInstanceIds.Add(instanceId, consumer.Id);
                    }

                    consumer.OnDisposed = () =>
                    {
                        lock (_lock)
                        {
                            if (_statusStreams.Remove(consumer.Id) && _statusStreams.Count == 0) _instance.StatusUpdated -= StatusUpdated;
                            _streamInstanceIds.Remove(instanceId, consumer.Id);
                        }
                    };

                    return consumer;
                }
            }

            return default;
        }


        public async Task Start(string instanceId)
        {
            if (instanceId == _instance.Id && IsManageAllowed(instanceId))
            {
                await _instance.Start();
            }
        }

        public async Task Stop(string instanceId)
        {
            if (instanceId == _instance.Id && IsManageAllowed(instanceId))
            {
                await _instance.Stop();
            }
        }

        private bool IsManageAllowed(string instanceId)
        {
            return _baseClient.AuthorizeZone(null, TrakHoundZoneResources.GetManageResourceId("instance", instanceId), TrakHoundZoneResources.WritePermission).IsAllowed;
        }
    }
}
