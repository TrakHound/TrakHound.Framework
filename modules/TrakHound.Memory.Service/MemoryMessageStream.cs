// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using System.Threading.Channels;
using TrakHound.Logging;
using TrakHound.Services.Messages;

namespace TrakHound.Memory.Service
{
    /// <summary>
    /// A Subscription to a <see cref="MemoryMessageService"/>. Messages are queued in a bounded buffer (dropping the oldest
    /// when full) so a slow consumer never blocks Publish. Messages queued before Start() are delivered once started.
    /// </summary>
    public class MemoryMessageStream : ITrakHoundStream<TrakHoundMessageResponse>
    {
        private readonly Channel<TrakHoundMessageResponse> _channel;
        private readonly string[] _filters;
        private readonly int _qos;
        private readonly object _lock = new object();
        private CancellationTokenSource _stop;
        private Task _worker;
        private long _droppedCount;
        private bool _disposed;


        public string Id { get; }

        public CancellationToken CancellationToken => _stop?.Token ?? default;

        public string ClientId { get; }

        public Type OutputType => typeof(TrakHoundMessageResponse);

        public IEnumerable<string> Filters => _filters;

        public int Qos => _qos;

        /// <summary>
        /// The number of Messages dropped because the buffer was full
        /// </summary>
        public long DroppedCount => Interlocked.Read(ref _droppedCount);


        public event EventHandler<TrakHoundMessageResponse> Received;

        public event EventHandler<string> Disposed;


        public Action<TrakHoundMessageResponse> OnReceived { get; set; }

        public Func<TrakHoundMessageResponse, Task> OnReceivedAsync { get; set; }

        public Action OnDisposed { get; set; }

        public Action<TrakHoundLogItem> OnLog { get; set; }


        public MemoryMessageStream(string clientId, IEnumerable<string> filters, int qos, int bufferSize)
        {
            Id = Guid.NewGuid().ToString();
            ClientId = !string.IsNullOrEmpty(clientId) ? clientId : Id;
            _filters = filters?.Distinct().ToArray() ?? Array.Empty<string>();
            _qos = qos;

            var options = new BoundedChannelOptions(bufferSize);
            options.FullMode = BoundedChannelFullMode.DropOldest;
            options.SingleReader = true;
            options.SingleWriter = false;
            _channel = Channel.CreateBounded<TrakHoundMessageResponse>(options, _ => Interlocked.Increment(ref _droppedCount));
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _stop?.Cancel();
            }

            _channel.Writer.TryComplete();

            OnDisposed?.Invoke();
            Disposed?.Invoke(this, Id);
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

        // Input from the consumer isn't used (Messages are Published through the Service)
        public Task<bool> Push(TrakHoundMessageResponse item) => Task.FromResult(false);


        /// <summary>
        /// Returns true if <paramref name="topic"/> matches any of the Stream's Topic Filters
        /// </summary>
        public bool IsMatch(string topic)
        {
            foreach (var filter in _filters)
            {
                if (TrakHoundMessageTopics.IsMatch(filter, topic)) return true;
            }

            return false;
        }

        /// <summary>
        /// Queues a Message for delivery. Never blocks: the oldest queued Message is dropped if the buffer is full.
        /// </summary>
        internal bool Enqueue(TrakHoundMessageResponse message)
        {
            return _channel.Writer.TryWrite(message);
        }


        private async Task Worker(CancellationToken cancellationToken)
        {
            try
            {
                var reader = _channel.Reader;
                while (await reader.WaitToReadAsync(cancellationToken))
                {
                    while (!cancellationToken.IsCancellationRequested && reader.TryRead(out var message))
                    {
                        try
                        {
                            OnReceived?.Invoke(message);
                            if (OnReceivedAsync != null) await OnReceivedAsync(message);
                            Received?.Invoke(this, message);
                        }
                        catch (Exception ex)
                        {
                            OnLog?.Invoke(new TrakHoundLogItem("Main", TrakHoundLogLevel.Debug, $"Memory Message Stream ({ClientId}) : Error Processing Message : Topic = {message.Topic} : {ex.Message}", null, UnixDateTime.Now));
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        }
    }
}
