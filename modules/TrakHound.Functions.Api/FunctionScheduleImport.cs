// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using TrakHound.ScheduledTasks;

namespace TrakHound.Functions
{
    /// <summary>
    /// Validates the Jobs of an Import, decides what happens to each one, and checks that every Task's Job exists (nothing is written here)
    /// </summary>
    internal static class FunctionScheduleImport
    {
        /// <summary>
        /// Returns an error message when the Document isn't an Export this version can read (null when it can). A Scheduled Tasks Api Export (Tasks only) can be imported too
        /// </summary>
        public static string ValidateDocument(FunctionScheduleExport document)
        {
            if (document == null) return "Request Body is required";
            if (!string.IsNullOrEmpty(document.Format) && document.Format != FunctionScheduleExport.FormatName && document.Format != ScheduledTaskExport.FormatName)
            {
                return $"Unsupported Format : {document.Format} (expected {FunctionScheduleExport.FormatName})";
            }
            if (document.Version > FunctionScheduleExport.CurrentVersion) return $"Unsupported Version : {document.Version} (this version reads {FunctionScheduleExport.CurrentVersion} and older)";
            if (document.Jobs.IsNullOrEmpty() && document.Tasks.IsNullOrEmpty()) return "No Jobs or Tasks to import";
            return null;
        }

        public static List<FunctionJobImportItem> PlanJobs(IEnumerable<FunctionJob> jobs, IEnumerable<FunctionJob> existingJobs, ScheduledTaskImportMode mode)
        {
            var items = new List<FunctionJobImportItem>();
            var existing = existingJobs?.Where(o => o != null).ToList() ?? new List<FunctionJob>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var incoming in jobs ?? Enumerable.Empty<FunctionJob>())
            {
                var item = new FunctionJobImportItem();
                items.Add(item);

                if (incoming == null)
                {
                    item.Action = ScheduledTaskImportAction.Invalid;
                    AddMessage(item, "Job is empty");
                    continue;
                }

                var job = Normalize(incoming);
                item.FunctionId = job.FunctionId;
                item.Id = job.Id;
                item.Name = job.Name;

                var errors = new List<string>();
                var idError = ConfigurationId.Validate(job.Id, "Job ID");
                if (idError != null) errors.Add($"{idError} (Tasks reference Jobs by ID)");
                if (string.IsNullOrEmpty(job.FunctionId)) errors.Add("Function ID is required");
                if (string.IsNullOrEmpty(job.Name)) errors.Add("Job Name is required");
                if (job.Id != null && job.FunctionId != null && !seenKeys.Add(GetKey(job.FunctionId, job.Id))) errors.Add($"Job {job.Id} (Function ID = {job.FunctionId}) is in the Import more than once");
                if (errors.Count > 0)
                {
                    item.Action = ScheduledTaskImportAction.Invalid;
                    item.Messages = errors;
                    continue;
                }

                var current = existing.FirstOrDefault(o => IsSameKey(o, job.FunctionId, job.Id));
                if (current == null)
                {
                    item.Action = ScheduledTaskImportAction.Create;
                    item.Job = job;
                    continue;
                }

                // The existing Job's IDs (Tasks must reference them exactly, even when the Import differs in case)
                item.FunctionId = current.FunctionId;
                item.Id = current.Id;

                if (IsSame(current, job))
                {
                    item.Action = ScheduledTaskImportAction.Unchanged;
                }
                else
                {
                    switch (mode)
                    {
                        case ScheduledTaskImportMode.Overwrite:
                            item.Action = ScheduledTaskImportAction.Update;
                            job.Id = current.Id; // Keep the existing Row (even when the ID differs in case)
                            job.FunctionId = current.FunctionId;
                            item.Job = job;
                            break;

                        case ScheduledTaskImportMode.Skip:
                            item.Action = ScheduledTaskImportAction.Skip;
                            AddMessage(item, "A different Job with this ID exists (kept)");
                            break;

                        default:
                            item.Action = ScheduledTaskImportAction.Conflict;
                            AddMessage(item, "A different Job with this ID exists (import with mode=overwrite to replace it, or mode=skip to keep it)");
                            break;
                    }
                }
            }

            CheckNames(items, existing);
            return items;
        }

