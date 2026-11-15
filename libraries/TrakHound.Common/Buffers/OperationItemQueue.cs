// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Buffers
{
    /// <summary>
    /// A thread-safe, fixed capacity in-memory FIFO Queue of Items
    /// </summary>
    public class OperationItemQueue<TItem> : IDisposable
    {
        private readonly object _lock = new object();
        private readonly int _itemLimit;
        private TItem[] _items;
        private int _itemIndex;
        private int _itemCount = 0;
        private long _lastAccessed;


        /// <summary>
        /// The maximum number of Items the Queue can hold
        /// </summary>
        public int Limit => _itemLimit;

        /// <summary>
        /// Gets the current number of Items in the Queue
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock) return _itemCount;
            }
        }


        /// <summary>
        /// Creates a new Queue that holds up to the specified number of Items (the storage array is allocated on the first Add)
        /// </summary>
        public OperationItemQueue(int limit = 5000000)
        {
            _itemLimit = limit;
            _itemIndex = 0;
        }

        /// <summary>
        /// The Queue holds nothing to release (kept so callers can Dispose it like the File Buffer)
        /// </summary>
        public void Dispose() { }


        /// <summary>
        /// Take (n) number of TItems and remove them from queue
        /// </summary>
        public int Take(ref TItem[] buffer, int bufferIndex, int count)
        {
            lock (_lock)
            {
                if (_itemCount > 0 && _items != null && bufferIndex >= 0 && bufferIndex < buffer.Length && count > 0)
                {
                    var take = count < _itemCount ? count : _itemCount;

                    Array.Copy(_items, 0, buffer, bufferIndex, take);

                    var remaining = _itemCount - take;

                    if (remaining > 0)
                    {
                        Array.Copy(_items, take, _items, 0, remaining);
                    }

                    _itemIndex -= take;
                    _itemCount -= take;

                    _lastAccessed = UnixDateTime.Now;

                    return take;
                }
            }

            return 0;
        }

        /// <summary>
        /// Get (n) Items from Queue without removing them
        /// </summary>
        public int Get(ref TItem[] buffer, int bufferIndex, int count)
        {
            lock (_lock)
            {
                if (_itemCount > 0 && _items != null && bufferIndex >= 0 && bufferIndex < buffer.Length && count > 0)
                {
                    var take = count < _itemCount ? count : _itemCount;

                    Array.Copy(_items, 0, buffer, bufferIndex, take);

                    return take;
                }
            }

            return 0;
        }

        /// <summary>
        /// Removes (n) Items from top of Queue
        /// </summary>
        public int Remove(int count)
        {
            lock (_lock)
            {
                if (_itemCount > 0 && _items != null && count > 0)
                {
                    var take = count < _itemCount ? count : _itemCount;
                    var remaining = _itemCount - take;

                    if (remaining > 0)
                    {
                        Array.Copy(_items, take, _items, 0, remaining);
                    }

                    _itemIndex -= take;
                    _itemCount -= take;

                    return take;
                }
            }

            return 0;
        }

        /// <summary>
        /// Adds an Item to the end of the Queue. Returns false if the Item is null or the Queue is full
        /// </summary>
        public bool Add(TItem item)
        {
            if (item != null)
            {
                lock (_lock)
                {
                    if (_itemIndex < _itemLimit)
                    {
                        if (_items == null) _items = new TItem[_itemLimit];

                        _items[_itemIndex] = item;
                        _itemIndex++;
                        _itemCount++;

                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Adds as many Items as will fit, starting at offset. Null Items are skipped.
        /// Returns the number of source Items consumed (Items from offset + consumed onward did not fit)
        /// </summary>
        public int AddRange(IReadOnlyList<TItem> items, int offset = 0)
        {
            if (items == null || offset < 0 || offset >= items.Count) return 0;

            lock (_lock)
            {
                var i = offset;
                for (; i < items.Count; i++)
                {
                    var item = items[i];
                    if (item == null) continue;

                    if (_itemIndex >= _itemLimit) break;

                    if (_items == null) _items = new TItem[_itemLimit];

                    _items[_itemIndex] = item;
                    _itemIndex++;
                    _itemCount++;
                }

                return i - offset;
            }
        }

        /// <summary>
        /// Adds all of the Items to the end of the Queue. Returns false (and adds nothing) if the Items are empty or do not all fit
        /// </summary>
        public bool Add(IEnumerable<TItem> items)
        {
            if (!items.IsNullOrEmpty())
            {
                lock (_lock)
                {
                    var remainingSize = _itemLimit - _itemCount;
                    if (items.Count() <= remainingSize)
                    {
                        if (_items == null) _items = new TItem[_itemLimit];

                        foreach (var item in items)
                        {
                            if (_itemIndex < _items.Length)
                            {
                                _items[_itemIndex] = item;
                                _itemIndex++;
                                _itemCount++;
                            }
                            else
                            {
                                return false;
                            }
                        }

                        return true;
                    }
                }
            }

            return false;
        }
    }
}
