// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Functions
{
    /// <summary>
    /// A row of the summary query (see Controller.GetSummaryQuery) : the Runs of a Function, of a Function's Scheduled Task,
    /// or of a Function in a time bucket (Grouping). Durations are nanoseconds
    /// </summary>
    internal class FunctionSummaryRow
    {
        // grouping_id([task_id], [bucket]) : 1 for each Column that is not grouped
        public const int TaskGrouping = 1;
        public const int BucketGrouping = 2;
        public const int FunctionGrouping = 3;


        public int Grouping { get; set; }

        public string FunctionId { get; set; }

        public string TaskId { get; set; }

        public long? Bucket { get; set; }

        public int RunCount { get; set; }

        public int SuccessCount { get; set; }

        public int CancelledCount { get; set; }

        public int ScheduledCount { get; set; }

        public int ManualCount { get; set; }

        public int ApiCount { get; set; }

        public long TotalDuration { get; set; }

        public long MaxDuration { get; set; }

        public DateTime? FirstStart { get; set; }

        /// <summary>
        /// Runs that started by the end of the range that Schedule occurrences are compared in
        /// </summary>
        public int ScheduleRunCount { get; set; }

        public string FunctionLastRunId { get; set; }

        public DateTime? FunctionLastRun { get; set; }

        public int? FunctionLastStatusCode { get; set; }

        public string TaskLastRunId { get; set; }

        public DateTime? TaskLastRun { get; set; }

        public int? TaskLastStatusCode { get; set; }
    }
}
