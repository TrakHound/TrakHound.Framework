// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using MQTTnet;
using MQTTnet.Client;
using System.Collections.Concurrent;
using TrakHound.Logging;
using TrakHound.Services;

namespace TrakHound.Mqtt.Service
{
    /// <summary>
    /// A Stream backed by its own MQTT connection. Once started, it connects, subscribes to its Topics and
    /// reconnects (and resubscribes) after a disconnection or a failed subscription until stopped or disposed.
    /// </summary>
    public class MqttStream<T> : ITrakHoundStream<T>
    {
        private readonly MqttConnectionConfiguration _connectionConfiguration;
        private readonly ConcurrentDictionary<string, MqttSubscription> _subscriptions = new ConcurrentDictionary<string, MqttSubscription>();
        private readonly string _clientId;
        private readonly IMqttClient _mqttClient;
        private readonly object _lock = new object();
        private CancellationTokenSource _stop;
        private Task _worker;
        private TaskCompletionSource _disconnected;
        private bool _disposed;


        public delegate T MqttMessageReceivedHandler(MqttApplicationMessage message);


        public string Id { get; }

        public CancellationToken CancellationToken => _stop?.Token ?? default;

        public string ClientId => _clientId;

        public Type OutputType => typeof(T);


        public event EventHandler<T> Received;

        public event EventHandler<string> Disposed;


        public Action<T> OnReceived { get; set; }

        public Func<T, Task> OnReceivedAsync { get; set; }

        public Action OnDisposed { get; set; }

        public MqttMessageReceivedHandler OnProcessMessage { get; set; }

        public Action<TrakHoundLogItem> OnLog { get; set; }


        public MqttStream(
            ITrakHoundServiceConfiguration driverConfiguration,
            MqttConnectionConfiguration connectionConfiguration,
            string clientId = null,
            Action<TrakHoundLogItem> onLogFunction = null
            )
        {
            Id = Guid.NewGuid().ToString();
            _connectionConfiguration = connectionConfiguration ?? throw new ArgumentNullException(nameof(connectionConfiguration));
            _clientId = !string.IsNullOrEmpty(clientId) ? clientId : Guid.NewGuid().ToString();
            OnLog = onLogFunction;

            _mqttClient = MqttClientFactory.CreateClient();
            _mqttClient.ApplicationMessageReceivedAsync += MqttMessageReceived;
            _mqttClient.DisconnectedAsync += MqttDisconnected;
        }

        public void Dispose()
        {
            Task worker;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                worker = _worker;
            }

            Stop();

            // Dispose the client once the Worker has disconnected cleanly
            if (worker != null) worker.ContinueWith(_ => DisposeClient());
            else DisposeClient();

            OnDisposed?.Invoke();
            Disposed?.Invoke(this, Id);
        }

        private void DisposeClient()
        {
            _mqttClient.ApplicationMessageReceivedAsync -= MqttMessageReceived;
            _mqttClient.DisconnectedAsync -= MqttDisconnected;
            try { _mqttClient.Dispose(); } catch { }
        }


        public void Start()
        {
            lock (_lock)
            {
                // Only one Worker per Stream
                if (_disposed || (_worker != null && !_worker.IsCompleted)) return;

                _stop = new CancellationTokenSource();
                var token = _stop.Token;
                _worker = Task.Run(() => Worker(token));
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _stop?.Cancel();
            }
        }

        public Task<bool> Push(T item) => Task.FromResult(false);


        public void AddSubscription(string topic, int qos)
        {
            if (!string.IsNullOrEmpty(topic))
            {
                _subscriptions[topic] = new MqttSubscription(topic, qos);
            }
        }

        public void AddSubscriptions(IEnumerable<string> topics, int qos)
        {
            if (!topics.IsNullOrEmpty())
            {
                foreach (var topic in topics) AddSubscription(topic, qos);
            }
        }


