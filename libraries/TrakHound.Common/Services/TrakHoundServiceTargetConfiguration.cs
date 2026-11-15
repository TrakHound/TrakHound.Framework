// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Services
{
    /// <summary>
    /// Configuration used to configure Route Targets
    /// </summary>
    public class TrakHoundServiceTargetConfiguration
    {
        /// <summary>
        /// A unique Identifier for the Target
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The Configuration ID of the Service ("*" for any Service) or the ID of the Router to direct Requests to
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// The Type of Target (Service or Router)
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// The results (names of <see cref="TrakHoundServiceResultType"/>, ex. "NotFound") that send the Request on to the Route's next Target.
        /// Null uses <see cref="TrakHoundServiceFallback.Default"/>, and an empty list never falls back
        /// </summary>
        public IEnumerable<string> FallbackOn { get; set; }

        /// <summary>
        /// Creates a Target Configuration with a new unique Id
        /// </summary>
        public TrakHoundServiceTargetConfiguration()
        {
            Id = Guid.NewGuid().ToString();
        }
    }
}
