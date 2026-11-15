// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TrakHound
{
    /// <summary>
    /// A Stream that queues incoming Items and outputs them in batches on a fixed interval
    /// </summary>
    public class TrakHoundQueueConsumer<T> : TrakHoundStream<IEnumerable<T>>
    {
        private readonly ITrakHoundStream<T> _internalConsumer;
        private readonly ITrakHoundStream<IEnumerable<T>> _internalListConsumer;
        private readonly ItemIntervalQueue<T> _queue;


        /// <summary>
        /// Creates a new Queue Consumer with the specified Id. Interval is in milliseconds and Limit is the maximum number of Items per batch
        /// </summary>
        public TrakHoundQueueConsumer(string consumerId, int interval = 5000, int limit = 1000) : base(consumerId)
        {
            OnInputReceived = ProcessMessage;
            OnDisposed = DisposeInternal;

            _queue = new ItemIntervalQueue<T>(interval, limit);
            _queue.ItemsReceived += QueueItemsReceived;
        }

        /// <summary>
        /// Creates a new Queue Consumer that queues the Items received from the specified Stream. Interval is in milliseconds and Limit is the maximum number of Items per batch
        /// </summary>
        public TrakHoundQueueConsumer(ITrakHoundStream<T> consumer, string consumerId = null, int interval = 5000, int limit = 1000) : base(consumerId)
        {
            if (consumer != null)
            {
                _internalConsumer = consumer;
                _internalConsumer.Received += InternalConsumerReceived;
                _internalConsumer.Disposed += InternalConsumerDisposed;
            }

            OnInputReceived = ProcessMessage;
            OnDisposed = DisposeInternal;

            _queue = new ItemIntervalQueue<T>(interval, limit);
            _queue.ItemsReceived += QueueItemsReceived;
        }

        /// <summary>
        /// Creates a new Queue Consumer that queues the batches of Items received from the specified Stream. Interval is in milliseconds and Limit is the maximum number of Items per batch
        /// </summary>
        public TrakHoundQueueConsumer(ITrakHoundStream<IEnumerable<T>> consumer, string consumerId = null, int interval = 5000, int limit = 1000) : base(consumerId)
        {
            if (consumer != null)
            {
                _internalListConsumer = consumer;
                _internalListConsumer.Received += InternalConsumerReceived;
                _internalListConsumer.Disposed += InternalConsumerDisposed;
            }

            OnInputReceived = ProcessMessage;
            OnDisposed = DisposeInternal;

            _queue = new ItemIntervalQueue<T>(interval, limit);
            _queue.ItemsReceived += QueueItemsReceived;
        }

        /// <summary>
        /// Adds an Item to the queue to be output with the next batch (null Items are ignored)
        /// </summary>
        public virtual bool Push(T item)
        {
            if (item != null) _queue.Add(item);

            return true;
        }

        private void InternalConsumerReceived(object sender, T item)
        {
            if (item != null) _queue.Add(item);
        }

        private void InternalConsumerReceived(object sender, IEnumerable<T> items)
        {
            if (items != null) _queue.Add(items);
        }

        private void InternalConsumerDisposed(object sender, string e)
        {
            _queue.Dispose();
            Dispose();
        }

        private void DisposeInternal()
        {
            _queue.Dispose();

            if (_internalConsumer != null)
            {
                _internalConsumer.Received -= InternalConsumerReceived;
                _internalConsumer.Disposed -= InternalConsumerDisposed;
                if (!LeaveInputsOpen) _internalConsumer.Dispose();
            }

            if (_internalListConsumer != null)
            {
                _internalListConsumer.Received -= InternalConsumerReceived;
                _internalListConsumer.Disposed -= InternalConsumerDisposed;
                if (!LeaveInputsOpen) _internalListConsumer.Dispose();
            }
        }

        private IEnumerable<T> ProcessMessage(IEnumerable<T> items)
        {
            if (items != null)
            {
                _queue.Add(items);
            }

            return default;
        }

        private void QueueItemsReceived(object sender, IEnumerable<T> items)
        {
            if (!items.IsNullOrEmpty())
            {
                _ = Emit(items);
            }
        }
    }

    /// <summary>
    /// A Stream that queues incoming Items and outputs them one at a time on a fixed interval
    /// </summary>
    public class TrakHoundSingleQueueConsumer<TInput, TOutput> : TrakHoundStream<TInput, TOutput>
    {
        private readonly ItemIntervalQueue<TInput> _queue;

        /// <summary>
        /// Function used to convert each queued Item into the TOutput that is forwarded to subscribers. Without it, an Item that is also a TOutput is forwarded as is and any other is dropped
        /// </summary>
        public Func<TInput, TOutput> OnConvert { get; set; }


        /// <summary>
        /// Creates a new Single Queue Consumer with the specified Id. Interval is in milliseconds and Limit is the maximum number of Items processed per interval
        /// </summary>
        public TrakHoundSingleQueueConsumer(string consumerId, int interval = 5000, int limit = 1000) : base(consumerId)
        {
            OnInputReceived = ProcessMessage;
            OnDisposed = DisposeInternal;
            _queue = new ItemIntervalQueue<TInput>(interval, limit);
            _queue.ItemsReceived += QueueItemsReceived;
        }

        // Stops the queue's polling worker, which would otherwise outlive the Stream
        private void DisposeInternal()
        {
            _queue.Dispose();
        }

        private TOutput ProcessMessage(TInput item)
        {
            if (item != null)
            {
                _queue.Add(item);
            }

            return default;
        }

        private void QueueItemsReceived(object sender, IEnumerable<TInput> items)
        {
            if (!items.IsNullOrEmpty())
            {
                _ = EmitItems(items);
            }
        }

        private async Task EmitItems(IEnumerable<TInput> items)
        {
            foreach (var item in items)
            {
                try
                {
                    if (OnConvert != null) await Emit(OnConvert(item));
                    else if (item is TOutput output) await Emit(output);
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                }
            }
        }
    }

    /// <summary>
    /// A Stream that queues incoming Items and converts each batch into a single TOutput on a fixed interval
    /// </summary>
    public class TrakHoundQueueConsumer<TInput, TOutput> : TrakHoundStream<IEnumerable<TInput>, TOutput>
    {
        private readonly ItemIntervalQueue<TInput> _queue;

        /// <summary>
        /// Function used to convert each batch of queued Items into the TOutput that is forwarded to subscribers (batches are dropped when it isn't set)
        /// </summary>
        public Func<IEnumerable<TInput>, TOutput> OnConvert { get; set; }


        /// <summary>
        /// Creates a new Queue Consumer that receives batches of Items from the specified Streams. Interval is in milliseconds and Limit is the maximum number of Items per batch
        /// </summary>
        public TrakHoundQueueConsumer(IEnumerable<ITrakHoundStream<IEnumerable<TInput>>> consumers, int interval = 5000, int limit = 1000) : base(consumers)
        {
            base.OnInputReceived = ProcessMessage;
            base.OnDisposed = DisposeInternal;
            _queue = new ItemIntervalQueue<TInput>(interval, limit);
            _queue.ItemsReceived += QueueItemsReceived;
        }

        /// <summary>
        /// Creates a new Queue Consumer with the specified Id. Interval is in milliseconds and Limit is the maximum number of Items per batch
        /// </summary>
        public TrakHoundQueueConsumer(string consumerId, int interval = 5000, int limit = 1000) : base(consumerId)
        {
            base.OnInputReceived = ProcessMessage;
            base.OnDisposed = DisposeInternal;
            _queue = new ItemIntervalQueue<TInput>(interval, limit);
            _queue.ItemsReceived += QueueItemsReceived;
        }

        // Stops the queue's polling worker, which would otherwise outlive the Stream
        private void DisposeInternal()
        {
            _queue.Dispose();
        }

        private TOutput ProcessMessage(IEnumerable<TInput> items)
        {
            if (!items.IsNullOrEmpty())
            {
                _queue.Add(items);
            }

            return default;
        }

        private void QueueItemsReceived(object sender, IEnumerable<TInput> items)
        {
            if (!items.IsNullOrEmpty() && OnConvert != null)
            {
                try
                {
                    _ = Emit(OnConvert(items));
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                }
            }
        }
    }
}
