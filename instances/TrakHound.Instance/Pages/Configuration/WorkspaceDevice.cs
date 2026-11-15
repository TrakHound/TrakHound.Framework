// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Instance.Pages.Configuration
{
    /// <summary>
    /// A Device a Workspace is exported for : its own export is for its Device ID, with its Licenses and its own values for the Workspace's Variables
    /// </summary>
    public class WorkspaceDevice
    {
        /// <summary>
        /// A name for the Device (ex. "Line 3 Press"), used in the names of its deployment files
        /// </summary>
        [JsonPropertyName("label")]
        public string Label { get; set; }

        [JsonPropertyName("deviceId")]
        public string DeviceId { get; set; }

        /// <summary>
        /// Values that replace the Workspace's Variables on this Device (Variable name => value). Secret values are encrypted
        /// </summary>
        [JsonPropertyName("variables")]
        public Dictionary<string, string> Variables { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);


        public WorkspaceDevice Clone()
        {
            return new WorkspaceDevice
            {
                Label = Label,
                DeviceId = DeviceId,
                Variables = new Dictionary<string, string>(Variables ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            };
        }
    }
}
