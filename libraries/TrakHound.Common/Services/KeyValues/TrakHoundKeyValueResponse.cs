// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Services.KeyValues
{
    /// <summary>
    /// The Values read from a KeyValue Collection
    /// </summary>
    public class TrakHoundKeyValueResponse
    {
        /// <summary>
        /// The Collection the Values were read from
        /// </summary>
        public string Collection { get; set; }

        /// <summary>
        /// The Values that were read, keyed by Key
        /// </summary>
        public IReadOnlyDictionary<string, string> Values { get; set; }
    }
}
