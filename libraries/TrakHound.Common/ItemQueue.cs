// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound
{
    /// <summary>
    /// A thread-safe queue of Items with an optional maximum size
    /// </summary>
    public class ItemQueue<T>
    {
        private readonly int _limit;
        // A plain FIFO (Items are not de-duplicated). Dictionary enumeration order isn't guaranteed after removals, so Take wasn't FIFO
        private readonly Queue<T> _items = new Queue<T>();
        private readonly object _lock = new object();


        /// <summary>
        /// Gets the current number of Items in the Queue
        /// </summary>
        public long Count
        {
            get
            {
                lock (_lock) return _items.Count;
            }
        }


        /// <summary>
        /// Creates a new ItemQueue that holds up to the specified number of Items
        /// </summary>
        public ItemQueue(int limit = int.MaxValue)
        {
            _limit = limit;
        }


        /// <summary>
        /// Take (n) number of Items and remove from the Queue
        /// </summary>
        public IEnumerable<T> Take(int count = 1)
        {
            lock (_lock)
            {
                var n = Math.Min(count, _items.Count);
                if (n > 0)
                {
                    var x = new List<T>(n);
                    for (var i = 0; i < n; i++) x.Add(_items.Dequeue());
                    return x;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds an Item to the queue (returns false if the Item is null or the queue is full)
        /// </summary>
        public bool Add(T item)
        {
            if (item != null)
            {
                lock (_lock)
                {
                    if (_items.Count >= _limit) return false;

                    _items.Enqueue(item);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Removes all Items from the queue
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _items.Clear();
            }
        }
    }
}
