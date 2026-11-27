// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services.Messages;
using TrakHound.Services.TimeSeries;
using TrakHound.Workers;

namespace TrakHound.Example.MqttToTimeSeries
{
    /// <summary>
    /// Subscribes to Messages (ex. from an MQTT Broker) and writes each value to a Time Series Service, with Tags and a Field taken from
    /// the Message's Topic (see <see cref="TopicMapping"/>). Both Services are reached through the Worker's Router
    /// </summary>
    public class Worker : TrakHoundWorker
    {
        private const string DefaultTopicPattern = "plant/{line}/{machine}/{field}";
        private const string DefaultMeasurement = "machine";
        private const string DefaultValuePath = "value";
        private const int DefaultFlushInterval = 1000;
        private const int DefaultMaxBuffer = 10000;
        private const int MaxBatchSize = 5000;
        private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

        private readonly object _lock = new object();
        private readonly List<TrakHoundTimeSeriesPoint> _buffer = new List<TrakHoundTimeSeriesPoint>();
        private readonly HashSet<string> _warnedTopics = new HashSet<string>();

        private TopicMapping _mapping;
        private string _measurement;
        private string _valuePath;
        private int _qos;
        private string _clientId;
        private int _maxBuffer;
        private long _written;
        private long _dropped;
        private bool _writeFailing;

        private CancellationTokenSource _stop;
        private Task _subscription;
        private Task _flush;


        // The Instance creates the Worker with this constructor, then sets Client, Volume and Variables
        public Worker(ITrakHoundWorkerConfiguration configuration) : base(configuration) { }


        protected override Task OnStartAsync()
        {
            // The Parameters set in the Worker's Configuration
            var topicPattern = Configuration.GetParameter("topicPattern");
            if (string.IsNullOrWhiteSpace(topicPattern)) topicPattern = DefaultTopicPattern;

            try
            {
                _mapping = TopicMapping.Parse(topicPattern);
            }
            catch (ArgumentException ex)
            {
                // Nothing to do without a valid Pattern : say why, and stay stopped until the Configuration is fixed
                Log(TrakHoundLogLevel.Error, ex.Message);
                return Task.CompletedTask;
            }

            _measurement = Configuration.GetParameter("measurement");
            if (string.IsNullOrWhiteSpace(_measurement)) _measurement = DefaultMeasurement;

            _valuePath = Configuration.GetParameter("valuePath");
            if (string.IsNullOrWhiteSpace(_valuePath)) _valuePath = DefaultValuePath;

            _qos = Math.Clamp(Configuration.GetParameter<int>("qos"), 0, 2);

            // A Client ID that stays the same across restarts, so a Broker with a Persistent Session (cleanSession: false) keeps its Messages
            _clientId = Configuration.GetParameter("clientId");
            if (string.IsNullOrWhiteSpace(_clientId)) _clientId = null;

            var flushInterval = Configuration.GetParameter<int>("flushInterval");
            if (flushInterval <= 0) flushInterval = DefaultFlushInterval;

            _maxBuffer = Configuration.GetParameter<int>("maxBuffer");
            if (_maxBuffer <= 0) _maxBuffer = DefaultMaxBuffer;

            _stop = new CancellationTokenSource();
            _subscription = RunSubscription(_stop.Token);
            _flush = RunFlush(TimeSpan.FromMilliseconds(flushInterval), _stop.Token);

            Log(TrakHoundLogLevel.Information, $"MQTT to Time Series STARTED : '{_mapping.SubscribeTopic}' to Measurement '{_measurement}' (Tags : {string.Join(", ", _mapping.TagNames)})");
            return Task.CompletedTask;
        }

        protected override async Task OnStopAsync()
        {
            if (_stop == null) return;

            _stop.Cancel();
            await Task.WhenAll(_subscription, _flush);
            _stop.Dispose();
            _stop = null;

            // Write what was received before stopping
            await Flush(CancellationToken.None);

            Log(TrakHoundLogLevel.Information, $"MQTT to Time Series STOPPED : {_written} Points written, {_dropped} dropped");
        }


        /// <summary>
        /// Subscribes, and subscribes again whenever the Subscription can't be opened or is closed (ex. the Message Service was reconfigured)
        /// </summary>
        private async Task RunSubscription(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var response = await Client.Services.Messages.Subscribe(_mapping.SubscribeTopic, _clientId, _qos, cancellationToken: cancellationToken);
                    if (!response.Success || response.Result == null)
                    {
                        Log(TrakHoundLogLevel.Warning, $"Subscribe to '{_mapping.SubscribeTopic}' failed ({response.StatusCode}{(!string.IsNullOrEmpty(response.Message) ? $" : {response.Message}" : "")}). Check the Worker's Router has a Route for Messages. Retrying in {RetryInterval.TotalSeconds}s");
                        await Task.Delay(RetryInterval, cancellationToken);
                        continue;
                    }

                    using var stream = response.Result;
                    var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    stream.Disposed += (_, _) => closed.TrySetResult();
                    stream.OnReceived = Received;
                    stream.Start();

                    Log(TrakHoundLogLevel.Information, $"Subscribed to '{_mapping.SubscribeTopic}'");

                    await Task.WhenAny(closed.Task, Task.Delay(Timeout.Infinite, cancellationToken));
                    if (cancellationToken.IsCancellationRequested) break;

                    Log(TrakHoundLogLevel.Warning, $"The Subscription to '{_mapping.SubscribeTopic}' closed. Subscribing again in {RetryInterval.TotalSeconds}s");
                    await Task.Delay(RetryInterval, cancellationToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Log(TrakHoundLogLevel.Error, $"Subscribe to '{_mapping.SubscribeTopic}' failed : {ex.Message}. Retrying in {RetryInterval.TotalSeconds}s");
                    try { await Task.Delay(RetryInterval, cancellationToken); } catch (OperationCanceledException) { break; }
                }
            }
        }

