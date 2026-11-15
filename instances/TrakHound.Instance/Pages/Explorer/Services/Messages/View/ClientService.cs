using System.Text;
using System.Text.Json;
using TrakHound.Services;
using TrakHound.Services.Messages;

namespace TrakHound.Instance.Pages.Explorer.Services.Messages.View
{
    public class ClientService : IDisposable
    {
        private const int _updateInterval = 100;
        private const int _maxUpdateCount = 0; // No limit (each tick drains the queue, which holds each Path at most once)

        /// <summary>
        /// The Topic subscribed to when none is specified
        /// </summary>
        public const string DefaultTopic = "#";

        /// <summary>
        /// The maximum number of Topics kept in memory. New Topics are ignored once reached (existing Topics keep updating).
        /// </summary>
        public const int MaxTopics = 10000;


        private readonly ExplorerService _explorerService;
        private readonly Dictionary<string, Message> _messages = new Dictionary<string, Message>(); // Path => UnsEventMessage
        private readonly ListDictionary<string, string> _pathParents = new ListDictionary<string, string>(); // ParentPath => Path
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(); // Path => UnsEventMessage.Content (as UTF8 string)
        // Created while subscribed (each runs a background loop), disposed in Stop()
        private ItemIntervalQueue<string> _updatedPaths;
        private ItemIntervalQueue<string> _updatedMessageCounts;
        // Paths waiting in each queue (so a busy Topic is queued once per interval instead of once per Message)
        private readonly HashSet<string> _pendingPaths = new HashSet<string>();
        private readonly HashSet<string> _pendingMessageCounts = new HashSet<string>();
        private readonly Dictionary<string, long> _messageCounts = new Dictionary<string, long>();
        private readonly HashSet<string> _expandedPaths = new HashSet<string>();
        private readonly ThrottleEvent _updateDelay = new ThrottleEvent(_updateInterval);
        private object _lock = new object();

        private string _subscribeTopic;
        private ITrakHoundStream<TrakHoundMessageResponse> _stream;
        private string _instanceId;
        private string _previousInstanceId;
        private string _serviceConfigurationId;
        private string _previousServiceConfigurationId;
        private string _selectedPath;
        private string _subscribeError;
        private bool _isSubscribing;
        private bool _topicLimitReached;
        private bool? _isAvailable;
        private string _availabilityMessage;
        private int _checkingAvailability;
        private int _availabilityTicks;
        private long _messagesReceived;
        private string _lastStatusSnapshot;

        private readonly System.Timers.Timer _metricsTimer;


        public string SubscribePath => _subscribeTopic;

        public string SelectedPath => _selectedPath;

        /// <summary>
        /// The error from the last Subscribe attempt (null if it succeeded)
        /// </summary>
        public string SubscribeError => _subscribeError;

        /// <summary>
        /// Whether a Subscribe request is in progress
        /// </summary>
        public bool IsSubscribing => _isSubscribing;

        /// <summary>
        /// Whether <see cref="MaxTopics"/> has been reached for the current Subscription
        /// </summary>
        public bool TopicLimitReached => _topicLimitReached;

        public string InstanceId => _instanceId;

        public string ServiceConfigurationId => _serviceConfigurationId;

        /// <summary>
        /// The current connection state, combining the Subscription state with the Message Service's availability
        /// </summary>
        public ConnectionStatus ConnectionStatus
        {
            get
            {
                if (_isSubscribing) return ConnectionStatus.Connecting;
                if (_subscribeError != null) return ConnectionStatus.Error;
                if (_stream == null) return ConnectionStatus.NotSubscribed;
                if (_isAvailable == false) return ConnectionStatus.Disconnected;
                return _isAvailable == true ? ConnectionStatus.Connected : ConnectionStatus.Connecting;
            }
        }

        /// <summary>
        /// Details for the current <see cref="ConnectionStatus"/> (ex. the error or the availability message)
        /// </summary>
        public string ConnectionMessage => _subscribeError ?? _availabilityMessage;

