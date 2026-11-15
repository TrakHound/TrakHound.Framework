using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using TrakHound.Services;
using TrakHound.Services.MessageQueues;

namespace TrakHound.RabbitMq.Service
{
    public class RabbitMqMessageQueueService :
        RabbitMqService,
        ITrakHoundMessageQueueService
    {
        private const string _defaultClientName = "TrakHound.RabbitMQ.Service";
        private const string _defaultHostname = "localhost";
        private const int _defaultPort = 5672;
        private const int _defaultSslPort = 5671;
        private const int _defaultConnectionTimeout = 5000;
        private const int _defaultManagementPort = 15672;
        private const int _defaultSslManagementPort = 15671;
        private const int _defaultPrefetch = 100;
        private const int _preconditionFailed = 406;
        private static readonly TimeSpan _managementRetryInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan _disposeTimeout = TimeSpan.FromSeconds(5);

        private readonly ConnectionFactory _connectionFactory;
        private readonly Dictionary<int, RabbitMqChannel> _channels = new Dictionary<int, RabbitMqChannel>();
        private readonly ConcurrentDictionary<string, MessageQueueDirectConsumer> _consumers = new ConcurrentDictionary<string, MessageQueueDirectConsumer>();
        private readonly ConcurrentDictionary<string, byte> _declaredQueues = new ConcurrentDictionary<string, byte>();
        private readonly ConcurrentDictionary<string, byte> _knownQueues = new ConcurrentDictionary<string, byte>();
        private readonly RabbitMqManagementClient _managementClient;
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private readonly object _channelsLock = new object();
        private readonly SemaphoreSlim _pullLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _publishLock = new SemaphoreSlim(1, 1);
        private readonly int _prefetch = _defaultPrefetch;
        private readonly bool _publishConfirm = true;
        private readonly TimeSpan _publishConfirmTimeout = TimeSpan.FromMilliseconds(_defaultConnectionTimeout);

        private IConnection _connection;
        private string _connectionId;
        private RabbitMqChannel _pullChannel;
        private RabbitMqChannel _publishChannel;
        private volatile bool _disposed;
        private bool? _managementAvailable;
        private DateTime _managementRetryTime;


        public string ConnectionId => _connectionId;


        public RabbitMqMessageQueueService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            if (configuration != null)
            {
                // Read Driver Configuration Parameters
                var hostname = configuration.GetParameter("hostname");
                var port = configuration.GetParameter<int>("port");
                var virtualHost = configuration.GetParameter("virtualHost");
                var username = configuration.GetParameter("username");
                var password = configuration.GetParameter("password");
                var ssl = configuration.GetParameter<bool>("ssl");
                var connectionTimeout = configuration.GetParameter<int>("connectionTimeout");
                var prefetch = configuration.GetParameter<int>("prefetch");
                var publishConfirm = configuration.GetParameter("publishConfirm");
                var managementUrl = configuration.GetParameter("managementUrl");
                var managementPort = configuration.GetParameter<int>("managementPort");

                // Set Defaults (if not found in configuration)
                if (string.IsNullOrEmpty(hostname)) hostname = _defaultHostname;
                if (port < 1) port = ssl ? _defaultSslPort : _defaultPort;
                if (connectionTimeout < 1) connectionTimeout = _defaultConnectionTimeout;
                if (managementPort < 1) managementPort = ssl ? _defaultSslManagementPort : _defaultManagementPort;
                if (string.IsNullOrEmpty(managementUrl)) managementUrl = $"{(ssl ? "https" : "http")}://{hostname}:{managementPort}";
                if (prefetch > 0) _prefetch = Math.Min(prefetch, ushort.MaxValue);
                _publishConfirm = !string.Equals(publishConfirm, "false", StringComparison.OrdinalIgnoreCase);
                _publishConfirmTimeout = TimeSpan.FromMilliseconds(connectionTimeout);

                // Create RabbitMQ Connection Factory
                var connectionFactory = new ConnectionFactory();
                connectionFactory.ClientProvidedName = $"{_defaultClientName}.{configuration.Id}";
                connectionFactory.HostName = hostname;
                connectionFactory.Port = port;
                connectionFactory.RequestedConnectionTimeout = TimeSpan.FromMilliseconds(connectionTimeout);
                if (!string.IsNullOrEmpty(virtualHost)) connectionFactory.VirtualHost = virtualHost;
                if (ssl) connectionFactory.Ssl = new SslOption { Enabled = true, ServerName = hostname };

                // Reconnection is handled here (and by each Consumer) so that a new Connection always gets
                // a new ConnectionId, which invalidates Delivery IDs issued on the previous Connection
                connectionFactory.AutomaticRecoveryEnabled = false;
                connectionFactory.TopologyRecoveryEnabled = false;

                if (!string.IsNullOrEmpty(username)) connectionFactory.UserName = username;
                if (!string.IsNullOrEmpty(password)) connectionFactory.Password = password;
                _connectionFactory = connectionFactory;

                // The management plugin's HTTP API provides the Queue list and Message rates for Metrics
                _managementClient = new RabbitMqManagementClient(managementUrl, username, password, connectionFactory.VirtualHost, TimeSpan.FromMilliseconds(connectionTimeout));
            }
        }

