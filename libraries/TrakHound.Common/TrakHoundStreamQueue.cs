// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace TrakHound
{
    /// <summary>
    /// Runs work items one at a time in the order they were added, so Items from input Streams are processed in order
    /// </summary>
    internal sealed class TrakHoundStreamQueue
    {
        private const int Capacity = 10000;

        private readonly Channel<Func<Task>> _channel;
        private readonly Action<Exception> _onError;
        private int _started;
        private long _dropped;


        /// <summary>
        /// The number of work items dropped because the queue was full (the oldest item is dropped)
        /// </summary>
        public long Dropped => Interlocked.Read(ref _dropped);


        public TrakHoundStreamQueue(Action<Exception> onError)
        {
            _onError = onError;

            // Bounded so one slow consumer can't grow a Stream's queue without limit. DropOldest never blocks a producer (TryWrite always succeeds)
            var options = new BoundedChannelOptions(Capacity) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest };
            _channel = Channel.CreateBounded<Func<Task>>(options, dropped => Interlocked.Increment(ref _dropped));
        }


        public void Add(Func<Task> work)
        {
            // The processing loop starts on the first item so Streams that never receive input don't hold a Task
            if (_channel.Writer.TryWrite(work) && Interlocked.Exchange(ref _started, 1) == 0)
            {
                _ = Task.Run(Process);
            }
        }

        public void Complete() => _channel.Writer.TryComplete();


        private async Task Process()
        {
            await foreach (var work in _channel.Reader.ReadAllAsync())
            {
                try
                {
                    await work();
                }
                catch (Exception ex)
                {
                    _onError?.Invoke(ex);
                }
            }
        }
    }
}
