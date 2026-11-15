// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TrakHound.Configurations;
using YamlDotNet.RepresentationModel;

namespace TrakHound.Deployments
{
    /// <summary>
    /// Removes secrets (passwords, tokens, keys) from Configuration files when a Deployment is created, and keeps the
    /// existing values on the target when it is installed (so a deployment without secrets doesn't blank them).
    /// </summary>
    public static class TrakHoundDeploymentSecrets
    {
        /// <summary>
        /// Written in place of a secret value. When installed, the target's existing value is kept.
        /// </summary>
        public const string Marker = "__TRAKHOUND_SECRET_NOT_DEPLOYED__";

        /// <summary>
        /// Folders (in a Profile's 'security' folder) whose files only contain secrets (excluded entirely) : the Admin Accounts and the Users
        /// (with their password hashes) and the API Keys
        /// </summary>
        public static readonly IReadOnlyList<string> SecretDirectories = new[] { "admins", "users", "api-keys" };


        /// <summary>
        /// Returns true if the YAML Key names a secret value (ex. a password, token, or key)
        /// </summary>
        public static bool IsSecretKey(string key) => TrakHoundSecrets.IsSecretKey(key);

        /// <summary>
        /// Returns true if the file only contains secrets and is excluded from Deployments (an Admin Accounts, Users or API Keys file in 'security', case-insensitive)
        /// </summary>
        public static bool IsSecretFile(string path)
        {
            var directory = Path.GetDirectoryName(path);
            var parent = Path.GetFileName(Path.GetDirectoryName(directory ?? ""));
            return string.Equals(parent, "security", StringComparison.OrdinalIgnoreCase) &&
                   SecretDirectories.Any(o => string.Equals(o, Path.GetFileName(directory), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns true if the file has a .yaml or .yml extension
        /// </summary>
        public static bool IsYamlFile(string path)
        {
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            return extension == ".yaml" || extension == ".yml";
        }


        /// <summary>
        /// Replace secret values in YAML with the Marker
        /// </summary>
        /// <returns>The YAML (unchanged if it has no secrets or can't be parsed) and the number of values replaced</returns>
        public static (string Yaml, int Count) Remove(string yaml)
        {
            if (string.IsNullOrWhiteSpace(yaml)) return (yaml, 0);

            try
            {
                var stream = new YamlStream();
                stream.Load(new StringReader(yaml));

                var count = 0;
                foreach (var document in stream.Documents)
                {
                    count += RemoveNode(document.RootNode);
                }

                if (count == 0) return (yaml, 0);

                using (var writer = new StringWriter())
                {
                    stream.Save(writer, false);
                    return (TrimDocumentEnd(writer.ToString()), count);
                }
            }
            catch
            {
                return (yaml, 0);
            }
        }

        /// <summary>
        /// Replace Markers in the deployed YAML with the values from the existing YAML (Markers without an existing value become empty)
        /// </summary>
        public static string Merge(string deployedYaml, string existingYaml)
        {
            if (string.IsNullOrEmpty(deployedYaml) || !deployedYaml.Contains(Marker)) return deployedYaml;

            try
            {
                var deployed = new YamlStream();
                deployed.Load(new StringReader(deployedYaml));

                YamlNode existingRoot = null;
                if (!string.IsNullOrWhiteSpace(existingYaml))
                {
                    try
                    {
                        var existing = new YamlStream();
                        existing.Load(new StringReader(existingYaml));
                        existingRoot = existing.Documents.FirstOrDefault()?.RootNode;
                    }
                    catch { }
                }

                foreach (var document in deployed.Documents)
                {
                    MergeNode(document.RootNode, existingRoot);
                }

                using (var writer = new StringWriter())
                {
                    deployed.Save(writer, false);
                    return TrimDocumentEnd(writer.ToString());
                }
            }
            catch
            {
                // Don't write Markers as values
                return deployedYaml.Replace(Marker, "");
            }
        }


        private static int RemoveNode(YamlNode node)
        {
            var count = 0;

            if (node is YamlMappingNode mapping)
            {
                foreach (var entry in mapping.Children.ToList())
                {
                    var key = (entry.Key as YamlScalarNode)?.Value;
                    // A reference to a Configuration Variable (ex. "${dbPassword}") isn't a secret
                    if (entry.Value is YamlScalarNode scalar && !string.IsNullOrEmpty(scalar.Value) && (IsSecretKey(key) || TrakHoundSecrets.IsEncrypted(scalar.Value)) && !TrakHoundConfigurationVariables.IsReference(scalar.Value))
                    {
                        mapping.Children[entry.Key] = new YamlScalarNode(Marker);
                        count++;
                    }
                    else
                    {
                        count += RemoveNode(entry.Value);
                    }
                }
            }
            else if (node is YamlSequenceNode sequence)
            {
                foreach (var child in sequence.Children)
                {
                    count += RemoveNode(child);
                }
            }

            return count;
        }

        private static void MergeNode(YamlNode node, YamlNode existing)
        {
            if (node is YamlMappingNode mapping)
            {
                var existingMapping = existing as YamlMappingNode;

                foreach (var entry in mapping.Children.ToList())
                {
                    var existingValue = GetChild(existingMapping, (entry.Key as YamlScalarNode)?.Value);

                    if (entry.Value is YamlScalarNode scalar && scalar.Value == Marker)
                    {
                        mapping.Children[entry.Key] = existingValue is YamlScalarNode existingScalar
                            ? new YamlScalarNode(existingScalar.Value)
                            : new YamlScalarNode("");
                    }
                    else
                    {
                        MergeNode(entry.Value, existingValue);
                    }
                }
            }
            else if (node is YamlSequenceNode sequence)
            {
                var existingSequence = existing as YamlSequenceNode;

                for (var i = 0; i < sequence.Children.Count; i++)
                {
                    // Match list items by "id" (ex. Package Sources), otherwise by position
                    var child = sequence.Children[i];
                    var id = GetChild(child as YamlMappingNode, "id") as YamlScalarNode;

                    YamlNode existingChild = null;
                    if (existingSequence != null)
                    {
                        existingChild = id != null
                            ? existingSequence.Children.FirstOrDefault(o => (GetChild(o as YamlMappingNode, "id") as YamlScalarNode)?.Value == id.Value)
                            : (i < existingSequence.Children.Count ? existingSequence.Children[i] : null);
                    }

                    MergeNode(child, existingChild);
                }
            }
        }

        private static YamlNode GetChild(YamlMappingNode mapping, string key)
        {
            if (mapping == null || key == null) return null;
            return mapping.Children.FirstOrDefault(o => (o.Key as YamlScalarNode)?.Value == key).Value;
        }

        private static string TrimDocumentEnd(string yaml)
        {
            // YamlStream.Save writes a "..." document end marker
            var trimmed = yaml.TrimEnd();
            if (trimmed.EndsWith("...")) trimmed = trimmed.Substring(0, trimmed.Length - 3).TrimEnd();
            return trimmed + Environment.NewLine;
        }
    }
}
