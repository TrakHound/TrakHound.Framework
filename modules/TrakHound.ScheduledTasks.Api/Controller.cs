// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Api;

namespace TrakHound.ScheduledTasks.Api
{
    public class Controller : TrakHoundApiController
    {
        #region "Worker Status"

        /// <summary>
        /// Receives a Worker Status from the TrakHound.ScheduledTasks.Worker package
        /// </summary>
        [ApiPost("status", Description = "Receives a Status from a Scheduled Tasks Worker (TrakHound.ScheduledTasks.Worker posts its Status here; not meant for other clients).")]
        public TrakHoundApiResponse ReceiveWorkerStatus(
            [FromBody(ContentType = "application/json", Description = "The Worker's Status")] WorkerStatus status
            )
        {
            if (status == null) return BadRequest();

            WorkerStatusStore.Set(status);
            return Ok();
        }

        /// <summary>
        /// The latest Status of each Scheduled Tasks Worker : its Run Slots, Tasks (state and counts since it started), upcoming occurrences, and recent events
        /// </summary>
        [ApiGet("status", Description = "Gets the latest Status of each Scheduled Tasks Worker: its run slots, each Task's state and counts since the Worker started, upcoming Runs, and recent events. A Worker that hasn't reported for 30 seconds is marked stale (it may be stopped).")]
        public TrakHoundApiResponse GetWorkerStatus()
        {
            return Ok(WorkerStatusStore.GetAll());
        }

        [ApiGet("status/tasks/{taskId}", Description = "Gets a Scheduled Task's status as reported by its Worker: state, next Run, the last Run's result and duration, and counts since the Worker started.")]
        public TrakHoundApiResponse GetTaskStatus(
            [FromRoute(Description = "The Scheduled Task ID")] string taskId
            )
        {
            var task = WorkerStatusStore.GetAll().SelectMany(o => o.Tasks ?? new List<ScheduledTaskStatus>()).FirstOrDefault(o => o.Id == taskId);
            return task != null ? Ok(task) : NotFound();
        }

        /// <summary>
        /// The next occurrences across all Tasks, in time order
        /// </summary>
        [ApiGet("status/upcoming", Description = "Lists the next Runs of all Scheduled Tasks in time order (from Workers that are still reporting).")]
        public TrakHoundApiResponse GetUpcoming(
            [FromQuery(Description = "How many Runs to return (1 to 500)")] int count = 20
            )
        {
            var upcoming = WorkerStatusStore.GetAll()
                .Where(o => !o.Stale)
                .SelectMany(o => o.Upcoming ?? new List<UpcomingRun>())
                .OrderBy(o => o.Time)
                .Take(Math.Clamp(count, 1, 500));

            return Ok(upcoming);
        }