        /// <summary>
        /// Job Names are unique for a Function : checked against the Jobs the Function has after the Import
        /// </summary>
        private static void CheckNames(List<FunctionJobImportItem> items, List<FunctionJob> existing)
        {
            var written = items.Where(o => o.Job != null).ToList();
            if (written.Count == 0) return;

            var replacedKeys = new HashSet<string>(written.Select(o => GetKey(o.Job.FunctionId, o.Job.Id)), StringComparer.OrdinalIgnoreCase);
            var final = existing.Where(o => !replacedKeys.Contains(GetKey(o.FunctionId, o.Id))).Concat(written.Select(o => o.Job)).ToList();

            foreach (var item in written)
            {
                var duplicate = final.FirstOrDefault(o => o != item.Job &&
                    string.Equals(o.FunctionId, item.Job.FunctionId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(o.Name?.Trim(), item.Job.Name, StringComparison.OrdinalIgnoreCase));

                if (duplicate != null)
                {
                    item.Action = ScheduledTaskImportAction.Invalid;
                    AddMessage(item, $"A Job named \"{duplicate.Name}\" (ID = {duplicate.Id}) is already used for this Function : Job Names are unique for a Function");
                }
            }

            foreach (var item in items.Where(o => o.Action == ScheduledTaskImportAction.Invalid)) item.Job = null;
        }

        /// <summary>
        /// The Jobs a Task can reference after the Import : the existing Jobs and the valid Jobs in the Import
        /// </summary>
        public static HashSet<(string FunctionId, string Id)> GetAvailableJobs(IEnumerable<FunctionJob> existingJobs, IEnumerable<FunctionJobImportItem> items)
        {
            var available = new HashSet<(string, string)>();

            foreach (var job in existingJobs ?? Enumerable.Empty<FunctionJob>())
            {
                if (job?.FunctionId != null && job.Id != null) available.Add((job.FunctionId.ToLowerInvariant(), job.Id));
            }

            foreach (var item in items ?? Enumerable.Empty<FunctionJobImportItem>())
            {
                if (item.Action != ScheduledTaskImportAction.Invalid && item.FunctionId != null && item.Id != null) available.Add((item.FunctionId.ToLowerInvariant(), item.Id));
            }

            return available;
        }

        /// <summary>
        /// The Tasks whose Job isn't on this Instance or in the Import (Task ID => error). Job IDs are compared exactly, the same as when the Task runs
        /// </summary>
        public static Dictionary<string, string> CheckTaskJobs(IEnumerable<ScheduledTaskConfiguration> tasks, HashSet<(string FunctionId, string Id)> availableJobs)
        {
            var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in tasks ?? Enumerable.Empty<ScheduledTaskConfiguration>())
            {
                var taskId = Trim(task?.Id);
                var functionId = Trim(task?.FunctionId);
                var jobId = Trim(task?.JobId);
                if (taskId == null || functionId == null || jobId == null) continue; // The Scheduled Tasks Api reports a missing ID or Function

                if (!availableJobs.Contains((functionId.ToLowerInvariant(), jobId)))
                {
                    errors[taskId] = $"Job Not Found : {jobId} (Function ID = {functionId}) : add the Job to the Import, or create it on this Instance first";
                }
            }

            return errors;
        }

        /// <summary>
        /// Marks the Tasks whose Job wasn't found as invalid
        /// </summary>
        public static void ApplyTaskErrors(ScheduledTaskImportResult result, Dictionary<string, string> taskErrors)
        {
            if (result == null || taskErrors.IsNullOrEmpty()) return;

            var errors = new Dictionary<string, string>(taskErrors, StringComparer.OrdinalIgnoreCase);

            foreach (var item in result.Tasks)
            {
                if (item.Id != null && errors.TryGetValue(item.Id, out var error))
                {
                    item.Action = ScheduledTaskImportAction.Invalid;
                    item.Messages ??= new List<string>();
                    item.Messages.Add(error);
                }
            }

            ScheduledTaskImport.UpdateCounts(result);
        }

        public static bool HasErrors(IEnumerable<FunctionJobImportItem> items)
        {
            return items.Any(o => o.Action == ScheduledTaskImportAction.Invalid || o.Action == ScheduledTaskImportAction.Conflict || o.Action == ScheduledTaskImportAction.Error);
        }

        private static FunctionJob Normalize(FunctionJob job)
        {
            return new FunctionJob
            {
                Id = Trim(job.Id),
                FunctionId = Trim(job.FunctionId),
                Name = Trim(job.Name),
                Description = Trim(job.Description),
                Parameters = FunctionJob.CleanParameters(job.Parameters)
            };
        }

        private static bool IsSame(FunctionJob current, FunctionJob job)
        {
            if (Trim(current.Name) != job.Name) return false;
            if (Trim(current.Description) != job.Description) return false;

            var a = current.Parameters ?? new Dictionary<string, string>();
            var b = job.Parameters ?? new Dictionary<string, string>();
            return a.Count == b.Count && a.All(o => b.TryGetValue(o.Key, out var value) && value == o.Value);
        }

        private static bool IsSameKey(FunctionJob job, string functionId, string id)
        {
            return string.Equals(job.FunctionId, functionId, StringComparison.OrdinalIgnoreCase) && string.Equals(job.Id, id, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetKey(string functionId, string id) => $"{functionId}\n{id}";

        private static string Trim(string value) => !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        private static void AddMessage(FunctionJobImportItem item, string message)
        {
            item.Messages ??= new List<string>();
            item.Messages.Add(message);
        }
    }
}
