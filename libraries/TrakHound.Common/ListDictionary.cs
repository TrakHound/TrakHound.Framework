// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrakHound
{
    /// <summary>
    /// A thread-safe Dictionary that stores a unique set of Values for each Key
    /// </summary>
    public class ListDictionary<TKey, TValue>
    {
        private readonly Dictionary<TKey, HashSet<TValue>> _dictionary = new Dictionary<TKey, HashSet<TValue>>();
        private readonly object _lock = new object();
        private int _count = 0;


        /// <summary>
        /// The total number of Values across all Keys
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock) return _count;
            }
        }

        /// <summary>
        /// Gets a snapshot of the Keys
        /// </summary>
        public IEnumerable<TKey> Keys
        {
            get
            {
                lock (_lock) return _dictionary.Keys.ToList();
            }
        }

        /// <summary>
        /// Gets a snapshot of the Values across all Keys
        /// </summary>
        public IEnumerable<TValue> Values
        {
            get
            {
                lock (_lock) return _dictionary.Values.SelectMany(x => x).ToList();
            }
        }


        /// <summary>
        /// Gets the number of Values for the specified Key (-1 if the Key is not found)
        /// </summary>
        public int GetCount(TKey key)
        {
            if (key != null)
            {
                lock (_lock)
                {
                    if (_dictionary.TryGetValue(key, out var values)) return values.Count;
                }
            }

            return -1;
        }


        /// <summary>
        /// Adds the Value to the set for the specified Key (returns false if the Key or Value is null)
        /// </summary>
        public bool Add(TKey key, TValue value)
        {
            if (key != null && value != null)
            {
                lock (_lock)
                {
                    if (GetOrCreateSet(key).Add(value)) _count++;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds the Values to the set for the specified Key (returns true if any non-null Value was added)
        /// </summary>
        public bool Add(TKey key, IEnumerable<TValue> values)
        {
            if (key != null && values != null)
            {
                var added = false;

                lock (_lock)
                {
                    HashSet<TValue> set = null;
                    foreach (var value in values)
                    {
                        if (value != null)
                        {
                            if (set == null) set = GetOrCreateSet(key);
                            if (set.Add(value)) _count++;
                            added = true;
                        }
                    }
                }

                return added;
            }

            return false;
        }

        /// <summary>
        /// Gets a snapshot of the Values for the specified Key (null if the Key is not found)
        /// </summary>
        public IEnumerable<TValue> Get(TKey key)
        {
            if (key != null)
            {
                lock (_lock)
                {
                    if (_dictionary.TryGetValue(key, out var values)) return values.ToList();
                }
            }

            return null;
        }

        /// <summary>
        /// Returns true if the specified Key exists
        /// </summary>
        public bool ContainsKey(TKey key)
        {
            if (key != null)
            {
                lock (_lock) return _dictionary.ContainsKey(key);
            }

            return false;
        }

        /// <summary>
        /// Returns true if the specified Key contains the specified Value
        /// </summary>
        public bool Contains(TKey key, TValue value)
        {
            if (key != null && value != null)
            {
                lock (_lock) return _dictionary.TryGetValue(key, out var values) && values.Contains(value);
            }

            return false;
        }

        /// <summary>
        /// Returns true if every Key contains the specified Value
        /// </summary>
        public bool AllKeysContains(TValue value)
        {
            if (value != null)
            {
                lock (_lock) return _dictionary.Values.All(x => x.Contains(value));
            }

            return false;
        }

        /// <summary>
        /// Removes the Key and all of its Values
        /// </summary>
        public void Remove(TKey key)
        {
            if (key != null)
            {
                lock (_lock)
                {
                    if (_dictionary.TryGetValue(key, out var values))
                    {
                        _count -= values.Count;
                        _dictionary.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        /// Removes the Value from the specified Key (the Key is removed when it has no Values left)
        /// </summary>
        public void Remove(TKey key, TValue value)
        {
            if (key != null && value != null)
            {
                lock (_lock)
                {
                    if (_dictionary.TryGetValue(key, out var values) && values.Remove(value))
                    {
                        _count--;
                        if (values.Count == 0) _dictionary.Remove(key);
                    }
                }
            }
        }

        /// <summary>
        /// Removes all Keys and Values
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _dictionary.Clear();
                _count = 0;
            }
        }


        private HashSet<TValue> GetOrCreateSet(TKey key)
        {
            if (!_dictionary.TryGetValue(key, out var values))
            {
                values = new HashSet<TValue>();
                _dictionary.Add(key, values);
            }

            return values;
        }
    }


    /// <summary>
    /// Extension methods for creating a ListDictionary
    /// </summary>
    public static class ListDictionaryExtensions
    {
        /// <summary>
        /// Creates a ListDictionary from the source, grouping each item by the Key returned by keySelector (null if the source is empty)
        /// </summary>
        public static ListDictionary<TKey, TSource> ToListDictionary<TKey, TSource>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            if (!source.IsNullOrEmpty() && keySelector != null)
            {
                var results = new ListDictionary<TKey, TSource>();
                foreach (var item in source)
                {
                    var key = keySelector(item);
                    if (key != null)
                    {
                        results.Add(key, item);
                    }
                }
                return results;
            }

            return null;
        }

        /// <summary>
        /// Creates a ListDictionary from the source using the specified Key and Value selectors (null if the source is empty)
        /// </summary>
        public static ListDictionary<TKey, TValue> ToListDictionary<TKey, TValue, TSource>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector)
        {
            if (!source.IsNullOrEmpty() && keySelector != null && valueSelector != null)
            {
                var results = new ListDictionary<TKey, TValue>();
                foreach (var item in source)
                {
                    var key = keySelector(item);
                    var value = valueSelector(item);
                    if (key != null)
                    {
                        results.Add(key, value);
                    }
                }
                return results;
            }

            return null;
        }
    }
}
