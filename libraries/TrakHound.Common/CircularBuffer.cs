// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound
{
    /// <summary>
    /// A set of fixed-length Circular Buffers, one per Key
    /// </summary>
    public class CircularBuffer<TKey, TValue>
    {
        private readonly Dictionary<TKey, CircularBuffer<TValue>> _buffers = new Dictionary<TKey, CircularBuffer<TValue>>();
        private readonly int _bufferLength;
        private readonly object _lock = new object();

        /// <summary>
        /// Creates a new CircularBuffer where each Key's buffer holds up to the specified number of Items
        /// </summary>
        public CircularBuffer(int length)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
            _bufferLength = length;
        }

        /// <summary>
        /// Adds an Item to the buffer for the specified Key, creating the buffer if needed (null Keys and Items are ignored)
        /// </summary>
        public void Add(TKey key, TValue item)
        {
            if (key == null || item == null) return;

            CircularBuffer<TValue> buffer;
            lock (_lock)
            {
                if (!_buffers.TryGetValue(key, out buffer))
                {
                    buffer = new CircularBuffer<TValue>(_bufferLength);
                    _buffers.Add(key, buffer);
                }
            }

            buffer.Add(item); // inner buffer has its own lock
        }

        /// <summary>
        /// Returns a snapshot of the Items for the specified Key, newest first (empty if the Key is not found)
        /// </summary>
        public TValue[] Get(TKey key)
        {
            if (key == null) return Array.Empty<TValue>();

            CircularBuffer<TValue> buffer;
            lock (_lock) _buffers.TryGetValue(key, out buffer);
            return buffer?.Get() ?? Array.Empty<TValue>();
        }
    }

    /// <summary>
    /// A thread-safe fixed-length buffer that overwrites the oldest Item once the Limit is reached
    /// </summary>
    public class CircularBuffer<TValue>
    {
        private readonly object _lock = new object();
        private readonly TValue[] _items;
        private int _head; // next write position
        private int _count;


        /// <summary>
        /// The maximum number of Items the Buffer can hold
        /// </summary>
        public int Limit => _items.Length;

        /// <summary>
        /// Gets the current number of Items in the Buffer
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock) return _count;
            }
        }


        /// <summary>
        /// Creates a new CircularBuffer that holds up to the specified number of Items
        /// </summary>
        public CircularBuffer(int limit = 500000)
        {
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
            _items = new TValue[limit];
        }


        /// <summary>
        /// Returns a snapshot of the Items, newest first
        /// </summary>
        public TValue[] Get()
        {
            lock (_lock)
            {
                if (_count == 0) return Array.Empty<TValue>();

                var result = new TValue[_count];
                var index = _head;
                for (var i = 0; i < _count; i++)
                {
                    index = index == 0 ? _items.Length - 1 : index - 1;
                    result[i] = _items[index];
                }

                return result;
            }
        }

        /// <summary>
        /// Adds an Item to the Buffer, overwriting the oldest Item if the Buffer is full (null Items are ignored)
        /// </summary>
        public void Add(TValue item)
        {
            if (item == null) return;

            lock (_lock)
            {
                _items[_head] = item;
                _head = (_head + 1) % _items.Length;
                if (_count < _items.Length) _count++;
            }
        }

        /// <summary>
        /// Removes all Items from the Buffer
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_items, 0, _items.Length);
                _head = 0;
                _count = 0;
            }
        }
    }
}
