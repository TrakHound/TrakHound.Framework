// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TrakHound.Cron;

namespace TrakHound.ScheduledTasks
{
    /// <summary>
    /// Validates the Tasks of an Import and decides what happens to each one (nothing is written here)
    /// </summary>
    internal static class ScheduledTaskImport
    {
        public static bool TryParseMode(string value, out ScheduledTaskImportMode mode)
        {
            mode = ScheduledTaskImportMode.Fail;
            if (string.IsNullOrWhiteSpace(value)) return true;
            return Enum.TryParse(value.Trim(), true, out mode) && Enum.IsDefined(mode);
        }

        public static string GetModeName(ScheduledTaskImportMode mode) => mode.ToString().ToLowerInvariant();

        /// <summary>
        /// Returns an error message when the Document isn't a Scheduled Tasks Export this version can read (null when it can)
        /// </summary>
        public static string ValidateDocument(ScheduledTaskExport document)
        {
            if (document == null) return "Request Body is required";
            if (!string.IsNullOrEmpty(document.Format) && document.Format != ScheduledTaskExport.FormatName) return $"Unsupported Format : {document.Format} (expected {ScheduledTaskExport.FormatName})";
            if (document.Version > ScheduledTaskExport.CurrentVersion) return $"Unsupported Version : {document.Version} (this version reads {ScheduledTaskExport.CurrentVersion} and older)";
            if (document.Tasks.IsNullOrEmpty()) return "No Tasks to import";
            return null;
        }

        /// <param name="existing">The Tasks on this Instance by their File ID</param>
        public static ScheduledTaskImportResult Plan(IEnumerable<ScheduledTaskConfiguration> tasks, IReadOnlyDictionary<string, ScheduledTaskConfiguration> existing, ScheduledTaskImportMode mode, bool dryRun)
        {
            var result = new ScheduledTaskImportResult();
            result.DryRun = dryRun;
            result.Mode = GetModeName(mode);

            // Compared ignoring case (File names are on Windows)
            var existingById = new Dictionary<string, KeyValuePair<string, ScheduledTaskConfiguration>>(StringComparer.OrdinalIgnoreCase);
            if (existing != null) foreach (var entry in existing) existingById[entry.Key] = entry;

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in tasks ?? Enumerable.Empty<ScheduledTaskConfiguration>())
            {
                var item = new ScheduledTaskImportItem();
                result.Tasks.Add(item);

                if (task == null)
                {
                    SetInvalid(item, "Task is empty");
                    continue;
                }

                var configuration = Normalize(task);
                item.Id = configuration.Id;
                item.FunctionId = configuration.FunctionId;

                var errors = Validate(configuration);
                if (configuration.Id != null && !seenIds.Add(configuration.Id)) errors.Add($"Task ID {configuration.Id} is in the Import more than once");
                if (errors.Count > 0)
                {
                    item.Action = ScheduledTaskImportAction.Invalid;
                    item.Messages = errors;
                    continue;
                }

                if (!task.Parameters.IsNullOrEmpty()) AddMessage(item, "Parameters are ignored (Scheduled Tasks get their Parameters from a Job)");

                if (!existingById.TryGetValue(configuration.Id, out var current) || current.Value == null)
                {
                    item.Action = ScheduledTaskImportAction.Create;
                    item.FileId = configuration.Id;
                    item.Configuration = configuration;
                    continue;
                }

                var currentConfiguration = Normalize(current.Value);
                currentConfiguration.Id ??= current.Key; // The Worker uses the File ID when the Task has none

                if (IsSame(currentConfiguration, configuration))
                {
                    item.Action = ScheduledTaskImportAction.Unchanged;
                }
                else
                {
                    switch (mode)
                    {
                        case ScheduledTaskImportMode.Overwrite:
                            item.Action = ScheduledTaskImportAction.Update;
                            item.FileId = current.Key; // Replace the existing File (even when its name differs in case)
                            item.Configuration = configuration;
                            break;

                        case ScheduledTaskImportMode.Skip:
                            item.Action = ScheduledTaskImportAction.Skip;
                            AddMessage(item, "A different Task with this ID exists (kept)");
                            break;

                        default:
                            item.Action = ScheduledTaskImportAction.Conflict;
                            AddMessage(item, "A different Task with this ID exists (import with mode=overwrite to replace it, or mode=skip to keep it)");
                            break;
                    }
                }
            }