        /// <summary>
        /// The number of Topics received for the current Subscription
        /// </summary>
        public int TopicCount
        {
            get { lock (_lock) return _messages.Count; }
        }

        /// <summary>
        /// The number of Messages received for the current Subscription
        /// </summary>
        public long MessageCount => Interlocked.Read(ref _messagesReceived);

        /// <summary>
        /// Raised (at most once per second) when the <see cref="ConnectionStatus"/> or the counts change
        /// </summary>
        public event EventHandler StatusUpdated;


        public event EventHandler<string> PathSelected;

        public event ValueUpdatedHandler ValueUpdated;

        public event MessageCountUpdatedHandler MessageCountUpdated;

        public event ExpandedHandler ExpandedChanged;

        public event EventHandler Updated;


        public ClientService(ExplorerService explorerService)
        {
            _explorerService = explorerService;

            _updateDelay.Elapsed += UpdateDelayElapsed;

            // Started on Subscribe and stopped in Stop()
            _metricsTimer = new System.Timers.Timer();
            _metricsTimer.Interval = 1000;
            _metricsTimer.Elapsed += MetricsTimerElapsed;
        }

        public void Dispose()
        {
            if (_stream != null) _stream.Dispose();
            if (_metricsTimer != null) _metricsTimer.Dispose();
            if (_updateDelay != null) _updateDelay.Dispose();
            StopWorkers();
        }

        private void StartWorkers()
        {
            lock (_lock)
            {
                if (_updatedPaths == null)
                {
                    _updatedPaths = new ItemIntervalQueue<string>(_updateInterval, _maxUpdateCount);
                    _updatedPaths.ItemsReceived += UpdateValues;
                }

                if (_updatedMessageCounts == null)
                {
                    _updatedMessageCounts = new ItemIntervalQueue<string>(_updateInterval, _maxUpdateCount);
                    _updatedMessageCounts.ItemsReceived += UpdateMessageCounts;
                }
            }

            _metricsTimer.Start();
        }

        private void StopWorkers()
        {
            _metricsTimer?.Stop();

            ItemIntervalQueue<string> updatedPaths;
            ItemIntervalQueue<string> updatedMessageCounts;
            lock (_lock)
            {
                updatedPaths = _updatedPaths;
                updatedMessageCounts = _updatedMessageCounts;
                _updatedPaths = null;
                _updatedMessageCounts = null;
                _pendingPaths.Clear();
                _pendingMessageCounts.Clear();
            }

            // An ItemIntervalQueue can't be restarted once disposed, so StartWorkers() creates new ones
            if (updatedPaths != null)
            {
                updatedPaths.ItemsReceived -= UpdateValues;
                updatedPaths.Dispose();
            }
            if (updatedMessageCounts != null)
            {
                updatedMessageCounts.ItemsReceived -= UpdateMessageCounts;
                updatedMessageCounts.Dispose();
            }
        }


        /// <summary>
        /// Sets the Instance and Message Service and subscribes to the current Topic
        /// </summary>
        public async Task<TrakHoundServiceResponse> Load(string instanceId, string configurationId)
        {
            _instanceId = instanceId;
            _serviceConfigurationId = configurationId;

            return await Subscribe(_subscribeTopic);

            //var client = _explorerService.GetClient(instanceId, configurationId);
            //if (client != null)
            //{
            //    var response = await client.Services.Messages.Subscribe(_subscribePath, Guid.NewGuid().ToString());
            //    if (response.Success)
            //    {
            //        _consumer = response.Result;
            //        _consumer.Received += EventMessageReceived;
            //    }
            //    else
            //    {
            //        // Connection ERROR
            //    }
            //}


            //var connectionConfiguration = _connectionService.GetConnection(connectionId);
            //if (connectionConfiguration != null && connectionConfiguration.Id != null && connectionConfiguration.Type != null)
            //{
            //    _connectionConfiguration = connectionConfiguration;

            //    switch (connectionConfiguration.Type.ToLower())
            //    {
            //        case "mqtt":

            //            var mqttServer = connectionConfiguration.GetParameter("server");
            //            var mqttPort = connectionConfiguration.GetParameter<int>("port");
            //            var mqttClientId = connectionConfiguration.GetParameter("clientId");

            //            var mqttConnection = new UnsMqttConnection(mqttServer, mqttPort, mqttClientId);
            //            mqttConnection.AddSubscription("#"); // Needs to be done differently. Need to subscribe to result of Subscribe(path) method
            //            mqttConnection.AddDestination();
            //            _client.AddConnection(mqttConnection);
            //            break;


            //        case "sparkplug-b":

            //            var sparkplugConnection = new UnsSparkplugConnection("localhost", 1883, "sparkplug-example");
            //            sparkplugConnection.AddApplication();
            //            _client.AddConnection(sparkplugConnection);
            //            break;
            //    }

            //    await _client.Start();
            //}
            //else
            //{
            //    // Raise Unknown Connection event
            //}
        }