        /// <summary>
        /// Recent events (newest first) : due, started, completed, failed, skipped, dropped, missed, and cancelled
        /// </summary>
        [ApiGet("status/events", Description = "Lists recent Worker events, newest first: due, started, completed, failed, skipped, dropped, missed and cancelled.")]
        public TrakHoundApiResponse GetEvents(
            [FromQuery(Description = "Only this Scheduled Task's events")] string taskId = null,
            [FromQuery(Description = "Only events of this type: due, started, completed, failed, skipped, dropped, missed or cancelled")] string type = null,
            [FromQuery(Description = "The most events to return (1 to 1,000)")] int take = 100
            )
        {
            var events = WorkerStatusStore.GetAll()
                .SelectMany(o => o.Events ?? new List<StatusEvent>())
                .Where(o => string.IsNullOrEmpty(taskId) || o.TaskId == taskId)
                .Where(o => string.IsNullOrEmpty(type) || string.Equals(o.Type, type, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.Time)
                .Take(Math.Clamp(take, 1, 1000));

            return Ok(events);
        }

        #endregion

        #region "Configuration List Cache"

        // The list reads every Task File, so it is cached : cleared when this Api writes or deletes a Task, and expired so edits made elsewhere show up
        private static readonly TimeSpan _listCacheExpiration = TimeSpan.FromSeconds(5);
        private readonly object _listCacheLock = new object();
        private CachedConfigurationList _listCache;
        private long _listCacheVersion;

        private class CachedConfigurationList
        {
            public List<ScheduledTaskConfiguration> Configurations; // Null when there are no Task Files
            public DateTime Created;
        }

        private void ClearListCache()
        {
            lock (_listCacheLock)
            {
                _listCache = null;
                _listCacheVersion++;
            }
        }

        private async Task<List<ScheduledTaskConfiguration>> GetListConfigurations()
        {
            long version;
            lock (_listCacheLock)
            {
                if (_listCache != null && DateTime.UtcNow - _listCache.Created < _listCacheExpiration) return _listCache.Configurations;
                version = _listCacheVersion;
            }

            var created = DateTime.UtcNow;
            List<ScheduledTaskConfiguration> configurations = null;

            var configurationFilePaths = await Volume.ListFiles("/");
            if (!configurationFilePaths.IsNullOrEmpty())
            {
                configurations = new List<ScheduledTaskConfiguration>();

                foreach (var configurationFilePath in configurationFilePaths)
                {
                    var configuration = await Volume.ReadJson<ScheduledTaskConfiguration>(configurationFilePath);
                    if (configuration != null)
                    {
                        configurations.Add(configuration);
                    }
                }
            }

            lock (_listCacheLock)
            {
                // Don't cache a list read while a Task was written or deleted (it may miss the change)
                if (version == _listCacheVersion) _listCache = new CachedConfigurationList { Configurations = configurations, Created = created };
            }

            return configurations;
        }

        #endregion

        [ApiGet(Description = "Lists the Scheduled Tasks (their configurations). Returns 404 when there are none.")]
        public async Task<TrakHoundApiResponse> ListConfigurations()
        {
            var configurations = await GetListConfigurations();
            if (configurations != null)
            {
                return Ok(configurations);
            }
            else
            {
                return NotFound();
            }
        }

        #region "Export / Import"

        /// <summary>
        /// The Scheduled Tasks (all, the ones in ids, or the ones for functionId : comma separated) as a Document that can be imported on another Instance
        /// </summary>
        [ApiGet("export", Description = "Exports the Scheduled Tasks (all, or the ones in ids or for functionId) as a document that import can import on another Instance. To include the Jobs the Tasks use, export from the Functions Api (functions/schedules/export) instead.")]
        public async Task<TrakHoundApiResponse> Export(
            [FromQuery(Description = "Comma separated Scheduled Task IDs to export")] string ids = null,
            [FromQuery(Description = "Comma separated Function IDs: export their Scheduled Tasks")] string functionId = null
            )
        {
            var idFilter = SplitList(ids);
            var functionFilter = SplitList(functionId);

            var tasks = (await ReadConfigurations())
                .Select(o => { var task = ScheduledTaskImport.Normalize(o.Value); task.Id ??= o.Key; return task; })
                .Where(o => idFilter == null || idFilter.Contains(o.Id))
                .Where(o => functionFilter == null || (o.FunctionId != null && functionFilter.Contains(o.FunctionId)))
                .OrderBy(o => o.FunctionId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var document = new ScheduledTaskExport();
            document.Format = ScheduledTaskExport.FormatName;
            document.Version = ScheduledTaskExport.CurrentVersion;
            document.Exported = DateTime.UtcNow;
            document.SourceInstanceId = InstanceId;
            document.Tasks = tasks;

            return Ok(document);
        }

        /// <summary>
        /// Imports the Tasks of an Export Document. Every Task is validated first, and nothing is written when one is invalid or (mode=fail, the default)
        /// a different Task with the same ID exists. mode=skip keeps existing Tasks, mode=overwrite replaces them. dryRun=true only returns what would happen.
        /// The Scheduled Tasks Worker picks up the written Tasks
        /// </summary>
        [ApiPost("import", Description = "Imports the Scheduled Tasks of an export document. Every Task is validated first, and nothing is written if one is invalid. When a different Task with the same ID exists: mode=fail (the default) refuses, mode=skip keeps it, and mode=overwrite replaces it. dryRun=true only reports what would happen. The Scheduled Tasks Worker picks up the written Tasks.")]
        public async Task<TrakHoundApiResponse> Import(
            [FromBody(ContentType = "application/json", Description = "An export document from export")] ScheduledTaskExport document,
            [FromQuery(Description = "When a different Task with the same ID exists: fail (the default) refuses the import, skip keeps it, overwrite replaces it")] string mode = null,
            [FromQuery(Description = "Only report what would happen, without writing anything")] bool dryRun = false
            )
        {
            if (!ScheduledTaskImport.TryParseMode(mode, out var importMode)) return BadRequest($"Invalid Mode : {mode} (fail, skip, or overwrite)");

            var documentError = ScheduledTaskImport.ValidateDocument(document);
            if (documentError != null) return BadRequest(documentError);

            var existing = await ReadConfigurations();
            var result = ScheduledTaskImport.Plan(document.Tasks, existing, importMode, dryRun);
            if (!result.Success) return BadRequest(result);
            if (dryRun) return Ok(result);

            foreach (var item in result.Tasks.Where(o => o.Configuration != null))
            {
                if (!await Volume.WriteJson($"{item.FileId}.json", item.Configuration))
                {
                    item.Action = ScheduledTaskImportAction.Error;
                    item.Messages ??= new List<string>();
                    item.Messages.Add("Error writing the Task to the Volume");
                }
            }
            ClearListCache();

            ScheduledTaskImport.UpdateCounts(result);
            return result.Success ? Ok(result) : InternalError(result);
        }

        /// <summary>
        /// The Tasks on this Instance by their File ID
        /// </summary>
        private async Task<Dictionary<string, ScheduledTaskConfiguration>> ReadConfigurations()
        {
            var configurations = new Dictionary<string, ScheduledTaskConfiguration>(StringComparer.OrdinalIgnoreCase);

            var configurationFilePaths = await Volume.ListFiles("/");
            if (!configurationFilePaths.IsNullOrEmpty())
            {
                foreach (var configurationFilePath in configurationFilePaths)
                {
                    var fileId = Path.GetFileNameWithoutExtension(configurationFilePath);
                    if (string.IsNullOrEmpty(fileId)) continue;

                    var configuration = await Volume.ReadJson<ScheduledTaskConfiguration>(configurationFilePath);
                    if (configuration != null) configurations[fileId] = configuration;
                }
            }

            return configurations;
        }

        private static HashSet<string> SplitList(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return new HashSet<string>(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        [ApiGet("{configurationId}", Description = "Gets a Scheduled Task's configuration: its Function, Job, schedule, time zone, and whether it is enabled.")]
        public async Task<TrakHoundApiResponse> GetConfiguration(
            [FromRoute(Description = "The Scheduled Task ID")] string configurationId
            )
        {
            if (!string.IsNullOrEmpty(configurationId))
            {
                if (!ConfigurationId.IsSafeFileName(configurationId)) return BadRequest($"Invalid Task ID : {configurationId}");

                var configurationFilePath = $"{configurationId}.json";
                var configuration = await Volume.ReadJson<ScheduledTaskConfiguration>(configurationFilePath);
                if (configuration != null)
                {
                    return Ok(configuration);
                }
                else
                {
                    return NotFound();
                }
            }
            else
            {
                return BadRequest();
            }
        }

        [ApiPost(Description = "Creates or replaces a Scheduled Task (by its ID, which also names its file). The Scheduled Tasks Worker picks up the change.")]
        public async Task<TrakHoundApiResponse> PublishConfiguration(
            [FromBody(Description = "The Scheduled Task: id (required), enabled, description, functionId, jobId, schedule (a cron expression with 5 fields, or 6 with seconds), timeZone (IANA or Windows ID) and runMissed")] ScheduledTaskConfiguration configuration
            )
        {
            if (configuration != null && !string.IsNullOrEmpty(configuration.Id))
            {
                if (!ConfigurationId.IsSafeFileName(configuration.Id)) return BadRequest($"Invalid Task ID : {configuration.Id}");

                var configurationFilePath = $"{configuration.Id}.json";

                var written = await Volume.WriteJson(configurationFilePath, configuration);
                ClearListCache();

                if (written)
                {
                    return Ok(configuration);
                }
                else
                {
                    return InternalError();
                }
            }
            else
            {
                return BadRequest();
            }
        }

		[ApiDelete("{configurationId}", Description = "Deletes a Scheduled Task. Returns 404 when it doesn't exist.")]
		public async Task<TrakHoundApiResponse> DeleteConfiguration(
			[FromRoute(Description = "The Scheduled Task ID")] string configurationId
			)
		{
			if (!string.IsNullOrEmpty(configurationId))
			{
				if (!ConfigurationId.IsSafeFileName(configurationId)) return BadRequest($"Invalid Task ID : {configurationId}");

				var configurationFilePath = $"{configurationId}.json";

				var deleted = await Volume.DeleteFile(configurationFilePath);
				ClearListCache();

				if (deleted)
				{
					return Ok();
				}
				else
				{
					return NotFound();
				}
			}
			else
			{
				return BadRequest();
			}
		}
	}
}
