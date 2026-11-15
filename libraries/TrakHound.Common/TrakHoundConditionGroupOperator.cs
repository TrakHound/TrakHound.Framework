// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// The logical operator used to combine the Conditions in a Condition Group
    /// </summary>
    public enum TrakHoundConditionGroupOperator
    {
        /// <summary>
        /// All Conditions in the group must match
        /// </summary>
        AND,
        /// <summary>
        /// At least one Condition in the group must match
        /// </summary>
        OR
    }
}
