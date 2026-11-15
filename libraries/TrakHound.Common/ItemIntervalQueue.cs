// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound
{
    /// <summary>
    /// A queue that delivers its Items in batches on a fixed interval using a background worker
    /// </summary>
    public class ItemIntervalQueue<T> : IDisposable
    {
        private static readonly object _lock = new object();
        private readonly ItemQueue<T> _queue;
        private readonly int _interval;
        private readonly int _take;
        private CancellationTokenSource stop;
        private bool _isStarted;


        /// <summary>
        /// Handler invoked on each interval with the batch of Items taken from the queue (not invoked when the queue is empty)
        /// </summary>
        public EventHandler<IEnumerable<T>> ItemsReceived { get; set; }


        /// <summary>
        /// Creates a new ItemIntervalQueue and starts the worker. Interval is in milliseconds, Take is the maximum number of Items per batch (0 or less for no limit), and Limit is the maximum number of Items held in the queue
        /// </summary>
        public ItemIntervalQueue(int interval, int take = 100, int limit = int.MaxValue)
        {
            _queue = new ItemQueue<T>(limit);
            _interval = Math.Max(1, interval);
            _take = take > 0 ? take : int.MaxValue;
            Start();
        }

        /// <summary>
        /// Adds an Item to the queue
        /// </summary>
        public void Add(T item)
        {
            _queue.Add(item);
        }

        /// <summary>
        /// Adds the Items to the queue
        /// </summary>
        public void Add(IEnumerable<T> items)
        {
            if (!items.IsNullOrEmpty())
            {
                foreach (var item in items) _queue.Add(item);
            }
        }

        /// <summary>
        /// Removes all Items from the queue
        /// </summary>
        public void Clear()
        {
            _queue.Clear();
        }

        /// <summary>
        /// Stops the background worker
        /// </summary>
        public void Dispose()
        {
            Stop();
        }


        private void Start()
        {
            if (!_isStarted)
            {
                _isStarted = true;

                stop = new CancellationTokenSource();

                _ = Task.Run(() => Worker(stop.Token));
            }
        }

        private void Stop()
        {
            if (_isStarted)
            {
                if (stop != null) stop.Cancel();
                _isStarted = false;
            }
        }


        private async Task Worker(CancellationToken cancellationToken)
        {
            do
            {
                // Caught per batch so one handler exception doesn't stop the worker for good
                try
                {
                    var items = _queue.Take(_take);
                    if (!items.IsNullOrEmpty())
                    {
                        if (ItemsReceived != null) ItemsReceived.Invoke(this, items);
                    }
                }
                catch (Exception) { }

                try
                {
                    await Task.Delay(_interval, cancellationToken);
                }
                catch (TaskCanceledException) { }

            } while (!cancellationToken.IsCancellationRequested);
        }
    }
}
