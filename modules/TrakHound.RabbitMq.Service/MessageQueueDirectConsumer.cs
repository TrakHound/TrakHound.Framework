using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TrakHound.Services.MessageQueues;

namespace TrakHound.RabbitMq.Service
{
    /// <summary>
    /// A Stream that consumes a Queue on its own Channel. Once started, it reopens the Channel (and resumes consuming)
    /// after the Channel or Connection closes until stopped or disposed.
    /// </summary>
    public class MessageQueueDirectConsumer : ITrakHoundStream<TrakHoundQueueMessage>
    {
        private const int _reconnectInterval = 5000;


        private readonly RabbitMqMessageQueueService _service;
        private readonly string _queue;
        private readonly string _consumerTag;
        private readonly bool _autoAcknowledge;
        private readonly int _prefetch;
        private readonly Action<TrakHoundLogLevel, string> _log;
        private readonly object _lock = new object();
        private CancellationTokenSource _stop;
        private Task _worker;
        private bool _disposed;


        public string Id { get; }

        public string Queue => _queue;

        public string ConsumerTag => _consumerTag;

        public CancellationToken CancellationToken => _stop?.Token ?? default;

        public Type OutputType => typeof(TrakHoundQueueMessage);


        public event EventHandler<TrakHoundQueueMessage> Received;

        public event EventHandler<string> Disposed;


        public Action<TrakHoundQueueMessage> OnReceived { get; set; }

        public Func<TrakHoundQueueMessage, Task> OnReceivedAsync { get; set; }

        public Action OnDisposed { get; set; }


        internal MessageQueueDirectConsumer(
            RabbitMqMessageQueueService service,
            string queue,
            bool autoAcknowledge = false,
            int prefetch = 0,
            Action<TrakHoundLogLevel, string> log = null
            )
        {
            Id = Guid.NewGuid().ToString();
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _queue = queue;
            _consumerTag = Guid.NewGuid().ToString();
            _autoAcknowledge = autoAcknowledge;
            _prefetch = Math.Clamp(prefetch, 0, ushort.MaxValue);
            _log = log;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            Stop();

            OnDisposed?.Invoke();
            Disposed?.Invoke(this, Id);
        }


        public void Start()
        {
            lock (_lock)
            {
                // Only one Worker per Consumer
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

        public Task<bool> Push(TrakHoundQueueMessage response) => Task.FromResult(false);


        private async Task Worker(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                RabbitMqChannel channel = null;
                try
                {
                    channel = await _service.CreateChannel(_queue, cancellationToken);
                    if (channel != null) await Consume(channel, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    Log(TrakHoundLogLevel.Error, $"Consume Error : {ex.Message}");
                }
                finally
                {
                    // Closing the Channel returns its unacknowledged Messages to the Queue
                    await _service.CloseChannel(channel);
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(_reconnectInterval, cancellationToken);
                    }
                    catch (OperationCanceledException) { }
                }
            }
        }

        private async Task Consume(RabbitMqChannel channel, CancellationToken cancellationToken)
        {
            var shutdown = new TaskCompletionSource<ShutdownEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            AsyncEventHandler<ShutdownEventArgs> shutdownHandler = (sender, args) =>
            {
                shutdown.TrySetResult(args);
                return Task.CompletedTask;
            };

            var consumer = new AsyncEventingBasicConsumer(channel.Channel);
            consumer.ReceivedAsync += (sender, args) => MessageReceived(channel, args);

            channel.Channel.ChannelShutdownAsync += shutdownHandler;
            try
            {
                await channel.Run(async (o) =>
                {
                    // Limit the unacknowledged Messages the Broker sends at once (otherwise it sends the whole Queue to this Consumer)
                    if (!_autoAcknowledge && _prefetch > 0) await o.BasicQosAsync(0, (ushort)_prefetch, false, cancellationToken);

                    await o.BasicConsumeAsync(_queue, _autoAcknowledge, _consumerTag, consumer, cancellationToken);
                }, cancellationToken);

                // The Channel may have closed before the handler was attached
                if (channel.Channel.IsClosed) shutdown.TrySetResult(channel.Channel.CloseReason);

                Log(TrakHoundLogLevel.Debug, "Consuming");

                // Wait until the Channel closes or the Consumer is Stopped (no polling)
                await Task.WhenAny(shutdown.Task, Task.Delay(Timeout.Infinite, cancellationToken));

                if (shutdown.Task.IsCompleted)
                {
                    Log(TrakHoundLogLevel.Warning, $"Channel Closed : {shutdown.Task.Result?.ReplyText}");
                }
                else if (channel.IsOpen)
                {
                    try { await channel.Run((o) => o.BasicCancelAsync(_consumerTag)); } catch { }
                }
            }
            finally
            {
                channel.Channel.ChannelShutdownAsync -= shutdownHandler;
            }
        }

        private async Task MessageReceived(RabbitMqChannel channel, BasicDeliverEventArgs args)
        {
            try
            {
                Log(TrakHoundLogLevel.Trace, "Message Received");

                // Body is only valid during this handler (CreateMessage copies it)
                var response = RabbitMqMessageQueueService.CreateMessage(_queue, channel, args.DeliveryTag, args.Redelivered, args.BasicProperties, args.Body);

                OnReceived?.Invoke(response);
                if (OnReceivedAsync != null) await OnReceivedAsync(response);
                Received?.Invoke(this, response);
            }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Error, $"Message Handler Error : {ex.Message}");
            }
        }

        private void Log(TrakHoundLogLevel level, string message)
        {
            _log?.Invoke(level, $"RabbitMQ : Consumer : [{_consumerTag}] ({_queue}) : {message}");
        }
    }
}