        protected override void OnDisposed()
        {
            _disposed = true;

            // Stop all Consumers
            foreach (var consumer in _consumers.Values)
            {
                try { consumer.Dispose(); } catch { }
            }
            _consumers.Clear();

            // Closing is async in the client, and Dispose isn't (run off the caller's context so it can't deadlock)
            try
            {
                Task.Run(CloseAll).Wait(_disposeTimeout);
            }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Debug, $"RabbitMQ : Dispose : Exception : {ex.Message}");
            }

            _managementClient?.Dispose();

            base.OnDisposed();
        }

        private async Task CloseAll()
        {
            await CloseChannel(Interlocked.Exchange(ref _pullChannel, null));
            await CloseChannel(Interlocked.Exchange(ref _publishChannel, null));

            await _connectionLock.WaitAsync();
            try
            {
                await CloseConnection();
            }
            finally
            {
                _connectionLock.Release();
            }
        }


        public override async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            var response = new TrakHoundServiceAvailabilityResponse();

            response.IsAvailable = (await Connect(cancellationToken)).Connection != null;
            if (!response.IsAvailable) response.Message = $"RabbitMQ Broker Not Available ({_connectionFactory?.HostName}:{_connectionFactory?.Port})";

            return Ok(response);
        }


        public Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>> Subscribe(string queue, bool autoAcknowledge, int prefetch = 0, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(queue)) return Task.FromResult(BadRequest<ITrakHoundStream<TrakHoundQueueMessage>>());
            if (_disposed) return Task.FromResult(NotAvailable<ITrakHoundStream<TrakHoundQueueMessage>>("Service Disposed"));

            var consumerPrefetch = prefetch > 0 ? Math.Min(prefetch, ushort.MaxValue) : _prefetch;

            var consumer = new MessageQueueDirectConsumer(this, queue, autoAcknowledge, consumerPrefetch, (level, message) => Log(level, message));
            consumer.Disposed += (sender, id) => _consumers.TryRemove(id, out _);
            _consumers.TryAdd(consumer.Id, consumer);
            consumer.Start();

            return Task.FromResult(Ok<ITrakHoundStream<TrakHoundQueueMessage>>(consumer));
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>> Pull(string queue, int count, bool autoAcknowledge, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(queue)) return BadRequest<IEnumerable<TrakHoundQueueMessage>>();

            try
            {
                var results = new List<TrakHoundQueueMessage>();

                if (count > 0)
                {
                    if (!await EnsureQueue(queue, cancellationToken)) return NotAvailable<IEnumerable<TrakHoundQueueMessage>>("RabbitMQ Broker Not Available");

                    // Pulled Messages are held on this Channel until they are Acknowledged or Rejected (it is separate from
                    // the Publish Channel, so a Publish error can't close it and return them to the Queue)
                    var connected = await UseChannel(() => _pullChannel, o => _pullChannel = o, _pullLock, false, async (channel) =>
                    {
                        for (var i = 0; i < count; i++)
                        {
                            var clientResult = await channel.Channel.BasicGetAsync(queue, autoAcknowledge, cancellationToken);
                            if (clientResult == null) break; // No more messages found

                            results.Add(CreateMessage(queue, channel, clientResult.DeliveryTag, clientResult.Redelivered, clientResult.BasicProperties, clientResult.Body));
                        }
                    }, cancellationToken);

                    if (!connected) return NotAvailable<IEnumerable<TrakHoundQueueMessage>>("RabbitMQ Broker Not Available");
                }

                Log(TrakHoundLogLevel.Debug, $"RabbitMQ : Pull() : Received {results.Count} Messages from ({queue})");

                return Ok<IEnumerable<TrakHoundQueueMessage>>(results);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Error, $"RabbitMQ : Pull() : Error ({queue}) : {ex.Message}");
                return InternalError<IEnumerable<TrakHoundQueueMessage>>(ex.Message);
            }
        }

        public async Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, Stream content, TrakHoundQueuePublishOptions options = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(queue) || content == null) return BadRequest<TrakHoundQueuePublishResult>();

            try
            {
                var stpw = System.Diagnostics.Stopwatch.StartNew();

                var body = await ReadContent(content, cancellationToken);

                if (!await EnsureQueue(queue, cancellationToken)) return NotAvailable<TrakHoundQueuePublishResult>("RabbitMQ Broker Not Available");

                var messageId = !string.IsNullOrEmpty(options?.MessageId) ? options.MessageId : Guid.NewGuid().ToString();

                var properties = new BasicProperties();
                properties.Persistent = true;
                properties.MessageId = messageId;
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                if (!string.IsNullOrEmpty(options?.ContentType)) properties.ContentType = options.ContentType;
                if (!options?.Headers.IsNullOrEmpty() ?? false) properties.Headers = options.Headers.ToDictionary(o => o.Key, o => (object)o.Value);

                var connected = await UseChannel(() => _publishChannel, o => _publishChannel = o, _publishLock, _publishConfirm, async (channel) =>
                {
                    if (_publishConfirm)
                    {
                        // With confirmations tracked, the publish completes once the Broker confirms the Message was stored
                        // (otherwise a dropped Message would still return Ok), and throws if the Broker rejects it
                        using var confirmTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        confirmTimeout.CancelAfter(_publishConfirmTimeout);

                        try
                        {
                            await channel.Channel.BasicPublishAsync("", queue, false, properties, body, confirmTimeout.Token);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            throw new TimeoutException($"The Broker did not confirm the Message within {_publishConfirmTimeout.TotalMilliseconds}ms");
                        }
                    }
                    else
                    {
                        await channel.Channel.BasicPublishAsync("", queue, false, properties, body, cancellationToken);
                    }
                }, cancellationToken);

                if (!connected) return NotAvailable<TrakHoundQueuePublishResult>("RabbitMQ Broker Not Available");

                stpw.Stop();
                Log(TrakHoundLogLevel.Debug, $"RabbitMQ : Publish() : Message Sent to ({queue}) in {stpw.ElapsedMilliseconds}ms");

                return Ok(new TrakHoundQueuePublishResult(messageId));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException ex)
            {
                Log(TrakHoundLogLevel.Error, $"RabbitMQ : Publish() : Timeout ({queue}) : {ex.Message}");
                return Timeout<TrakHoundQueuePublishResult>(ex.Message);
            }
            catch (PublishException ex)
            {
                Log(TrakHoundLogLevel.Error, $"RabbitMQ : Publish() : Rejected ({queue}) : {ex.Message}");
                return InternalError<TrakHoundQueuePublishResult>("The Broker rejected the Message");
            }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Error, $"RabbitMQ : Publish() : Error ({queue}) : {ex.Message}");
                return InternalError<TrakHoundQueuePublishResult>(ex.Message);
            }
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Acknowledge(string queue, IEnumerable<string> deliveryIds, CancellationToken cancellationToken = default)
        {
            return ProcessDeliveries("Acknowledge", queue, deliveryIds, (channel, deliveryTag) => channel.BasicAckAsync(deliveryTag, false, cancellationToken).AsTask(), cancellationToken);
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Reject(string queue, IEnumerable<string> deliveryIds, bool requeue = true, CancellationToken cancellationToken = default)
        {
            // Without requeue, the Broker discards the Message (or dead-letters it, if the Queue has a dead letter exchange)
            return ProcessDeliveries("Reject", queue, deliveryIds, (channel, deliveryTag) => channel.BasicRejectAsync(deliveryTag, requeue, cancellationToken).AsTask(), cancellationToken);
        }

        private async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> ProcessDeliveries(string operation, string queue, IEnumerable<string> deliveryIds, Func<IChannel, ulong, Task> action, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(queue) || deliveryIds.IsNullOrEmpty()) return BadRequest<IEnumerable<TrakHoundQueueDeliveryResult>>();

            var results = new List<TrakHoundQueueDeliveryResult>();

            foreach (var deliveryId in deliveryIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var channel = GetChannel(deliveryId);
                var deliveryTag = GetDeliveryTag(deliveryId);

                if (channel != null && deliveryTag > 0)
                {
                    try
                    {
                        await channel.Run((o) => action(o, deliveryTag), cancellationToken);

                        results.Add(new TrakHoundQueueDeliveryResult(deliveryId, true));
                        Log(TrakHoundLogLevel.Debug, $"RabbitMQ : {operation}() : {operation} Sent ({queue})");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        results.Add(new TrakHoundQueueDeliveryResult(deliveryId, false, ex.Message));
                        Log(TrakHoundLogLevel.Error, $"RabbitMQ : {operation}() : Error ({queue}) : {ex.Message}");
                    }
                }
                else
                {
                    // The Delivery ID is invalid or was issued on a Connection/Channel that has since closed (the broker will redeliver the message)
                    results.Add(new TrakHoundQueueDeliveryResult(deliveryId, false, "The Channel the Message was delivered on has closed, so the Broker returned it to the Queue"));
                    Log(TrakHoundLogLevel.Warning, $"RabbitMQ : {operation}() : {operation} Error ({queue}) : Delivery ID Not Found : {deliveryId}");
                }
            }

            return Ok<IEnumerable<TrakHoundQueueDeliveryResult>>(results);
        }


        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>> GetMetrics(IEnumerable<string> queues = null, CancellationToken cancellationToken = default)
        {
            var requestedQueues = queues?.Where(o => !string.IsNullOrEmpty(o)).Distinct().ToList();
            if (requestedQueues != null && requestedQueues.Count < 1) requestedQueues = null;

            try
            {
                // Management API : every Queue, with Message rates
                var metrics = await ReadManagementMetrics(cancellationToken);
                if (metrics != null)
                {
                    if (requestedQueues != null) metrics = metrics.Where(o => requestedQueues.Contains(o.Queue)).ToList();
                    return Ok<IEnumerable<TrakHoundMessageQueueMetrics>>(metrics.OrderBy(o => o.Queue, StringComparer.OrdinalIgnoreCase).ToList());
                }

                // AMQP fallback : Ready counts of the requested Queues (or the Queues used by this Service), no rates
                var channelMetrics = await ReadChannelMetrics(requestedQueues ?? _knownQueues.Keys.ToList(), cancellationToken);
                if (channelMetrics == null) return NotAvailable<IEnumerable<TrakHoundMessageQueueMetrics>>("RabbitMQ Broker Not Available");

                return Ok<IEnumerable<TrakHoundMessageQueueMetrics>>(channelMetrics.OrderBy(o => o.Queue, StringComparer.OrdinalIgnoreCase).ToList());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Error, $"RabbitMQ : GetMetrics() : Error : {ex.Message}");
                return InternalError<IEnumerable<TrakHoundMessageQueueMetrics>>(ex.Message);
            }
        }

        /// <summary>
        /// Reads the Metrics of every Queue from the management API. Returns null if the API is not available
        /// </summary>
        private async Task<List<TrakHoundMessageQueueMetrics>> ReadManagementMetrics(CancellationToken cancellationToken)
        {
            if (_managementClient == null) return null;

            // After a failure, wait before trying again (a host that drops packets makes every attempt wait the full timeout)
            if (_managementAvailable == false && DateTime.UtcNow < _managementRetryTime) return null;

            try
            {
                var metrics = await _managementClient.GetQueueMetrics(cancellationToken);

                if (_managementAvailable != true)
                {
                    _managementAvailable = true;
                    Log(TrakHoundLogLevel.Information, $"RabbitMQ : Management API Connected ({_managementClient.BaseUrl})");
                }

                return metrics;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _managementRetryTime = DateTime.UtcNow + _managementRetryInterval;

                // Only log when the state changes (Metrics are polled)
                if (_managementAvailable != false)
                {
                    _managementAvailable = false;
                    Log(TrakHoundLogLevel.Warning, $"RabbitMQ : Management API Not Available ({_managementClient.BaseUrl}) : {ex.Message} : Metrics are limited to the Ready count of known Queues (no Message rates). Enable the rabbitmq_management plugin or set managementUrl / managementPort");
                }

                return null;
            }
        }

        /// <summary>
        /// Reads the Ready and Consumer counts of each Queue with a passive Queue Declare (AMQP has no Queue list or Message rates).
        /// Returns null if no Connection to the broker could be made
        /// </summary>
        private async Task<List<TrakHoundMessageQueueMetrics>> ReadChannelMetrics(IEnumerable<string> queueNames, CancellationToken cancellationToken)
        {
            var (connection, _) = await Connect(cancellationToken);
            if (connection == null) return null;

            var results = new List<TrakHoundMessageQueueMetrics>();
            var timestamp = UnixDateTime.Now;

            foreach (var queueName in queueNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Its own Channel, since a passive Declare of a missing Queue closes the Channel
                    await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                    var queue = await channel.QueueDeclarePassiveAsync(queueName, cancellationToken);

                    results.Add(new TrakHoundMessageQueueMetrics
                    {
                        Queue = queueName,
                        MessageCount = queue.MessageCount,
                        ReadyCount = queue.MessageCount,
                        ConsumerCount = (int)queue.ConsumerCount,
                        Timestamp = timestamp
                    });
                }
                catch (OperationInterruptedException)
                {
                    // The Queue doesn't exist (the Broker closed the Channel)
                    _knownQueues.TryRemove(queueName, out _);
                }
            }

            return results;
        }


        private static async Task<ReadOnlyMemory<byte>> ReadContent(Stream content, CancellationToken cancellationToken)
        {
            if (content.CanSeek) content.Seek(0, SeekOrigin.Begin);

            // Avoid copying when the content is already in memory
            if (content is MemoryStream contentMemoryStream && contentMemoryStream.TryGetBuffer(out var buffer))
            {
                return buffer;
            }

            using (var memoryStream = new MemoryStream())
            {
                await content.CopyToAsync(memoryStream, cancellationToken);
                return memoryStream.ToArray();
            }
        }


        #region "Messages"

        /// <summary>
        /// Creates the Message for a delivery (the Body is copied since it is only valid while the delivery is being handled)
        /// </summary>
        internal static TrakHoundQueueMessage CreateMessage(string queue, RabbitMqChannel channel, ulong deliveryTag, bool redelivered, IReadOnlyBasicProperties properties, ReadOnlyMemory<byte> body)
        {
            var message = new TrakHoundQueueMessage();
            message.Queue = queue;
            message.DeliveryId = CreateDeliveryId(channel.ConnectionId, channel.ChannelNumber, deliveryTag);
            message.Content = body.ToArray();
            message.Redelivered = redelivered;
            message.ReceivedTimestamp = UnixDateTime.Now;

            if (properties != null)
            {
                if (properties.IsMessageIdPresent()) message.MessageId = properties.MessageId;
                if (properties.IsContentTypePresent()) message.ContentType = properties.ContentType;
                if (properties.IsHeadersPresent()) message.Headers = ReadHeaders(properties.Headers);

                // AMQP Timestamps are whole seconds
                if (properties.IsTimestampPresent() && properties.Timestamp.UnixTime > 0) message.PublishedTimestamp = properties.Timestamp.UnixTime * 1_000_000_000;
            }

            return message;
        }

        /// <summary>
        /// Converts AMQP Header values to strings (string values are sent as UTF-8 bytes)
        /// </summary>
        internal static Dictionary<string, string> ReadHeaders(IDictionary<string, object> headers)
        {
            if (headers.IsNullOrEmpty()) return null;

            var results = new Dictionary<string, string>();
            foreach (var header in headers)
            {
                switch (header.Value)
                {
                    case null: results[header.Key] = null; break;
                    case byte[] bytes: results[header.Key] = Encoding.UTF8.GetString(bytes); break;
                    case AmqpTimestamp timestamp: results[header.Key] = timestamp.UnixTime.ToString(CultureInfo.InvariantCulture); break;
                    case IFormattable formattable: results[header.Key] = formattable.ToString(null, CultureInfo.InvariantCulture); break;
                    default: results[header.Key] = header.Value.ToString(); break;
                }
            }

            return results;
        }

        #endregion

        #region "Connection"

        /// <summary>
        /// Returns the current open Connection (and its ID), (re)connecting if needed. The Connection is null if the broker can't be reached.
        /// </summary>
        private async Task<(IConnection Connection, string ConnectionId)> Connect(CancellationToken cancellationToken)
        {
            await _connectionLock.WaitAsync(cancellationToken);
            try
            {
                if (!_disposed && _connectionFactory != null && (_connection == null || !_connection.IsOpen))
                {
                    await CloseConnection();

                    try
                    {
                        var stpw = System.Diagnostics.Stopwatch.StartNew();

                        _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

                        // Create a unique connection ID (used for DeliveryId)
                        _connectionId = Guid.NewGuid().ToString();

                        stpw.Stop();
                        Log(TrakHoundLogLevel.Debug, $"RabbitMQ Client : Connected Successfully : {stpw.ElapsedMilliseconds}ms");
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (BrokerUnreachableException ex)
                    {
                        Log(TrakHoundLogLevel.Error, $"RabbitMQ Client : Client Connection : Broker Unreachable : {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Log(TrakHoundLogLevel.Error, $"RabbitMQ Client : Client Connection : Exception : {ex.Message}");
                    }
                }

                return _connection != null && _connection.IsOpen ? (_connection, _connectionId) : (null, null);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        /// <summary>
        /// Closes the current Connection. Must be called while holding _connectionLock.
        /// </summary>
        private async Task CloseConnection()
        {
            // Channels (and their Delivery Tags) are only valid on the Connection that created them
            lock (_channelsLock) _channels.Clear();

            // A new Connection may be to a different Broker (ex. a restarted node), so declare Queues again
            _declaredQueues.Clear();

            if (_connection != null)
            {
                try
                {
                    if (_connection.IsOpen) await _connection.CloseAsync();
                    await _connection.DisposeAsync();

                    Log(TrakHoundLogLevel.Debug, $"RabbitMQ Client : Connection Closed");
                }
                catch (Exception ex)
                {
                    Log(TrakHoundLogLevel.Error, $"RabbitMQ Client : Close Connection : Exception : {ex.Message}");
                }

                _connection = null;
                _connectionId = null;
            }
        }

        #endregion

        #region "Queues"

        /// <summary>
        /// Declares the Queue (durable) the first time it is used on a Connection. If the Queue already exists with
        /// different settings (ex. a quorum Queue, or one with a TTL), it is used as it is. Returns false if no
        /// Connection to the broker could be made. Uses its own Channels, since a failed Declare closes its Channel.
        /// </summary>
        internal async Task<bool> EnsureQueue(string queue, CancellationToken cancellationToken)
        {
            var (connection, _) = await Connect(cancellationToken);
            if (connection == null) return false;
            if (_declaredQueues.ContainsKey(queue)) return true;

            try
            {
                await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                await channel.QueueDeclareAsync(queue: queue, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
            }
            catch (OperationInterruptedException ex) when (ex.ShutdownReason?.ReplyCode == _preconditionFailed)
            {
                // The Queue exists with other settings. A passive Declare only checks that it exists
                await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
                await channel.QueueDeclarePassiveAsync(queue, cancellationToken);

                Log(TrakHoundLogLevel.Debug, $"RabbitMQ : Queue ({queue}) exists with different settings, so it is used as it is : {ex.ShutdownReason?.ReplyText}");
            }

            _declaredQueues.TryAdd(queue, 0);
            _knownQueues.TryAdd(queue, 0);
            return true;
        }

        #endregion

        #region "Channels"

        /// <summary>
        /// Opens a new Channel (used by a Consumer) after declaring the Queue. Close using CloseChannel().
        /// </summary>
        internal async Task<RabbitMqChannel> CreateChannel(string queue, CancellationToken cancellationToken)
        {
            try
            {
                if (await EnsureQueue(queue, cancellationToken)) return await OpenChannel(null, false, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Error, $"RabbitMQ : CreateChannel() : Declare Queue Error ({queue}) : {ex.Message}");
            }

            return null;
        }

        private async Task<RabbitMqChannel> OpenChannel(SemaphoreSlim channelLock, bool confirm, CancellationToken cancellationToken)
        {
            try
            {
                var (connection, connectionId) = await Connect(cancellationToken);
                if (connection != null)
                {
                    var options = new CreateChannelOptions(publisherConfirmationsEnabled: confirm, publisherConfirmationTrackingEnabled: confirm);
                    var channel = new RabbitMqChannel(connectionId, await connection.CreateChannelAsync(options, cancellationToken), channelLock);

                    lock (_channelsLock)
                    {
                        // Channel Numbers are only unique per Connection
                        _channels[channel.ChannelNumber] = channel;
                    }

                    return channel;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Error, $"RabbitMQ : OpenChannel() : Error : {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Closes the Channel. Doesn't take its Lock (a long lived Channel is closed while its owner holds it)
        /// </summary>
        internal async Task CloseChannel(RabbitMqChannel channel)
        {
            if (channel != null)
            {
                lock (_channelsLock)
                {
                    if (_channels.TryGetValue(channel.ChannelNumber, out var existing) && existing == channel)
                    {
                        _channels.Remove(channel.ChannelNumber);
                    }
                }

                try
                {
                    if (channel.IsOpen) await channel.Channel.CloseAsync();
                    await channel.Channel.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Log(TrakHoundLogLevel.Debug, $"RabbitMQ : CloseChannel() : Channel Dispose Exception : {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Runs the action on a long lived Channel (Pull or Publish), reopening it if it has closed or belongs to a previous Connection.
        /// Returns false if no Connection to the broker could be made.
        /// </summary>
        private async Task<bool> UseChannel(Func<RabbitMqChannel> get, Action<RabbitMqChannel> set, SemaphoreSlim channelLock, bool confirm, Func<RabbitMqChannel, Task> action, CancellationToken cancellationToken)
        {
            await channelLock.WaitAsync(cancellationToken);
            try
            {
                var channel = get();
                if (channel == null || !channel.IsOpen || channel.ConnectionId != _connectionId)
                {
                    await CloseChannel(channel);

                    channel = await OpenChannel(channelLock, confirm, cancellationToken);
                    set(channel);
                    if (channel == null) return false;
                }

                await action(channel);
                return true;
            }
            finally
            {
                channelLock.Release();
            }
        }

        private RabbitMqChannel GetChannel(string deliveryId)
        {
            var connectionId = GetConnectionId(deliveryId);
            var channelNumber = GetChannelNumber(deliveryId);

            lock (_channelsLock)
            {
                if (_channels.TryGetValue(channelNumber, out var channel) && channel.ConnectionId == connectionId && channel.IsOpen)
                {
                    return channel;
                }
            }

            return null;
        }

        #endregion

        #region "Delivery IDs"

        internal static string CreateDeliveryId(string connectionId, int channelNumber, ulong deliveryTag)
        {
            return $"{connectionId}:{channelNumber}:{deliveryTag}";
        }

        internal static string GetConnectionId(string deliveryId)
        {
            var parts = deliveryId?.Split(':');
            return parts != null && parts.Length > 2 ? parts[0] : null;
        }

        internal static int GetChannelNumber(string deliveryId)
        {
            var parts = deliveryId?.Split(':');
            return parts != null && parts.Length > 2 ? parts[1].ToInt() : 0;
        }

        internal static ulong GetDeliveryTag(string deliveryId)
        {
            var parts = deliveryId?.Split(':');
            return parts != null && parts.Length > 2 ? parts[2].ToULong() : 0;
        }

        #endregion
    }
}