        private async Task Worker(CancellationToken cancellationToken)
        {
            var reconnectInterval = MqttClientFactory.GetReconnectInterval(_connectionConfiguration);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _disconnected = disconnected;

                    await Connect(cancellationToken);

                    if (await Subscribe(cancellationToken))
                    {
                        // Wait until Disconnected or Stopped (no polling)
                        await Task.WhenAny(disconnected.Task, Task.Delay(Timeout.Infinite, cancellationToken));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    Log(TrakHoundLogLevel.Debug, $"Connection Error : {ex.Message}");
                }

                await Disconnect();

                if (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(reconnectInterval, cancellationToken);
                    }
                    catch (OperationCanceledException) { }
                }
            }
        }

        private async Task Connect(CancellationToken cancellationToken)
        {
            if (!_mqttClient.IsConnected)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(MqttClientFactory.GetTimeout(_connectionConfiguration));

                Log(TrakHoundLogLevel.Debug, "Connecting to Broker..");
                await _mqttClient.ConnectAsync(MqttClientFactory.CreateOptions(_connectionConfiguration, _clientId), timeout.Token);
                Log(TrakHoundLogLevel.Debug, "Connected to Broker Successfully");
            }
        }

        private async Task Disconnect()
        {
            try
            {
                if (_mqttClient.IsConnected)
                {
                    Log(TrakHoundLogLevel.Debug, "Disconnecting from Broker..");
                    await _mqttClient.DisconnectAsync(MqttClientFactory.CreateDisconnectOptions());
                    Log(TrakHoundLogLevel.Debug, "Disconnected from Broker Successfully");
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Debug, $"Disconnect Error : {ex.Message}");
            }
        }

        /// <summary>
        /// Subscribes to all Topics. Returns false (so the Worker reconnects and retries) if the Broker rejects any Topic.
        /// </summary>
        private async Task<bool> Subscribe(CancellationToken cancellationToken)
        {
            var subscriptions = _subscriptions.Values.ToList();
            if (subscriptions.IsNullOrEmpty()) return true;

            foreach (var subscription in subscriptions)
            {
                Log(TrakHoundLogLevel.Debug, $"Subscribing to Topic : {subscription.Topic}");
            }

            var result = await _mqttClient.SubscribeAsync(MqttClientFactory.CreateSubscribeOptions(subscriptions), cancellationToken);

            // Result Codes 0-2 are the Granted QoS levels. Anything else is a failure.
            var rejected = result?.Items?.Where(o => (int)o.ResultCode > 2).ToList();
            if (result == null || !rejected.IsNullOrEmpty())
            {
                var reasons = rejected?.Select(o => $"{o.TopicFilter?.Topic} ({o.ResultCode})");
                Log(TrakHoundLogLevel.Warning, $"Subscribe Error : {(reasons != null ? string.Join(", ", reasons) : "No Response")}");
                return false;
            }

            Log(TrakHoundLogLevel.Debug, "Subscribed Successfully");
            return true;
        }

        private Task MqttDisconnected(MqttClientDisconnectedEventArgs args)
        {
            _disconnected?.TrySetResult();
            return Task.CompletedTask;
        }

        private async Task MqttMessageReceived(MqttApplicationMessageReceivedEventArgs args)
        {
            try
            {
                if (OnProcessMessage != null)
                {
                    var result = OnProcessMessage(args.ApplicationMessage);
                    if (result != null)
                    {
                        Log(TrakHoundLogLevel.Trace, $"Message Received : {args.ApplicationMessage?.Topic}");

                        OnReceived?.Invoke(result);
                        if (OnReceivedAsync != null) await OnReceivedAsync(result);
                        Received?.Invoke(this, result);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Error, $"Message Handler Error : Topic = {args.ApplicationMessage?.Topic} : {ex.Message}");
            }
        }

        private void Log(TrakHoundLogLevel level, string message)
        {
            if (OnLog != null)
            {
                var item = new TrakHoundLogItem();
                item.Sender = _clientId;
                item.LogLevel = level;
                item.Message = $"MQTT : Stream : {_connectionConfiguration.Hostname}:{_connectionConfiguration.Port} : {message}";
                OnLog(item);
            }
        }
    }
}
