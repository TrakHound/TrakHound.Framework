// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// The comparison operator used by a Condition
    /// </summary>
    public enum TrakHoundConditionOperator
    {
        /// <summary>
        /// The Target is equal to the Pattern
        /// </summary>
        EQUALS,
        /// <summary>
        /// The Target is not equal to the Pattern
        /// </summary>
        NOT_EQUALS,
        /// <summary>
        /// The Target is greater than the Pattern
        /// </summary>
        GREATER_THAN,
        /// <summary>
        /// The Target is greater than or equal to the Pattern
        /// </summary>
        GREATER_THAN_OR_EQUAL,
        /// <summary>
        /// The Target is less than the Pattern
        /// </summary>
        LESS_THAN,
        /// <summary>
        /// The Target is less than or equal to the Pattern
        /// </summary>
        LESS_THAN_OR_EQUAL,
        /// <summary>
        /// The Target matches the wildcard Pattern
        /// </summary>
        LIKE,
        /// <summary>
        /// The Target has the Pattern as a member
        /// </summary>
        HAS_MEMBER,
        /// <summary>
        /// The Target is a member of the Pattern
        /// </summary>
        MEMBER_OF
    }
}
