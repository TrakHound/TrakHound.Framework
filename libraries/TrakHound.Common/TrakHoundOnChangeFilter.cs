// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text;

namespace TrakHound
{
    /// <summary>
    /// Filters values so that only changes are passed through, tracking the last value for each Key
    /// </summary>
    public class TrakHoundOnChangeFilter
    {
        private readonly Dictionary<string, string> _values;
        private readonly object _lock = new object();


        /// <summary>
        /// Creates a new, empty TrakHoundOnChangeFilter
        /// </summary>
        public TrakHoundOnChangeFilter()
        {
            _values = new Dictionary<string, string>();
        }


        /// <summary>
        /// Clears all of the stored values
        /// </summary>
        public void Clear()
        {
            lock (_lock) _values.Clear();
        }


        /// <summary>
        /// Returns true (and stores the value) if the value is different from the last value stored for the Key (always true the first time a Key is seen)
        /// </summary>
        public bool Filter(string key, string value)
        {
            if (!string.IsNullOrEmpty(key))
            {
                lock (_lock)
                {
                    var update = false;

                    var existing = _values.GetValueOrDefault(key);
                    if (existing != null)
                    {
                        update = existing != value;
                    }
                    else
                    {
                        update = true;
                    }

                    if (update)
                    {
                        if (existing != null) _values.Remove(key);
                        _values.Add(key, value);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Creates a Key by joining the parts with ':'
        /// </summary>
        public static string CreateKey(params string[] parts)
        {
            if (parts != null)
            {
                var builder = new StringBuilder();
                for (var i = 0; i < parts.Length; i++)
                {
                    builder.Append(parts[i]);
                    if (i < parts.Length - 1) builder.Append(':');
                }
                return builder.ToString();
            }

            return null;
        }
    }
}