            UpdateCounts(result);
            return result;
        }

        public static void UpdateCounts(ScheduledTaskImportResult result)
        {
            result.Created = result.Tasks.Count(o => o.Action == ScheduledTaskImportAction.Create);
            result.Updated = result.Tasks.Count(o => o.Action == ScheduledTaskImportAction.Update);
            result.Unchanged = result.Tasks.Count(o => o.Action == ScheduledTaskImportAction.Unchanged);
            result.Skipped = result.Tasks.Count(o => o.Action == ScheduledTaskImportAction.Skip);
            result.Conflicts = result.Tasks.Count(o => o.Action == ScheduledTaskImportAction.Conflict);
            result.Invalid = result.Tasks.Count(o => o.Action == ScheduledTaskImportAction.Invalid);
            result.Errors = result.Tasks.Count(o => o.Action == ScheduledTaskImportAction.Error);
            result.Success = result.Conflicts == 0 && result.Invalid == 0 && result.Errors == 0;
        }

        /// <summary>
        /// A copy with trimmed values, empty values as null, and without the Parameters (no longer used)
        /// </summary>
        public static ScheduledTaskConfiguration Normalize(ScheduledTaskConfiguration task)
        {
            return new ScheduledTaskConfiguration
            {
                Id = Trim(task.Id),
                Enabled = task.Enabled,
                Description = Trim(task.Description),
                FunctionId = Trim(task.FunctionId),
                JobId = Trim(task.JobId),
                Schedule = Trim(task.Schedule),
                TimeZone = Trim(task.TimeZone),
                RunMissed = task.RunMissed
            };
        }

        public static List<string> Validate(ScheduledTaskConfiguration task)
        {
            var errors = new List<string>();

            var idError = ConfigurationId.Validate(task.Id, "Task ID");
            if (idError != null) errors.Add(idError);

            if (string.IsNullOrEmpty(task.FunctionId)) errors.Add("Function ID is required");

            var scheduleError = ValidateSchedule(task.Schedule);
            if (scheduleError != null) errors.Add(scheduleError);

            var timeZoneError = ValidateTimeZone(task.TimeZone);
            if (timeZoneError != null) errors.Add(timeZoneError);

            if (task.JobId != null)
            {
                var jobIdError = ConfigurationId.Validate(task.JobId, "Job ID");
                if (jobIdError != null) errors.Add(jobIdError);
            }

            return errors;
        }

        /// <summary>
        /// The same format detection as the Scheduled Tasks Worker (6 fields = with seconds)
        /// </summary>
        public static string ValidateSchedule(string schedule)
        {
            if (string.IsNullOrWhiteSpace(schedule)) return "Schedule is required";

            try
            {
                var trimmed = schedule.Trim();
                var format = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5 ? CronFormat.IncludeSeconds : CronFormat.Standard;
                CronExpression.Parse(trimmed, format);
                return null;
            }
            catch (Exception ex)
            {
                return $"Invalid Schedule : {schedule} : {ex.Message}";
            }
        }

        /// <summary>
        /// IANA or Windows ID (the server's local Time Zone when empty)
        /// </summary>
        public static string ValidateTimeZone(string timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId)) return null;

            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
                return null;
            }
            catch (Exception)
            {
                return $"Time Zone Not Found : {timeZoneId}";
            }
        }

        private static bool IsSame(ScheduledTaskConfiguration a, ScheduledTaskConfiguration b)
        {
            return JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
        }

        private static string Trim(string value) => !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        private static void SetInvalid(ScheduledTaskImportItem item, string message)
        {
            item.Action = ScheduledTaskImportAction.Invalid;
            AddMessage(item, message);
        }

        private static void AddMessage(ScheduledTaskImportItem item, string message)
        {
            item.Messages ??= new List<string>();
            item.Messages.Add(message);
        }
    }
}
