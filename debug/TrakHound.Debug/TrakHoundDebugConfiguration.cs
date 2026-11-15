// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Debug
{
    /// <summary>
    /// The settings of a debug host, read from trakhound.debug.json in the build output
    /// </summary>
    public class TrakHoundDebugConfiguration
    {
        public const string Filename = "trakhound.debug.json";
        public const string DefaultInstanceUrl = "http://localhost:8472";
        public const string DefaultConfigurationId = "debug";

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };


        /// <summary>
        /// The Instance the Client connects to
        /// </summary>
        [JsonPropertyName("instanceUrl")]
        public string InstanceUrl { get; set; } = DefaultInstanceUrl;

        /// <summary>
        /// An API Key for the Instance (sent in the X-API-Key header)
        /// </summary>
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; }

        /// <summary>
        /// A Bearer Token for the Instance (sent in the Authorization header)
        /// </summary>
        [JsonPropertyName("bearerToken")]
        public string BearerToken { get; set; }

        /// <summary>
        /// The ID of the module's Configuration (on an Instance, each Configuration has its own ID)
        /// </summary>
        [JsonPropertyName("configurationId")]
        public string ConfigurationId { get; set; } = DefaultConfigurationId;

        /// <summary>
        /// The folder used as the module's Volume. A relative path is relative to the build output. Defaults to "volume"
        /// </summary>
        [JsonPropertyName("volumePath")]
        public string VolumePath { get; set; }

        /// <summary>
        /// The Configuration Parameters (as set in the module's Configuration on an Instance)
        /// </summary>
        [JsonPropertyName("parameters")]
        public Dictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// The input Parameters a Function is run with (used when none are passed as an argument)
        /// </summary>
        [JsonPropertyName("input")]
        public Dictionary<string, object> Input { get; set; }

        /// <summary>
        /// Apps only: the Theme shown until one is picked ("light" or "dark")
        /// </summary>
        [JsonPropertyName("theme")]
        public string Theme { get; set; }


        /// <summary>
        /// Reads the configuration file (trakhound.debug.json in the build output by default). Returns the defaults if the file doesn't exist
        /// </summary>
        public static TrakHoundDebugConfiguration Read(string path = null)
        {
            if (string.IsNullOrEmpty(path)) path = Path.Combine(AppContext.BaseDirectory, Filename);

            if (!File.Exists(path))
            {
                Console.WriteLine($"Debug Configuration Not Found : {path} : Using Defaults");
                return new TrakHoundDebugConfiguration();
            }

            TrakHoundDebugConfiguration configuration;
            try
            {
                configuration = JsonSerializer.Deserialize<TrakHoundDebugConfiguration>(File.ReadAllText(path), _jsonOptions) ?? new TrakHoundDebugConfiguration();
            }
            catch (JsonException ex)
            {
                // Stop here: running with the defaults would hide the mistake
                // The build copies the file from the project folder, so point there instead of at the copy
                throw new InvalidOperationException($"Invalid Debug Configuration : {ex.Message} (fix {Filename} in the project folder; the build copies it to {path})", ex);
            }

            // JSON values are read as JsonElements. Convert them to the types the Instance reads from a Configuration file
            configuration.Parameters = ConvertValues(configuration.Parameters);
            configuration.Input = ConvertValues(configuration.Input);
            return configuration;
        }

        /// <summary>
        /// Returns the Function input Parameters as strings (the type Functions receive)
        /// </summary>
        public Dictionary<string, string> GetInput()
        {
            var input = new Dictionary<string, string>();
            if (Input != null)
            {
                foreach (var entry in Input) input[entry.Key] = entry.Value?.ToString();
            }
            return input;
        }


        private static Dictionary<string, object> ConvertValues(Dictionary<string, object> values)
        {
            var converted = new Dictionary<string, object>();
            if (values != null)
            {
                foreach (var entry in values)
                {
                    converted[entry.Key] = entry.Value is JsonElement element ? ConvertValue(element) : entry.Value;
                }
            }
            return converted;
        }

        private static object ConvertValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Number: return element.TryGetInt64(out var integer) ? integer : element.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return null;

                // Objects and Arrays are kept as JSON text
                default: return element.GetRawText();
            }
        }
    }
}
