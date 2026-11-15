// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound
{
    /// <summary>
    /// A group of Conditions (and nested Condition Groups) combined using a logical operator
    /// </summary>
    public struct TrakHoundConditionGroupQuery
    {
        /// <summary>
        /// The Identifier of the Condition Group
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// The operator used to combine the Conditions and nested Condition Groups (AND / OR)
        /// </summary>
        public TrakHoundConditionGroupOperator GroupOperator { get; set; }

        /// <summary>
        /// The nested Condition Groups
        /// </summary>
        public IEnumerable<TrakHoundConditionGroupQuery> ConditionGroups { get; set; }

        /// <summary>
        /// The Conditions in the group
        /// </summary>
        public IEnumerable<TrakHoundConditionQuery> Conditions { get; set; }
    }
}
