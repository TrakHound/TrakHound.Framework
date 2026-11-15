// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Hosting
{
    /// <summary>
    /// The settings of a standalone host, read from the "TrakHound" section of the application's configuration
    /// (ex. appsettings.json, or environment variables such as TrakHound__InstanceUrl). The names match trakhound.debug.json
    /// </summary>
    public class TrakHoundHostOptions
    {
        public const string SectionName = "TrakHound";
        public const string DefaultConfigurationId = "default";
        public const string DefaultVolumePath = "volume";


        /// <summary>
        /// The Instance the Client connects to. Without one, the module runs on its own and Client calls fail
        /// </summary>
        public string InstanceUrl { get; set; }

        /// <summary>
        /// An API Key for the Instance (sent in the X-API-Key header)
        /// </summary>
        public string ApiKey { get; set; }

        /// <summary>
        /// A Bearer Token for the Instance (sent in the Authorization header)
        /// </summary>
        public string BearerToken { get; set; }

        /// <summary>
        /// The ID of the module's Configuration (on an Instance, each Configuration has its own ID)
        /// </summary>
        public string ConfigurationId { get; set; } = DefaultConfigurationId;

        /// <summary>
        /// The folder used as the module's Volume. A relative path is relative to the application's folder. Defaults to "volume"
        /// </summary>
        public string VolumePath { get; set; }

        /// <summary>
        /// The folder the module's Variables are kept in. A relative path is relative to the application's folder. Defaults to the application's folder
        /// </summary>
        public string VariablesPath { get; set; }

        /// <summary>
        /// The Configuration Parameters (as set in the module's Configuration on an Instance). Values are strings: GetParameter&lt;T&gt; converts them
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Functions only: the input Parameters a Function is run with by RunTrakHoundFunction (ex. --TrakHound:Input:name=value on the command line)
        /// </summary>
        public Dictionary<string, string> Input { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// The API Keys accepted by AddTrakHoundApiKeyAuthentication (callers of this host, not the Instance's keys)
        /// </summary>
        public List<TrakHoundHostApiKey> ApiKeys { get; set; } = new List<TrakHoundHostApiKey>();
    }

    /// <summary>
    /// An API Key a caller of the host can send (in the X-API-Key header, or as "Authorization: Bearer {key}")
    /// </summary>
    public class TrakHoundHostApiKey
    {
        /// <summary>
        /// Who the Key is for (the signed in user's name)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The SHA-256 hash (hex) of the Key, so the Key itself isn't kept in the settings
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// The Key itself, when it comes from a secret store (ex. an environment variable or Key Vault). Use Hash in files
        /// </summary>
        public string Key { get; set; }
    }
}
