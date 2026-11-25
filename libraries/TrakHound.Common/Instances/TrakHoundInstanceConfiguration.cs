// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using TrakHound.Packages;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Instances
{
    public class TrakHoundInstanceConfiguration
    {
        private const string BackupDirectoryName = "backup";
        public const string Filename = "instance.config.yaml";
        public const int DefaultPackageUpdateCheckInterval = 6; // Hours
        public const int DefaultPackageInventoryReportInterval = 6; // Hours
        public const int DefaultMaxRequestBodySizeMb = 256;


        /// <summary>
        /// The ID of the Instance. It isn't one of the settings (it would be copied to every device a deployment is installed on) : the Instance keeps it
        /// in its own file (see <see cref="TrakHoundInstanceIdentity"/>). Files saved before that have it here, and it is read from them once
        /// </summary>
        [JsonPropertyName("instanceId")]
        [YamlIgnore]
        public string InstanceId { get; set; }

        [JsonPropertyName("instanceKey")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string InstanceKey { get; set; }

        [JsonPropertyName("name")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string Description { get; set; }


        [JsonPropertyName("httpAddress")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string HttpAddress { get; set; }

        [JsonPropertyName("httpPort")]
        public int HttpPort { get; set; }

        /// <summary>
        /// The largest Request body (ex. an upload to an Api or a Blob) in megabytes. 0 = no limit. Applied when saved
        /// </summary>
        [JsonPropertyName("maxRequestBodySizeMb")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int MaxRequestBodySizeMb { get; set; }


        /// <summary>
        /// The IP Address the Admin Port listens on (ex. 127.0.0.1). Empty uses the HTTP Address
        /// </summary>
        [JsonPropertyName("adminHttpAddress")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string AdminHttpAddress { get; set; }

        /// <summary>
        /// Serve the Admin Interface and the System API on a separate TCP Port (0 = served on the HTTP Port). Applied when the Instance is restarted
        /// </summary>
        [JsonPropertyName("adminHttpPort")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public int AdminHttpPort { get; set; }


        /// <summary>
        /// The IP Addresses or Networks (ex. 127.0.0.1, 10.0.0.0/8) of the reverse proxies whose X-Forwarded-For and X-Forwarded-Proto headers
        /// give the client's address and scheme. Empty ignores the headers. Applied when Saved
        /// </summary>
        [JsonPropertyName("trustedProxies")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public List<string> TrustedProxies { get; set; }

        /// <summary>
        /// The client IP Addresses or Networks (ex. 10.0.0.25, 192.168.1.0/24) the Admin Interface is served to. Empty allows every address.
        /// Loopback (this computer) is always allowed. Applied when Saved
        /// </summary>
        [JsonPropertyName("adminAllowedAddresses")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public List<string> AdminAllowedAddresses { get; set; }


        [JsonPropertyName("adminInterfaceEnabled")]
        public bool AdminInterfaceEnabled { get; set; }

        [JsonPropertyName("adminAuthenticationEnabled")]
        public bool AdminAuthenticationEnabled { get; set; }

        [JsonPropertyName("adminDefaultTheme")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string AdminDefaultTheme { get; set; }

        [JsonPropertyName("systemApiEnabled")]
        public bool SystemApiEnabled { get; set; }

        /// <summary>
        /// Allow requests from other machines to System Resources (and Service operations) that no Identity Profile covers. When false
        /// (the default), an unprotected System Resource can only be used from the same machine as the Instance
        /// </summary>
        [JsonPropertyName("systemApiUnprotectedRemoteAccess")]
        public bool SystemApiUnprotectedRemoteAccess { get; set; }

        /// <summary>
        /// Serve the MCP (Model Context Protocol) Server at /_mcp so AI clients can use the Instance's tools. Requires the System API.
        /// Served on the HTTP port (and the Admin Port) and authorized by Identity Profiles like the System API
        /// </summary>
        [JsonPropertyName("mcpEnabled")]
        public bool McpEnabled { get; set; }

        /// <summary>
        /// Serve the configured APIs over HTTP. When disabled, APIs are still available within the Instance (ex. to Functions, Workers and Apps).
        /// </summary>
        [JsonPropertyName("apiHttpEnabled")]
        public bool ApiHttpEnabled { get; set; }

        /// <summary>
        /// Serve the configured Apps over HTTP (ex. an Instance that only runs Functions and Workers)
        /// </summary>
        [JsonPropertyName("appsHttpEnabled")]
        public bool AppsHttpEnabled { get; set; }

        /// <summary>
        /// Redirect browsers to the Initialization Page (/_init) while the Instance is not Started. When disabled, requests get a 503 with Retry-After.
        /// </summary>
        [JsonPropertyName("initializationPageEnabled")]
        public bool InitializationPageEnabled { get; set; }

        /// <summary>
        /// The folder of a Custom Initialization Page (relative to the Configuration Profile's folder, or absolute) : its index.html is shown
        /// instead of the built-in page, and its other files are served at /_init/assets/... Empty uses the built-in page. Applied when saved.
        /// </summary>
        [JsonPropertyName("initializationPagePath")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string InitializationPagePath { get; set; }

        /// <summary>
        /// Show the Error Page (/_error) for unhandled errors and Admin pages that are not found. When disabled, a plain status response is returned instead.
        /// </summary>
        [JsonPropertyName("errorPageEnabled")]
        public bool ErrorPageEnabled { get; set; }


        /// <summary>
        /// How often (in Hours) the Package Sources are checked for Package Updates (0 = Disabled)
        /// </summary>
        [JsonPropertyName("packageUpdateCheckInterval")]
        public int PackageUpdateCheckInterval { get; set; }

        /// <summary>
        /// How often (in Hours) the installed Packages are reported to the TrakHound Package Manager Sources when nothing has changed (0 = Disabled). They are also reported when the Instance starts and when Packages change
        /// </summary>
        [JsonPropertyName("packageInventoryReportInterval")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int PackageInventoryReportInterval { get; set; }

        /// <summary>
        /// Whether Packages are checked against their Manifest and signature when installed or loaded, and whether a Package that fails is used. Applied when the Instance is restarted
        /// </summary>
        [JsonPropertyName("packageVerification")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundPackageVerificationMode PackageVerification { get; set; }


        [JsonPropertyName("basePath")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string BasePath { get; set; }

        [JsonPropertyName("routeRewrites")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public Dictionary<string, string> RouteRewrites { get; set; }


        /// <summary>
        /// Which Metrics are collected (applied when the Instance is restarted)
        /// </summary>
        [JsonPropertyName("metrics")]
        public TrakHoundInstanceMetricsConfiguration Metrics { get; set; }


        /// <summary>
        /// Which Health Check items are Critical or Ignored (applied on the next evaluation)
        /// </summary>
        [JsonPropertyName("health")]
        public TrakHoundInstanceHealthConfiguration Health { get; set; }


        /// <summary>
        /// How often each caller can call the Apis, the System API and the MCP Tools (applied when Saved)
        /// </summary>
        [JsonPropertyName("rateLimits")]
        public TrakHoundInstanceRateLimitConfiguration RateLimits { get; set; }


        [JsonPropertyName("directories")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public TrakHoundInstanceDirectoryConfiguration Directories { get; set; }


        [JsonIgnore]
        [YamlIgnore]
        public string Path { get; set; }

        [JsonIgnore]
        [YamlIgnore]
        public string ChangeToken { get; set; }

        /// <summary>
        /// The Instance ID found in the file (files saved before it moved to its own file). Null if the file doesn't have one
        /// </summary>
        [JsonIgnore]
        [YamlIgnore]
        public string FileInstanceId { get; set; }

        /// <summary>
        /// The settings that are Variable references in the file (the YAML keys joined by '.' => the reference, ex. "httpPort" => "${port}").
        /// They are written back while their value is unchanged, so a value changed elsewhere (ex. "th instance set") replaces the reference
        /// </summary>
        [JsonIgnore]
        [YamlIgnore]
        public IReadOnlyDictionary<string, string> References => _references;

        /// <summary>
        /// The references that couldn't be resolved when the file was read (those settings use their defaults)
        /// </summary>
        [JsonIgnore]
        [YamlIgnore]
        public List<string> UnresolvedReferences { get; } = new List<string>();

        private readonly Dictionary<string, string> _references = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _referenceValues = new Dictionary<string, string>(StringComparer.Ordinal); // The value each reference had (null if not written)


        public TrakHoundInstanceConfiguration()
        {
            InstanceId = $"i-{StringFunctions.RandomString(10)}";
            HttpPort = 8472;
            MaxRequestBodySizeMb = DefaultMaxRequestBodySizeMb;
            AdminInterfaceEnabled = true;
            AdminAuthenticationEnabled = true;
            AdminDefaultTheme = "Dark";
            SystemApiEnabled = true;
            ApiHttpEnabled = true;
            AppsHttpEnabled = true;
            InitializationPageEnabled = true;
            ErrorPageEnabled = true;
            PackageUpdateCheckInterval = DefaultPackageUpdateCheckInterval;
            PackageInventoryReportInterval = DefaultPackageInventoryReportInterval;
            PackageVerification = TrakHoundPackageVerificationMode.Warn;
            Metrics = new TrakHoundInstanceMetricsConfiguration();
            Health = new TrakHoundInstanceHealthConfiguration();
            RateLimits = new TrakHoundInstanceRateLimitConfiguration();
            Directories = new TrakHoundInstanceDirectoryConfiguration();
        }


        /// <param name="path">The file (instance.config.yaml in the application directory when null)</param>
        /// <param name="resolveReference">Resolves a value with Variable references (ex. "${port}"). Returns null if it can't be. Without it, those settings use their defaults</param>
        public static TrakHoundInstanceConfiguration Read(string path = null, Func<string, string> resolveReference = null)
        {
            var configurationPath = path;
            if (string.IsNullOrEmpty(configurationPath)) configurationPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Filename);

            if (!string.IsNullOrEmpty(configurationPath))
            {
                try
                {
                    var text = File.ReadAllText(configurationPath);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var configuration = Parse(text, resolveReference);
                        configuration.Path = configurationPath;
                        return configuration;
                    }
                }
                catch { }
            }

            return null;
        }

        private static TrakHoundInstanceConfiguration Parse(string text, Func<string, string> resolveReference)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            var root = stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode;
            if (root == null) return deserializer.Deserialize<TrakHoundInstanceConfiguration>(text);

            var fileInstanceId = (GetNode(root, "instanceId") as YamlScalarNode)?.Value;

            // Settings with Variable references are replaced with their values (or removed, so they use their defaults)
            var references = new List<(YamlMappingNode Parent, YamlNode Key, string Path, string Reference)>();
            FindReferences(root, null, references);

            var unresolved = new List<string>();
            foreach (var reference in references)
            {
                var value = resolveReference?.Invoke(reference.Reference);
                if (value != null) ((YamlScalarNode)reference.Parent.Children[reference.Key]).Value = value;
                else
                {
                    reference.Parent.Children.Remove(reference.Key);
                    unresolved.Add($"{reference.Path} : {reference.Reference}");
                }
            }

            TrakHoundInstanceConfiguration configuration;
            try
            {
                configuration = deserializer.Deserialize<TrakHoundInstanceConfiguration>(ToText(stream));
            }
            catch (YamlException) when (references.Count > 0)
            {
                // A resolved value isn't valid for its setting (ex. a port that isn't a number) : every referenced setting uses its default
                foreach (var reference in references.Where(o => o.Parent.Children.ContainsKey(o.Key)))
                {
                    reference.Parent.Children.Remove(reference.Key);
                    unresolved.Add($"{reference.Path} : {reference.Reference} (not a valid value)");
                }
                configuration = deserializer.Deserialize<TrakHoundInstanceConfiguration>(ToText(stream));
            }

            configuration.FileInstanceId = fileInstanceId;
            if (!string.IsNullOrEmpty(fileInstanceId)) configuration.InstanceId = fileInstanceId;
            configuration.UnresolvedReferences.AddRange(unresolved.Distinct());

            foreach (var reference in references) configuration.SetReference(reference.Path, reference.Reference);
            return configuration;
        }

        /// <summary>
        /// Make the setting a Variable reference (ex. "httpPort", "${port}"). It is written while the setting keeps its current value
        /// </summary>
        public void SetReference(string path, string reference)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(reference)) return;

            _references[path] = reference;
            _referenceValues[path] = (GetNode(SerializeToNode(), path) as YamlScalarNode)?.Value;
        }

        /// <summary>
        /// Write the setting's value instead of its Variable reference
        /// </summary>
        public void RemoveReference(string path)
        {
            if (path == null) return;

            _references.Remove(path);
            _referenceValues.Remove(path);
        }

        public void Save(string path = null, bool createBackup = true)
        {
            try
            {
                var configurationPath = path;
                if (configurationPath.IsNullOrEmpty()) configurationPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Filename);

                var configurationDirectory = System.IO.Path.GetDirectoryName(configurationPath);
                if (!Directory.Exists(configurationDirectory)) Directory.CreateDirectory(configurationDirectory);

                if (createBackup)
                {
                    // Create Backup of Configuration File
                    var backupDir = TrakHoundDataDirectory.GetPath(BackupDirectoryName);
                    if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
                    var backupFilename = System.IO.Path.ChangeExtension(UnixDateTime.Now.ToString(), ".backup.yaml");
                    var backupPath = System.IO.Path.Combine(backupDir, backupFilename);
                    if (File.Exists(configurationPath))
                    {
                        File.Copy(configurationPath, backupPath);
                    }
                }

                // Update ChangeToken
                ChangeToken = Guid.NewGuid().ToString();


                File.WriteAllText(configurationPath, ToYaml());
            }
            catch { }
        }

        /// <summary>
        /// The settings as YAML, with the Variable references of the settings that kept their values
        /// </summary>
        public string ToYaml()
        {
            var yaml = CreateSerializer().Serialize(this);
            if (_references.Count == 0) return yaml;

            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            var root = stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode;
            if (root == null) return yaml;

            foreach (var reference in _references)
            {
                var current = (GetNode(root, reference.Key) as YamlScalarNode)?.Value;
                if (current == _referenceValues.GetValueOrDefault(reference.Key)) SetNode(root, reference.Key, reference.Value);
            }

            return ToText(stream);
        }


        private static ISerializer CreateSerializer() => new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();

        private YamlMappingNode SerializeToNode()
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(CreateSerializer().Serialize(this)));
            return stream.Documents.FirstOrDefault()?.RootNode as YamlMappingNode;
        }

        private static void FindReferences(YamlMappingNode mapping, string path, List<(YamlMappingNode, YamlNode, string, string)> references)
        {
            foreach (var entry in mapping.Children)
            {
                if (entry.Key is not YamlScalarNode key) continue;

                var entryPath = path != null ? $"{path}.{key.Value}" : key.Value;
                if (entry.Value is YamlMappingNode child) FindReferences(child, entryPath, references);
                else if (entry.Value is YamlScalarNode scalar && TrakHound.Configurations.TrakHoundConfigurationVariables.HasReferences(scalar.Value)) references.Add((mapping, entry.Key, entryPath, scalar.Value));
            }
        }

        private static YamlNode GetNode(YamlMappingNode root, string path)
        {
            YamlNode node = root;
            foreach (var key in path.Split('.'))
            {
                if (node is not YamlMappingNode mapping || !mapping.Children.TryGetValue(new YamlScalarNode(key), out node)) return null;
            }
            return node;
        }

        private static void SetNode(YamlMappingNode root, string path, string value)
        {
            var keys = path.Split('.');
            var mapping = root;
            foreach (var key in keys.Take(keys.Length - 1))
            {
                if (!mapping.Children.TryGetValue(new YamlScalarNode(key), out var child) || child is not YamlMappingNode childMapping)
                {
                    childMapping = new YamlMappingNode();
                    mapping.Children[new YamlScalarNode(key)] = childMapping;
                }
                mapping = childMapping;
            }

            mapping.Children[new YamlScalarNode(keys.Last())] = new YamlScalarNode(value) { Style = ScalarStyle.DoubleQuoted };
        }

        private static string ToText(YamlStream stream)
        {
            using (var writer = new StringWriter())
            {
                stream.Save(writer, false);

                // YamlStream.Save writes a "..." document end marker
                var text = writer.ToString().TrimEnd();
                if (text.EndsWith("...")) text = text.Substring(0, text.Length - 3).TrimEnd();
                return text + Environment.NewLine;
            }
        }
    }
}
