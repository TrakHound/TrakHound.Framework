// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;

namespace TrakHound.Instance.Health
{
    /// <summary>
    /// A change in a Health Check's reported Status
    /// </summary>
    public class TrakHoundHealthCheckChange
    {
        /// <summary>
        /// The previously reported Result (null for the first evaluation)
        /// </summary>
        public TrakHoundHealthCheckResult Previous { get; init; }

        public TrakHoundHealthCheckResult Current { get; init; }
    }
}
