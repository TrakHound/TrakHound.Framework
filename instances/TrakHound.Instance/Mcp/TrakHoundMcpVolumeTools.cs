// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using TrakHound.Instance.Security;
using TrakHound.Instances;

namespace TrakHound.Instance.Mcp
{
    [McpServerToolType]
    public class TrakHoundMcpVolumeTools : TrakHoundMcpTools
    {
        private const int MaxFiles = 1000;


        public TrakHoundMcpVolumeTools(ITrakHoundInstance instance) : base(instance) { }


        [McpServerTool(Name = "volumes_list_files", Title = "List Volume Files", ReadOnly = true)]
        [Description("Lists the files in a Volume (the storage used by a Package, ex. the Volume ID of an Api, App or Function).")]
        [TrakHoundMcpTool(Resource = TrakHoundSystemSecurity.Volumes)]
        public async Task<string> ListFiles(
            [Description("The Volume ID")] string volumeId,
            [Description("A directory within the Volume (empty for the root)")] string path = null,
            [Description("Include files in subdirectories")] bool includeSubdirectories = false)
        {
            var files = await GetClient().Volumes.ListFiles(volumeId, string.IsNullOrWhiteSpace(path) ? null : path, includeSubdirectories);
            var list = files?.Take(MaxFiles + 1).ToList() ?? new List<string>();
            return ToJson(new { files = list.Take(MaxFiles), truncated = list.Count > MaxFiles });
        }

        [McpServerTool(Name = "volumes_read_file", Title = "Read Volume File", ReadOnly = true)]
        [Description("Reads a text file from a Volume (ex. a configuration or a log). Binary files aren't returned.")]
        [TrakHoundMcpTool(Resource = TrakHoundSystemSecurity.Volumes)]
        public async Task<string> ReadFile(
            [Description("The Volume ID")] string volumeId,
            [Description("The file path within the Volume")] string path)
        {
            var bytes = await GetClient().Volumes.DownloadFile(volumeId, path);
            if (bytes == null) throw new McpException($"File '{path}' was not found in Volume '{volumeId}'");

            // A NUL in the first 8 KB means the file is binary
            if (bytes.AsSpan(0, Math.Min(bytes.Length, 8192)).IndexOf((byte)0) >= 0)
            {
                throw new McpException($"'{path}' is a binary file ({bytes.Length:N0} bytes) and can't be returned as text");
            }

            return Truncate(Encoding.UTF8.GetString(bytes));
        }
    }
}
