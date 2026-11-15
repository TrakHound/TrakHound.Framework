// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound
{
    /// <summary>
    /// Extension methods for creating a Dictionary that keeps only the first item for each Key
    /// </summary>
    public static class UniqueDictionary
    {
        /// <summary>
        /// Creates a Dictionary from the source using the Key returned by keySelector, keeping the first item for duplicate Keys and skipping null Keys (null if the source is empty)
        /// </summary>
        public static Dictionary<TKey, TSource> ToUniqueDictionary<TKey, TSource>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            if (!source.IsNullOrEmpty() && keySelector != null)
            {
                var results = new Dictionary<TKey, TSource>();
                foreach (var item in source)
                {
                    var key = keySelector(item);
                    if (key != null && !results.ContainsKey(key))
                    {
                        results.Add(key, item);
                    }
                }
                return results;
            }

            return null;
        }

        /// <summary>
        /// Creates a Dictionary from the source using the specified Key and Value selectors, keeping the first item for duplicate Keys and skipping null Keys (null if the source is empty)
        /// </summary>
        public static Dictionary<TKey, TValue> ToUniqueDictionary<TKey, TValue, TSource>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector)
        {
            if (!source.IsNullOrEmpty() && keySelector != null && valueSelector != null)
            {
                var results = new Dictionary<TKey, TValue>();
                foreach (var item in source)
                {
                    var key = keySelector(item);
                    var value = valueSelector(item);
                    if (key != null && !results.ContainsKey(key))
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
