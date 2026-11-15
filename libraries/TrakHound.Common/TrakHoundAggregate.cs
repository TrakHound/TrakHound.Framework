// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// An aggregate Value calculated for a Target
    /// </summary>
    public class TrakHoundAggregate
    {
        /// <summary>
        /// The Target (ex. Object path or Uuid) that the Value was calculated for
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// The aggregate Value
        /// </summary>
        public double Value { get; set; }


        /// <summary>
        /// Creates a new TrakHoundAggregate for the specified Target and Value
        /// </summary>
        public TrakHoundAggregate(string target, double value)
        {
            Target = target;
            Value = value;
        }
    }
}
