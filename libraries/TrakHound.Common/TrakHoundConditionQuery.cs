// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound
{
    /// <summary>
    /// A single Condition that compares a Target to a Pattern using an Operator
    /// </summary>
    public struct TrakHoundConditionQuery
    {
        /// <summary>
        /// The Identifier of the Condition
        /// </summary>
        public string ConditionId { get; set; }

        /// <summary>
        /// The operator used to compare the Target to the Pattern
        /// </summary>
        public TrakHoundConditionOperator Operator { get; set; } 

        /// <summary>
        /// The Target to compare
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// The Pattern (value) to compare the Target to
        /// </summary>
        public string Pattern { get; set; }


        /// <summary>
        /// Creates a new Condition (a new ConditionId is generated if one is not specified)
        /// </summary>
        public TrakHoundConditionQuery(string target, TrakHoundConditionOperator conditionOperator, string pattern, string conditionId = null)
        {
            Target = target;
            Operator = conditionOperator;
            Pattern = pattern;
            ConditionId = conditionId ?? Guid.NewGuid().ToString();
        }
    }
}
