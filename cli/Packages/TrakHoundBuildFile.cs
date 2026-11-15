// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.CLI.Packages
{
    /// <summary>
    /// A list of DotNet Package projects to Build (and optionally Publish) with a single command
    /// </summary>
    public class TrakHoundBuildFile
    {
        public const string Filename = "trakhound.build.json";


        /// <summary>
        /// Settings applied to every Project (unless the Project overrides them)
        /// </summary>
        [JsonPropertyName("defaults")]
        public TrakHoundBuildProject Defaults { get; set; }

        [JsonPropertyName("projects")]
        public List<TrakHoundBuildProject> Projects { get; set; }


        public static TrakHoundBuildFile Read(string path)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            return JsonSerializer.Deserialize<TrakHoundBuildFile>(File.ReadAllText(path), options);
        }
    }

    public class TrakHoundBuildProject
    {
        /// <summary>
        /// The path to the DotNet project directory (or .csproj file). Relative paths are resolved against the Build File directory
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }

        /// <summary>
        /// The path to the directory to write the Package file to. Relative paths are resolved against the Build File directory
        /// </summary>
        [JsonPropertyName("output")]
        public string Output { get; set; }

        [JsonPropertyName("projectConfiguration")]
        public string ProjectConfiguration { get; set; }

        [JsonPropertyName("includeDebugSymbols")]
        public bool? IncludeDebugSymbols { get; set; }

        [JsonPropertyName("includeSource")]
        public bool? IncludeSource { get; set; }

        [JsonPropertyName("publish")]
        public bool? Publish { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("server")]
        public string Server { get; set; }

        [JsonPropertyName("organization")]
        public string Organization { get; set; }


        /// <summary>
        /// Return a copy of this Project with any unset values taken from the Defaults
        /// </summary>
        public TrakHoundBuildProject WithDefaults(TrakHoundBuildProject defaults)
        {
            if (defaults == null) return this;

            return new TrakHoundBuildProject
            {
                Path = Path,
                Output = !string.IsNullOrEmpty(Output) ? Output : defaults.Output,
                ProjectConfiguration = !string.IsNullOrEmpty(ProjectConfiguration) ? ProjectConfiguration : defaults.ProjectConfiguration,
                IncludeDebugSymbols = IncludeDebugSymbols ?? defaults.IncludeDebugSymbols,
                IncludeSource = IncludeSource ?? defaults.IncludeSource,
                Publish = Publish ?? defaults.Publish,
                Version = !string.IsNullOrEmpty(Version) ? Version : defaults.Version,
                Server = !string.IsNullOrEmpty(Server) ? Server : defaults.Server,
                Organization = !string.IsNullOrEmpty(Organization) ? Organization : defaults.Organization
            };
        }
    }
}
