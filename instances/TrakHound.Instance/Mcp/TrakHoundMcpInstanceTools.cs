// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using TrakHound.Instance.Boots;
using TrakHound.Instance.Security;
using TrakHound.Instances;

namespace TrakHound.Instance.Mcp
{
    [McpServerToolType]
    public class TrakHoundMcpInstanceTools : TrakHoundMcpTools
    {
        public TrakHoundMcpInstanceTools(ITrakHoundInstance instance) : base(instance) { }


        [McpServerTool(Name = "instance_boots", Title = "List Instance Boots", ReadOnly = true)]
        [Description("Lists the recent Boots of the Instance process (newest first): when each started, how long it took to be ready, the Warnings and Errors logged while starting, and how it ended (Stopped, Failed, Crashed or Unclean). Use instance_boot for the steps and log entries of one Boot.")]
        [TrakHoundMcpTool(Resource = TrakHoundSystemSecurity.Instance)]
        public string Boots([Description("The most Boots to return (up to 100)")] int take = 10)
        {
            // Read from the Boot History directly, so it is available while the Runtime is Stopped or in Error
            var boots = TrakHoundInstanceLifetimeMonitor.BootHistory?.GetBoots() ?? Enumerable.Empty<TrakHoundInstanceBoot>();

            return ToJson(boots.Take(Clamp(take, 1, TrakHoundBootHistory.DefaultMaxBoots)).Select(o => new
            {
                id = o.Id,
                status = o.Status.ToString(),
                processId = o.ProcessId,
                version = o.Version,
                started = o.Started.ToString("o"),
                bootSeconds = o.BootDuration != null ? Math.Round(o.BootDuration.Value.TotalSeconds, 2) : (double?)null,
                ended = o.Ended?.ToString("o"),
                reason = o.Reason,
                bootErrors = o.Log?.ErrorCount ?? 0,
                bootWarnings = o.Log?.WarningCount ?? 0,
                runtimeStarts = o.Starts?.Count ?? 0,
                failedRuntimeStarts = o.Starts?.Count(s => !s.Success) ?? 0
            }));
        }

        [McpServerTool(Name = "instance_boot", Title = "Get Instance Boot", ReadOnly = true)]
        [Description("Gets one Boot of the Instance process: each startup step with its duration (and the step that failed), each Runtime Start within it, the Warning and Error log entries, and the Exception if it failed or crashed.")]
        [TrakHoundMcpTool(Resource = TrakHoundSystemSecurity.Instance)]
        public string Boot([Description("The ID of the Boot (from instance_boots)")] string bootId)
        {
            var boot = TrakHoundInstanceLifetimeMonitor.BootHistory?.GetBoot(bootId);
            if (boot == null) throw new McpException($"Boot '{bootId}' was not found");

            return Truncate(ToJson(boot));
        }
    }
}
