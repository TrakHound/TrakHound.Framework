// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Configurations
{
    /// <summary>
    /// Name/Value pairs shared by every Configuration in a Profile (stored in "variables.config.yaml" in the Profile directory).
    /// Parameter values reference them as ${name} and they are replaced when read using GetParameter().
    /// </summary>
    /// <remarks>
    /// <para>${env:NAME} references an Environment Variable of the Instance's process (also inside a Variable's value).
    /// $${name} is written as the literal text ${name}.</para>
    /// <para>Variables are read when the Instance Starts, so a change is applied when the Instance is Restarted.</para>
    /// <para>Variables named as secrets (ex. "dbPassword") are encrypted like secret Parameters.</para>
    /// </remarks>
    public static class TrakHoundConfigurationVariables
    {
        /// <summary>
        /// The filename (in the Profile directory)
        /// </summary>
        public const string Filename = "variables.config.yaml";

        /// <summary>
        /// The YAML key the Variables are listed under
        /// </summary>
        public const string VariablesKey = "variables";

        /// <summary>
        /// The prefix of a reference to an Environment Variable (ex. ${env:DATA_PATH})
        /// </summary>
        public const string EnvironmentPrefix = "env:";

        private const string _category = "variable";

        // $${...} (escaped) or ${name} / ${env:NAME}
        private static readonly Regex _referenceRegex = new Regex(@"\$\$\{|\$\{(env:)?([A-Za-z0-9_.\-]+)\}", RegexOptions.Compiled);
        private static readonly Regex _singleReferenceRegex = new Regex(@"^\$\{(env:)?[A-Za-z0-9_.\-]+\}$", RegexOptions.Compiled);

        private static readonly HashSet<string> _notFound = new HashSet<string>();
        private static readonly object _lock = new object();
        private static IReadOnlyDictionary<string, string> _variables = new Dictionary<string, string>();


        /// <summary>
        /// The Variables that are loaded (values are stored as in the file, so secrets are encrypted)
        /// </summary>
        public static IReadOnlyDictionary<string, string> Variables => _variables;

        /// <summary>
        /// Raised when a Parameter references a Variable that isn't defined (once per reference)
        /// </summary>
        public static event EventHandler<string> VariableNotFound;

        /// <summary>
        /// Raised when the Variables are loaded
        /// </summary>
        public static event EventHandler Loaded;


        /// <summary>
        /// Gets the path of the file for the Profile directory
        /// </summary>
        public static string GetPath(string profileDirectory)
        {
            return !string.IsNullOrEmpty(profileDirectory) ? Path.Combine(profileDirectory, Filename) : null;
        }

        /// <summary>
        /// Read the Variables from the Profile directory (none if the file doesn't exist). Plain text secrets are encrypted and the file is updated.
        /// </summary>
        /// <returns>The number of Variables loaded</returns>
        public static int Load(string profileDirectory)
        {
            var variables = Read(profileDirectory);

            lock (_lock)
            {
                _variables = variables;
                _notFound.Clear();
            }

            Loaded?.Invoke(null, EventArgs.Empty);
            return variables.Count;
        }

        /// <summary>
        /// Read the Variables from the Profile directory (without loading them). Plain text secrets are encrypted and the file is updated.
        /// </summary>
        public static Dictionary<string, string> Read(string profileDirectory)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            var path = GetPath(profileDirectory);
            if (path == null || !File.Exists(path)) return result;

            try
            {
                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return result;

                // Encrypt plain text secrets (ex. a password entered in the file) and update the file
                var (encryptedText, encryptedCount) = TrakHoundSecrets.EncryptYaml(text, VariablesKey);
                if (encryptedCount > 0)
                {
                    try
                    {
                        File.WriteAllText(path, encryptedText);
                        text = encryptedText;
                    }
                    catch { }
                }

                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                var file = deserializer.Deserialize<VariablesFile>(text);
                if (file?.Variables != null)
                {
                    foreach (var variable in file.Variables)
                    {
                        if (IsValidName(variable.Key)) result[variable.Key] = variable.Value ?? "";
                    }
                }
            }
            catch { }

            return result;
        }

        /// <summary>
        /// Save the Variables to the Profile directory (secrets are encrypted). They are applied when the Instance is Restarted.
        /// </summary>
        public static void Save(string profileDirectory, IDictionary<string, string> variables)
        {
            var path = GetPath(profileDirectory);
            if (path == null) return;

            var builder = new StringBuilder();
            builder.AppendLine("# Variables shared by the Configurations in this Profile. Reference them in a Parameter value as ${name}.");
            builder.AppendLine("# ${env:NAME} references an Environment Variable. Changes are applied when the Instance is Restarted.");
            builder.AppendLine($"{VariablesKey}:");

            if (variables != null)
            {
                foreach (var variable in variables.Where(o => IsValidName(o.Key)).OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase))
                {
                    var value = variable.Value ?? "";
                    if (TrakHoundSecrets.IsSecretKey(variable.Key) && !IsReference(value)) value = TrakHoundSecrets.Encrypt(value);

                    builder.AppendLine($"  {variable.Key}: {ToDoubleQuoted(value)}");
                }
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(path, builder.ToString());
        }


        /// <summary>
        /// Returns true if the name can be used for a Variable (letters, numbers, '_', '.', and '-')
        /// </summary>
        public static bool IsValidName(string name)
        {
            return !string.IsNullOrEmpty(name) && !name.StartsWith(EnvironmentPrefix, StringComparison.Ordinal) && name.All(o => char.IsLetterOrDigit(o) || o == '_' || o == '.' || o == '-');
        }

        /// <summary>
        /// Returns true if the whole value is a single reference (ex. "${dbPassword}"). A reference isn't a secret, so it isn't encrypted or removed from Deployments.
        /// </summary>
        public static bool IsReference(string value)
        {
            return value != null && _singleReferenceRegex.IsMatch(value);
        }

        /// <summary>
        /// Returns true if the value contains a reference (or an escaped reference)
        /// </summary>
        public static bool HasReferences(string value)
        {
            return value != null && value.Contains("${");
        }


        /// <summary>
        /// Returns the value with references replaced (references that aren't defined are left as they are). Used by GetParameter().
        /// </summary>
        public static object Expand(object value, string category, string configurationId, string name)
        {
            if (value is string text && HasReferences(text))
            {
                return Replace(text, true, reference => NotFound(reference, category, configurationId, name));
            }

            return value;
        }

        /// <summary>
        /// Gets the value of a Variable (decrypted, with Environment Variable references replaced). Returns null if it isn't defined.
        /// </summary>
        public static string GetValue(string name)
        {
            if (string.IsNullOrEmpty(name) || !_variables.TryGetValue(name, out var value)) return null;

            // A Variable can reference Environment Variables (ex. a path that is different on each host) but not other Variables
            var resolved = TrakHoundSecrets.Resolve(value, _category, name, name) as string;
            return resolved != null && HasReferences(resolved) ? Replace(resolved, false, reference => NotFound(reference, _category, name, name)) : resolved;
        }


        /// <summary>
        /// Replaces the references in the text : ${env:NAME} with Environment Variables, and ${name} with the Variables (the loaded Variables when null).
        /// Returns null if a reference isn't defined. Used for settings that aren't Parameters (ex. the Instance Settings)
        /// </summary>
        public static string Resolve(string text, IReadOnlyDictionary<string, string> variables = null)
        {
            if (!HasReferences(text)) return text;

            var found = true;
            var result = _referenceRegex.Replace(text, match =>
            {
                if (match.Value == "$${") return "${";

                var key = match.Groups[2].Value;
                string value;
                if (match.Groups[1].Success)
                {
                    value = Environment.GetEnvironmentVariable(key);
                }
                else if (variables != null)
                {
                    // Secret Variables are saved encrypted
                    value = variables.TryGetValue(key, out var saved) ? (TrakHoundSecrets.IsEncrypted(saved) ? TrakHoundSecrets.Decrypt(saved) : saved) : null;
                }
                else
                {
                    value = GetValue(key);
                }

                if (value == null) found = false;
                return value ?? match.Value;
            });

            return found ? result : null;
        }

        private static string Replace(string text, bool allowVariables, Action<string> notFound)
        {
            return _referenceRegex.Replace(text, match =>
            {
                if (match.Value == "$${") return "${";

                var isEnvironment = match.Groups[1].Success;
                var key = match.Groups[2].Value;
                string result = null;

                if (isEnvironment) result = Environment.GetEnvironmentVariable(key);
                else if (allowVariables) result = GetValue(key);

                if (result == null && (isEnvironment || allowVariables)) notFound(match.Value);
                return result ?? match.Value;
            });
        }

        private static void NotFound(string reference, string category, string configurationId, string name)
        {
            bool raise;
            var key = $"{category}:{configurationId}:{name}:{reference}";
            lock (_lock) raise = _notFound.Add(key);

            if (raise && VariableNotFound != null)
            {
                VariableNotFound(null, $"Configuration Variable not found : {category} : {configurationId} : {name} : {reference} is not defined in '{Filename}' (or the Environment) and was left as it is.");
            }
        }

        private static string ToDoubleQuoted(string value)
        {
            return "\"" + (value ?? "")
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t") + "\"";
        }


        private class VariablesFile
        {
            public Dictionary<string, string> Variables { get; set; }
        }
    }
}
