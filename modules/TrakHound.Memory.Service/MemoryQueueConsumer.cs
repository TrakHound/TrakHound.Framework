// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using System.Threading.Channels;
using TrakHound.Services.MessageQueues;

namespace TrakHound.Memory.Service
{
    /// <summary>
    /// A Subscription to a Memory Queue. Messages are raised in the order they are delivered, one at a time.
    /// Stopping or disposing it returns its unacknowledged Messages to the Queue
    /// </summary>
    internal sealed class MemoryQueueConsumer : ITrakHoundStream<TrakHoundQueueMessage>
    {
        private readonly MemoryQueue _queue;
        private readonly Action<TrakHoundLogLevel, string> _log;
        private readonly object _lock = new object();
        private Channel<TrakHoundQueueMessage> _buffer;
        private CancellationTokenSource _stop;
        private bool _workerStarted;
        private bool _disposed;


        public string Id { get; } = Guid.NewGuid().ToString();

        public bool AutoAcknowledge { get; }

        public int Prefetch { get; }

        /// <summary>
        /// Messages delivered to this Subscription that haven't been Acknowledged (changed by the Queue while holding its lock)
        /// </summary>
        internal int Unacknowledged { get; set; }

        public CancellationToken CancellationToken => _stop?.Token ?? default;

        public Type OutputType => typeof(TrakHoundQueueMessage);


        public event EventHandler<TrakHoundQueueMessage> Received;

        public event EventHandler<string> Disposed;


        public Action<TrakHoundQueueMessage> OnReceived { get; set; }

        public Func<TrakHoundQueueMessage, Task> OnReceivedAsync { get; set; }

        public Action OnDisposed { get; set; }


        public MemoryQueueConsumer(MemoryQueue queue, bool autoAcknowledge, int prefetch, Action<TrakHoundLogLevel, string> log)
        {
            _queue = queue;
            AutoAcknowledge = autoAcknowledge;
            Prefetch = Math.Max(prefetch, 1);
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


        /// <summary>
        /// Joins the Queue: Messages are delivered to the buffer from now on, but aren't raised until Start() is called,
        /// so none are raised before the caller attaches its handlers
        /// </summary>
        internal void Attach()
        {
            lock (_lock)
            {
                if (_disposed || _stop != null) return;

                _buffer = Channel.CreateUnbounded<TrakHoundQueueMessage>(new UnboundedChannelOptions { SingleReader = true });
                _stop = new CancellationTokenSource();
            }

            _queue.AddConsumer(this);
        }

        public void Start()
        {
            Attach(); // Rejoins the Queue if it was Stopped

            lock (_lock)
            {
                if (_disposed || _stop == null || _workerStarted) return;

                _workerStarted = true;
                var buffer = _buffer;
                var token = _stop.Token;
                _ = Task.Run(() => Worker(buffer, token));
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_stop == null) return;

                _stop.Cancel();
                _stop = null;
                _workerStarted = false;
                _buffer.Writer.TryComplete();
            }

            // Messages that weren't Acknowledged (including ones still in the buffer) go back to the Queue
            _queue.RemoveConsumer(this);
        }

        public Task<bool> Push(TrakHoundQueueMessage value) => Task.FromResult(false);


        /// <summary>
        /// Called by the Queue (while holding its lock), so it only buffers the Message
        /// </summary>
        internal void Enqueue(TrakHoundQueueMessage message)
        {
            _buffer?.Writer.TryWrite(message);
        }

        private async Task Worker(Channel<TrakHoundQueueMessage> buffer, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var message in buffer.Reader.ReadAllAsync(cancellationToken))
                {
                    try
                    {
                        OnReceived?.Invoke(message);
                        if (OnReceivedAsync != null) await OnReceivedAsync(message);
                        Received?.Invoke(this, message);
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke(TrakHoundLogLevel.Error, $"Memory Message Queues : Consumer ({_queue.Name}) : Message Handler Error : {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}