        private const int AvailabilityCheckInterval = 10; // Seconds (Metrics Timer ticks)

        private void MetricsTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Check the Message Service's availability right after subscribing and then periodically
            if (_stream != null && (_isAvailable == null || ++_availabilityTicks >= AvailabilityCheckInterval))
            {
                _availabilityTicks = 0;
                _ = CheckAvailability();
            }

            RaiseStatusUpdated();
        }

        private async Task CheckAvailability()
        {
            // Only one check at a time
            if (Interlocked.Exchange(ref _checkingAvailability, 1) == 1) return;

            try
            {
                var client = _explorerService.GetClient(_instanceId);
                if (client != null)
                {
                    var response = await client.Services.Messages.CheckAvailability(_serviceConfigurationId);
                    _isAvailable = response.Success && response.Result != null && response.Result.IsAvailable;
                    _availabilityMessage = response.Result?.Message ?? (!response.Success ? response.Message : null);
                }
            }
            catch (Exception ex)
            {
                _isAvailable = false;
                _availabilityMessage = ex.Message;
            }
            finally
            {
                Interlocked.Exchange(ref _checkingAvailability, 0);
            }

            RaiseStatusUpdated();
        }

        private void RaiseStatusUpdated()
        {
            // Only notify when something shown in the status changed
            var snapshot = $"{ConnectionStatus}|{ConnectionMessage}|{_subscribeTopic}|{TopicCount}|{MessageCount}|{_topicLimitReached}";
            if (snapshot != _lastStatusSnapshot)
            {
                _lastStatusSnapshot = snapshot;
                StatusUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Subscribes to the Topic ("#" if empty), replacing the current Subscription. Does nothing if already subscribed
        /// to the same Topic on the same Instance and Service. Never throws: failures are returned and stored in <see cref="SubscribeError"/>.
        /// </summary>
        public async Task<TrakHoundServiceResponse> Subscribe(string topic)
        {
            var subscribeTopic = !string.IsNullOrEmpty(topic) ? topic : DefaultTopic;

            if (_stream != null && subscribeTopic == _subscribeTopic &&
                _instanceId == _previousInstanceId && _serviceConfigurationId == _previousServiceConfigurationId)
            {
                return TrakHoundServiceResponse.Ok();
            }

            _subscribeTopic = subscribeTopic;
            _previousInstanceId = _instanceId;
            _previousServiceConfigurationId = _serviceConfigurationId;

            ResetSubscription();
            StartWorkers();

            _isSubscribing = true;
            Updated?.Invoke(this, EventArgs.Empty);
            RaiseStatusUpdated();

            TrakHoundServiceResponse result;
            try
            {
                var client = _explorerService.GetClient(_instanceId);
                if (client != null)
                {
                    var response = await client.Services.Messages.Subscribe(_subscribeTopic, routerId: _serviceConfigurationId);
                    if (response.Success && response.Result != null)
                    {
                        _stream = response.Result;
                        _stream.Received += MessageReceived;
                        _stream.Start();
                        result = TrakHoundServiceResponse.Ok();
                    }
                    else
                    {
                        result = new TrakHoundServiceResponse(response.StatusCode, !string.IsNullOrEmpty(response.Message) ? response.Message : "Subscribe Failed");
                    }
                }
                else
                {
                    result = TrakHoundServiceResponse.NotAvailable($"Instance ({_instanceId}) Not Available");
                }
            }
            catch (Exception ex)
            {
                result = TrakHoundServiceResponse.InternalError(ex.Message);
            }

            if (!result.Success)
            {
                // Allow the same Topic to be retried
                _previousInstanceId = null;
                _previousServiceConfigurationId = null;
            }

            _subscribeError = !result.Success ? $"Subscribe to \"{_subscribeTopic}\" Failed ({result.StatusCode}) : {result.Message}" : null;
            _isSubscribing = false;
            Updated?.Invoke(this, EventArgs.Empty);
            RaiseStatusUpdated();

            return result;
        }

        /// <summary>
        /// Stops the current Subscription and clears all Messages. Call when leaving the page, since this
        /// service lives for the whole circuit (browser tab).
        /// </summary>
        public void Stop()
        {
            ResetSubscription();
            StopWorkers();

            _previousInstanceId = null;
            _previousServiceConfigurationId = null;
            _subscribeError = null;
            _isSubscribing = false;
        }

        private void ResetSubscription()
        {
            // Detach the previous Stream first so it can't write to the collections while they are cleared
            var stream = _stream;
            _stream = null;
            if (stream != null)
            {
                stream.Received -= MessageReceived;
                stream.Dispose();
            }

            lock (_lock)
            {
                _messages.Clear();
                _pathParents.Clear();
                _values.Clear();
                _messageCounts.Clear();
                _expandedPaths.Clear();
                _topicLimitReached = false;
            }
            lock (_lock)
            {
                _updatedPaths?.Clear();
                _updatedMessageCounts?.Clear();
                _pendingPaths.Clear();
                _pendingMessageCounts.Clear();
            }

            Interlocked.Exchange(ref _messagesReceived, 0);
            _isAvailable = null;
            _availabilityMessage = null;
            _availabilityTicks = 0;

            // Clear the Selection (the selected Topic belongs to the previous Subscription)
            if (_selectedPath != null)
            {
                _selectedPath = null;
                PathSelected?.Invoke(this, null);
            }
        }

        /// <summary>
        /// Publishes the Content (an empty Content is valid, ex. to clear a Retained Message). Returns the Service Response.
        /// </summary>
        public async Task<TrakHound.Services.TrakHoundServiceResponse> Publish(string topic, string content, bool retain = false, int qos = 0)
        {
            if (string.IsNullOrEmpty(topic)) return TrakHound.Services.TrakHoundServiceResponse.BadRequest("No Topic Specified");

            var client = _explorerService.GetClient(_instanceId, _serviceConfigurationId);
            if (client == null) return TrakHound.Services.TrakHoundServiceResponse.NotAvailable("Client Not Available");

            using (var contentStream = new MemoryStream(Encoding.UTF8.GetBytes(content ?? string.Empty)))
            {
                return await client.Services.Messages.Publish(topic, contentStream, retain, qos, routerId: _serviceConfigurationId);
            }
        }


        public IEnumerable<string> GetFilteredPaths(string path)
        {
            if (!string.IsNullOrEmpty(path) && path != "#")
            {
                var parentPath = TrakHoundPath.GetParentPath(path);
                if (parentPath != null)
                {
                    var childPaths = GetChildPaths(parentPath);
                    if (childPaths != null)
                    {
                        var filteredPaths = new List<string>();

                        foreach (var childPath in childPaths)
                        {
                            if (childPath.ToLower().StartsWith(path.ToLower()))
                            {
                                filteredPaths.Add(childPath);
                            }
                        }

                        return filteredPaths;
                    }
                }
                else
                {
                    IEnumerable<string> childPaths;
                    lock (_lock) childPaths = _pathParents.Get("/")?.ToList();
                    if (childPaths != null)
                    {
                        var filteredPaths = new List<string>();

                        foreach (var childPath in childPaths)
                        {
                            if (childPath.ToLower().StartsWith(path.ToLower()))
                            {
                                filteredPaths.Add(childPath);
                            }
                        }

                        return filteredPaths;
                    }
                }
            }
            else
            {
                lock (_lock)
                {
                    return _pathParents.Get("/")?.ToList();
                }
            }

            return null;
        }

        public IEnumerable<string> GetChildPaths(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                lock (_lock)
                {
                    return _pathParents.Get(path.ToLower())?.ToList();
                }
            }

            return null;
        }

        public string GetValue(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                lock (_lock)
                {
                    return _values.GetValueOrDefault(path);
                }
            }

            return null;
        }