        /// <summary>
        /// Turns a Message into a Point and adds it to the buffer. A Message that can't be read is skipped (and logged once for its Topic)
        /// </summary>
        private void Received(TrakHoundMessageResponse message)
        {
            if (!_mapping.TryMap(message.Topic, out var tags, out var field))
            {
                WarnOnce(message.Topic, $"Skipped Messages on '{message.Topic}' : the Topic doesn't match the Topic Pattern '{_mapping.Pattern}'");
                return;
            }

            if (!PayloadReader.TryRead(message.GetContentUtf8String(), _valuePath, out var value, out var error))
            {
                WarnOnce(message.Topic, $"Skipped a Message on '{message.Topic}' : {error}");
                return;
            }

            var point = new TrakHoundTimeSeriesPoint
            {
                Measurement = _measurement,
                Tags = tags,
                // When the Message Service received it (MQTT Messages don't carry a time)
                Timestamp = message.Timestamp > 0 ? new DateTimeOffset(DateTime.SpecifyKind(message.Timestamp.ToDateTime(), DateTimeKind.Utc)) : DateTimeOffset.UtcNow,
                Fields = new Dictionary<string, object> { [field] = value },
                Quality = TrakHoundTimeSeriesQuality.Good
            };

            lock (_lock)
            {
                _buffer.Add(point);

                // Keep the newest Points if the Time Series Service can't keep up (or can't be reached)
                if (_buffer.Count > _maxBuffer)
                {
                    var over = _buffer.Count - _maxBuffer;
                    _buffer.RemoveRange(0, over);
                    _dropped += over;
                }
            }
        }

        private async Task RunFlush(TimeSpan interval, CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(interval);

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    await Flush(cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// Writes the buffered Points in batches. A batch that fails stays in the buffer and is written again on the next flush
        /// (Time Series Writes are safe to retry)
        /// </summary>
        private async Task Flush(CancellationToken cancellationToken)
        {
            while (true)
            {
                List<TrakHoundTimeSeriesPoint> batch;
                lock (_lock)
                {
                    if (_buffer.Count == 0) return;
                    batch = _buffer.Take(MaxBatchSize).ToList();
                }

                try
                {
                    var response = await Client.Services.TimeSeries.Write(batch, cancellationToken: cancellationToken);
                    if (!response.Success)
                    {
                        // 404 or 405 : no Time Series Service to send it to
                        var hint = response.StatusCode == 404 || response.StatusCode == 405 ? " Check the Worker's Router has a Route for TimeSeries." : "";
                        WriteFailed($"Writing {batch.Count} Points failed ({response.StatusCode}{(!string.IsNullOrEmpty(response.Message) ? $" : {response.Message}" : "")}).{hint} Retrying on the next flush, and keeping up to {_maxBuffer} Points until then");
                        return;
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    WriteFailed($"Writing {batch.Count} Points failed : {ex.Message}. Retrying on the next flush, and keeping up to {_maxBuffer} Points until then");
                    return;
                }

                if (_writeFailing)
                {
                    _writeFailing = false;
                    Log(TrakHoundLogLevel.Information, "Writing Points again");
                }

                lock (_lock)
                {
                    // By reference, since Points that were dropped while the batch was being written are no longer in the buffer
                    var sent = new HashSet<TrakHoundTimeSeriesPoint>(batch, ReferenceEqualityComparer.Instance);
                    _buffer.RemoveAll(sent.Contains);
                    _written += batch.Count;
                }

                Log(TrakHoundLogLevel.Debug, $"Wrote {batch.Count} Points");
            }
        }

        /// <summary>
        /// Logs the first failed Write as a Warning, and the ones after it (until a Write succeeds) as Debug, so an outage doesn't fill the Log
        /// </summary>
        private void WriteFailed(string message)
        {
            Log(_writeFailing ? TrakHoundLogLevel.Debug : TrakHoundLogLevel.Warning, message);
            _writeFailing = true;
        }

        private void WarnOnce(string topic, string message)
        {
            bool first;
            lock (_warnedTopics) first = _warnedTopics.Add(topic ?? string.Empty);

            Log(first ? TrakHoundLogLevel.Warning : TrakHoundLogLevel.Debug, message);
        }
    }
}