        public Message GetMessage(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                lock (_lock)
                {
                    return _messages.GetValueOrDefault(path);
                }
            }

            return null;
        }

        public long GetMessageCount(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                lock (_lock)
                {
                    return _messageCounts.GetValueOrDefault(path);
                }
            }

            return 0;
        }


        public void Select(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                _selectedPath = path;

                if (PathSelected != null) PathSelected.Invoke(this, path);
            }
        }

        public bool GetExpanded(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                lock (_lock)
                {
                    return _expandedPaths.Contains(path);
                }
            }

            return false;
        }

        public void SetExpanded(string path, bool expanded)
        {
            if (!string.IsNullOrEmpty(path))
            {
                if (expanded)
                {
                    lock (_lock)
                    {
                        if (!_expandedPaths.Contains(path)) _expandedPaths.Add(path);
                    }
                }
                else
                {
                    lock (_lock)
                    {
                        _expandedPaths.Remove(path);
                    }
                }

                if (ExpandedChanged != null) ExpandedChanged.Invoke(path, expanded);
            }
        }


        private void MessageReceived(object sender, TrakHoundMessageResponse messageResponse)
        {
            var message = new Message();
            message.Topic = messageResponse.Topic;
            message.Timestamp = messageResponse.Timestamp.ToDateTime();
            message.Qos = messageResponse.Qos;
            message.Retain = messageResponse.Retain;
            message.Content = messageResponse.Content;

            if (message != null && message.Topic != null)
            {
                var alreadyExists = false;
                var limitReached = false;

                // Reads the whole Content without moving the shared stream's Position (other consumers may read it too)
                var contentBytes = TrakHoundMessageResponse.GetContentBytes(message.Content);
                var value = contentBytes != null ? Encoding.UTF8.GetString(contentBytes) : null;

                lock (_lock)
                {
                    alreadyExists = _messages.ContainsKey(message.Topic);

                    // Cap the number of Topics kept in memory (existing Topics keep updating)
                    if (!alreadyExists && _messages.Count >= MaxTopics)
                    {
                        limitReached = !_topicLimitReached;
                        _topicLimitReached = true;
                    }
                }

                Interlocked.Increment(ref _messagesReceived);

                if (_topicLimitReached && !alreadyExists)
                {
                    if (limitReached) Updated?.Invoke(this, EventArgs.Empty);
                    return;
                }

                lock (_lock)
                {

                    _messages.Remove(message.Topic);
                    _messages.Add(message.Topic, message);

                    _values.Remove(message.Topic);
                    _values.Add(message.Topic, value);


                    foreach (var path in TrakHoundPath.GetPaths(message.Topic))
                    {
                        var parentPath = TrakHoundPath.GetParentPath(path);
                        if (parentPath != null)
                        {
                            _pathParents.Add(parentPath.ToLower(), path);
                        }
                        else
                        {
                            _pathParents.Add("/", path);
                        }

                        // Update Message Count for Path
                        if (_messageCounts.ContainsKey(path))
                        {
                            _messageCounts[path] += 1;
                        }
                        else
                        {
                            _messageCounts.Add(path, 1);
                        }
                        if (_updatedMessageCounts != null && _pendingMessageCounts.Add(path)) _updatedMessageCounts.Add(path);
                    }

                    if (_updatedPaths != null && _pendingPaths.Add(message.Topic)) _updatedPaths.Add(message.Topic);
                }

                _updateDelay.Set();
                //if (!alreadyExists) _updateDelay.Set();
            }
        }

        private void UpdateDelayElapsed(object sender, EventArgs e)
        {
            if (Updated != null) Updated.Invoke(this, new EventArgs());
        }

        private void UpdateValues(object sender, IEnumerable<string> paths)
        {
            if (paths != null)
            {
                // Released before reading, so a Message received after this point queues the Path again
                lock (_lock) foreach (var path in paths) _pendingPaths.Remove(path);

                foreach (var path in paths)
                {
                    var value = GetValue(path);
                    if (ValueUpdated != null) ValueUpdated.Invoke(path, value);
                }
            }
        }

        private void UpdateMessageCounts(object sender, IEnumerable<string> paths)
        {
            if (paths != null)
            {
                // Released before reading, so a Message received after this point queues the Path again
                lock (_lock) foreach (var path in paths) _pendingMessageCounts.Remove(path);

                foreach (var path in paths)
                {
                    var count = GetMessageCount(path);
                    if (MessageCountUpdated != null) MessageCountUpdated.Invoke(path, count);
                }
            }
        }


        /// <summary>
        /// Gets the Message Content as a string. A null Content Type formats JSON Content (and falls back to Text)
        /// </summary>
        public string GetString(Message message, MessageContentType? contentType = null)
        {
            var contentBytes = GetContentBytes(message);
            if (contentBytes == null) return null;

            switch (contentType)
            {
                case MessageContentType.Json: return ConvertJson(contentBytes) ?? ConvertPlainText(contentBytes);
                case MessageContentType.Bytes: return ConvertBytes(contentBytes);
                case MessageContentType.Text: return ConvertPlainText(contentBytes);
                default: return (IsJson(contentBytes) ? ConvertJson(contentBytes) : null) ?? ConvertPlainText(contentBytes);
            }
        }

        /// <summary>
        /// Reads the whole Content without moving the shared stream's Position (other consumers may read it too)
        /// </summary>
        public static byte[] GetContentBytes(Message message)
        {
            if (message?.Content == null) return null;
            return TrakHoundMessageResponse.GetContentBytes(message.Content);
        }

        private static bool IsJson(byte[] content)
        {
            // Check the first non-whitespace character before attempting to parse
            foreach (var b in content)
            {
                if (b == ' ' || b == '\t' || b == '\r' || b == '\n') continue;
                return b == '{' || b == '[';
            }

            return false;
        }

        private static string ConvertBytes(byte[] content)
        {
            // Hex dump : offset, 16 bytes per line, and the printable ASCII characters
            const int bytesPerLine = 16;
            const int maxBytes = 64 * 1024;
            var length = Math.Min(content.Length, maxBytes);
            var builder = new StringBuilder();

            for (var offset = 0; offset < length; offset += bytesPerLine)
            {
                var count = Math.Min(bytesPerLine, length - offset);

                builder.Append(offset.ToString("X8")).Append("  ");
                for (var i = 0; i < bytesPerLine; i++)
                {
                    builder.Append(i < count ? content[offset + i].ToString("X2") + " " : "   ");
                    if (i == 7) builder.Append(' ');
                }

                builder.Append(' ');
                for (var i = 0; i < count; i++)
                {
                    var b = content[offset + i];
                    builder.Append(b >= 32 && b < 127 ? (char)b : '.');
                }

                builder.AppendLine();
            }

            if (content.Length > length) builder.AppendLine($"... {content.Length - length:N0} more bytes");

            return builder.ToString();
        }

        private static string ConvertPlainText(byte[] content)
        {
            try
            {
                return Encoding.UTF8.GetString(content);
            }
            catch { }

            return null;
        }

        private static string ConvertJson(byte[] content)
        {
            try
            {
                var options = new JsonSerializerOptions()
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Deserialize<JsonElement>(content);

                return JsonSerializer.Serialize(json, options);
            }
            catch { }

            return null;
        }
    }
}
