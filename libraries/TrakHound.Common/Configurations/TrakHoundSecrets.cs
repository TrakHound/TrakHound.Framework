// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace TrakHound.Configurations
{
    /// <summary>
    /// Encrypts secret Configuration Parameters (passwords, tokens, keys) so they aren't stored as plain text.
    /// Values are encrypted with a key generated for the Instance (stored in the Configuration directory, outside of the Profiles)
    /// and are decrypted when read using GetParameter().
    /// </summary>
    /// <remarks>
    /// This keeps secrets out of Configuration files (and anything they are copied to). It is not meant to protect against someone
    /// with access to the Instance's files, since the key is stored with them.
    /// </remarks>
    public static class TrakHoundSecrets
    {
        /// <summary>
        /// The prefix of an encrypted value
        /// </summary>
        public const string Prefix = "enc:v1:";

        /// <summary>
        /// The filename of the key (in the Configuration directory, the parent of the Profile directories)
        /// </summary>
        public const string KeyFilename = ".secretkey";

        private const string _parametersKey = "parameters";
        private const int _keySize = 32;
        private const int _nonceSize = 12;
        private const int _tagSize = 16;

        private static readonly HashSet<string> _failed = new HashSet<string>();
        private static readonly object _lock = new object();
        private static string _keyPath;
        private static byte[] _key;
        private static bool _encryptionFailed;


        /// <summary>
        /// Raised when a value can't be decrypted (once per value)
        /// </summary>
        public static event EventHandler<string> DecryptionFailed;

        /// <summary>
        /// Raised when a value can't be encrypted, so it is saved as plain text (ex. the key file can't be read or created).
        /// Raised once until a value is encrypted again
        /// </summary>
        public static event EventHandler<string> EncryptionFailed;


        /// <summary>
        /// Set the directory the key is stored in. The key is created when a value is first encrypted.
        /// </summary>
        public static void Initialize(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;

            var keyPath = Path.GetFullPath(Path.Combine(directory, KeyFilename));

            lock (_lock)
            {
                if (keyPath != _keyPath)
                {
                    _keyPath = keyPath;
                    _key = null;
                }
            }
        }


        /// <summary>
        /// Returns true if the Parameter name is for a secret (ex. "password", "apiToken", "client_secret", "apiKey").
        /// Names and IDs of secrets aren't secret (ex. "accessTokenName", "keyId").
        /// </summary>
        public static bool IsSecretKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            var lower = name.ToLowerInvariant();

            if (lower.EndsWith("name") || lower.EndsWith("id")) return false;

            return lower.Contains("password") || lower.Contains("token") || lower.Contains("secret") || lower.EndsWith("key");
        }

        /// <summary>
        /// Returns true if the value is encrypted (starts with the "enc:v1:" Prefix)
        /// </summary>
        public static bool IsEncrypted(string value)
        {
            return value != null && value.StartsWith(Prefix, StringComparison.Ordinal);
        }


        /// <summary>
        /// Encrypt a value (returns the value unchanged if it is empty, already encrypted, or the key is unavailable)
        /// </summary>
        public static string Encrypt(string value)
        {
            if (string.IsNullOrEmpty(value) || IsEncrypted(value)) return value;

            var key = GetKey(true);
            if (key == null)
            {
                OnEncryptionFailed($"A secret Configuration value was saved as plain text : the key ({_keyPath}) could not be read or created. Check that the Instance can read and write it, then save the value again.");
                return value;
            }

            try
            {
                var plainBytes = Encoding.UTF8.GetBytes(value);
                var bytes = new byte[_nonceSize + _tagSize + plainBytes.Length];
                var nonce = bytes.AsSpan(0, _nonceSize);
                var tag = bytes.AsSpan(_nonceSize, _tagSize);
                var cipherBytes = bytes.AsSpan(_nonceSize + _tagSize);

                RandomNumberGenerator.Fill(nonce);

                using (var aes = new AesGcm(key, _tagSize))
                {
                    aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
                }

                lock (_lock) _encryptionFailed = false;
                return Prefix + Convert.ToBase64String(bytes);
            }
            catch (Exception ex)
            {
                OnEncryptionFailed($"A secret Configuration value was saved as plain text : it could not be encrypted : {ex.Message}");
                return value;
            }
        }

        private static void OnEncryptionFailed(string message)
        {
            bool raise;
            lock (_lock)
            {
                raise = !_encryptionFailed;
                _encryptionFailed = true;
            }

            if (raise) EncryptionFailed?.Invoke(null, message);
        }

        /// <summary>
        /// Decrypt a value (returns the value unchanged if it isn't encrypted, or null if it can't be decrypted)
        /// </summary>
        public static string Decrypt(string value)
        {
            return TryDecrypt(value, out var result) ? result : null;
        }

        private static bool TryDecrypt(string value, out string result)
        {
            result = value;
            if (!IsEncrypted(value)) return true;

            result = null;

            var key = GetKey(false);
            if (key == null) return false;

            try
            {
                var bytes = Convert.FromBase64String(value.Substring(Prefix.Length));
                if (bytes.Length < _nonceSize + _tagSize) return false;

                var plainBytes = new byte[bytes.Length - _nonceSize - _tagSize];

                using (var aes = new AesGcm(key, _tagSize))
                {
                    aes.Decrypt(bytes.AsSpan(0, _nonceSize), bytes.AsSpan(_nonceSize + _tagSize), bytes.AsSpan(_nonceSize, _tagSize), plainBytes);
                }

                result = Encoding.UTF8.GetString(plainBytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns a Parameter value to use (decrypted if it is encrypted, then with Configuration Variable references replaced). Used by GetParameter().
        /// </summary>
        public static object Resolve(object value, string category, string configurationId, string name)
        {
            return TrakHoundConfigurationVariables.Expand(ResolveSecret(value, category, configurationId, name), category, configurationId, name);
        }

        /// <summary>
        /// Returns the value decrypted if it is encrypted (null if it can't be decrypted)
        /// </summary>
        internal static object ResolveSecret(object value, string category, string configurationId, string name)
        {
            if (value is string text && IsEncrypted(text))
            {
                if (TryDecrypt(text, out var result)) return result;

                bool raise;
                lock (_lock) raise = _failed.Add(text);

                if (raise && DecryptionFailed != null)
                {
                    DecryptionFailed(null, $"Configuration Parameter could not be decrypted : {category} : {configurationId} : {name} : It was encrypted by a different Instance (or the '{KeyFilename}' file was changed). Enter the value again.");
                }

                return null;
            }

            return value;
        }


        /// <summary>
        /// Encrypt the plain text secret Parameters
        /// </summary>
        /// <returns>The number of Parameters encrypted</returns>
        public static int EncryptParameters(IDictionary<string, object> parameters)
        {
            if (parameters == null || parameters.Count == 0) return 0;

            var count = 0;
            foreach (var parameter in parameters.ToList())
            {
                if (IsSecretKey(parameter.Key) && parameter.Value is string value && !string.IsNullOrEmpty(value) && !IsEncrypted(value) && !TrakHoundConfigurationVariables.IsReference(value))
                {
                    var encrypted = Encrypt(value);
                    if (encrypted != value)
                    {
                        parameters[parameter.Key] = encrypted;
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Encrypt the plain text secret Parameters in a Configuration file's YAML. Only the values are replaced, so the
        /// rest of the file (formatting, comments) is kept.
        /// </summary>
        /// <param name="yaml">The YAML text</param>
        /// <param name="sectionKey">The top level key of the values to encrypt (the Parameters of a Configuration by default)</param>
        /// <returns>The YAML (unchanged if it has no plain text secrets or can't be parsed) and the number of values encrypted</returns>
        public static (string Yaml, int Count) EncryptYaml(string yaml, string sectionKey = _parametersKey)
        {
            if (string.IsNullOrWhiteSpace(yaml)) return (yaml, 0);

            try
            {
                var stream = new YamlStream();
                stream.Load(new StringReader(yaml));

                // Values to replace (by their position in the text)
                var replacements = new List<(int Start, int End, string Value)>();

                foreach (var document in stream.Documents)
                {
                    var parameters = (document.RootNode as YamlMappingNode)?.Children
                        .FirstOrDefault(o => (o.Key as YamlScalarNode)?.Value == sectionKey).Value as YamlMappingNode;
                    if (parameters == null) continue;

                    foreach (var entry in parameters.Children)
                    {
                        var key = (entry.Key as YamlScalarNode)?.Value;
                        if (!IsSecretKey(key)) continue;

                        if (entry.Value is YamlScalarNode scalar && !string.IsNullOrEmpty(scalar.Value) && !IsEncrypted(scalar.Value) && !TrakHoundConfigurationVariables.IsReference(scalar.Value) &&
                            (scalar.Style == ScalarStyle.Plain || scalar.Style == ScalarStyle.SingleQuoted || scalar.Style == ScalarStyle.DoubleQuoted))
                        {
                            var encrypted = Encrypt(scalar.Value);
                            if (encrypted == scalar.Value) return (yaml, 0); // Key unavailable

                            replacements.Add(((int)scalar.Start.Index, (int)scalar.End.Index, $"\"{encrypted}\""));
                        }
                    }
                }

                if (replacements.Count == 0) return (yaml, 0);

                var builder = new StringBuilder(yaml);
                foreach (var replacement in replacements.OrderByDescending(o => o.Start))
                {
                    builder.Remove(replacement.Start, replacement.End - replacement.Start);
                    builder.Insert(replacement.Start, replacement.Value);
                }

                return (builder.ToString(), replacements.Count);
            }
            catch
            {
                return (yaml, 0);
            }
        }

        /// <summary>
        /// Decrypt the encrypted Parameters in a Configuration file's YAML (ex. so a Deployment can be installed on a different Instance)
        /// </summary>
        /// <returns>The YAML (unchanged if it has no encrypted values) and the number of values decrypted</returns>
        public static (string Yaml, int Count) DecryptYaml(string yaml)
        {
            if (string.IsNullOrWhiteSpace(yaml) || !yaml.Contains(Prefix)) return (yaml, 0);

            try
            {
                var stream = new YamlStream();
                stream.Load(new StringReader(yaml));

                var replacements = new List<(int Start, int End, string Value)>();
                foreach (var document in stream.Documents)
                {
                    FindEncrypted(document.RootNode, replacements);
                }

                if (replacements.Count == 0) return (yaml, 0);

                var builder = new StringBuilder(yaml);
                foreach (var replacement in replacements.OrderByDescending(o => o.Start))
                {
                    builder.Remove(replacement.Start, replacement.End - replacement.Start);
                    builder.Insert(replacement.Start, replacement.Value);
                }

                return (builder.ToString(), replacements.Count);
            }
            catch
            {
                return (yaml, 0);
            }
        }

        private static void FindEncrypted(YamlNode node, List<(int Start, int End, string Value)> replacements)
        {
            if (node is YamlScalarNode scalar)
            {
                // Values that can't be decrypted are left encrypted
                if (IsEncrypted(scalar.Value) && TryDecrypt(scalar.Value, out var value))
                {
                    replacements.Add(((int)scalar.Start.Index, (int)scalar.End.Index, ToDoubleQuoted(value)));
                }
            }
            else if (node is YamlMappingNode mapping)
            {
                foreach (var entry in mapping.Children) FindEncrypted(entry.Value, replacements);
            }
            else if (node is YamlSequenceNode sequence)
            {
                foreach (var child in sequence.Children) FindEncrypted(child, replacements);
            }
        }

        private static string ToDoubleQuoted(string value)
        {
            var builder = new StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (char.IsControl(c)) builder.Append($"\\u{(int)c:x4}");
                        else builder.Append(c);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }


        private static byte[] GetKey(bool create)
        {
            lock (_lock)
            {
                if (_key != null) return _key;

                // Default to the Application's Configuration directory
                if (_keyPath == null) _keyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TrakHoundConfigurations.Directory, KeyFilename);

                try
                {
                    if (File.Exists(_keyPath))
                    {
                        var key = Convert.FromBase64String(File.ReadAllText(_keyPath).Trim());
                        if (key.Length == _keySize) _key = key;

                        // A key created by an earlier version (or copied in) may be readable by other users
                        TrakHoundFilePermissions.RestrictFile(_keyPath);
                    }
                    else if (create)
                    {
                        var key = RandomNumberGenerator.GetBytes(_keySize);

                        var directory = Path.GetDirectoryName(_keyPath);
                        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                        // Don't overwrite a key created by another process
                        // Readable only by the user the Instance runs as (on Linux and macOS)
                        using (var fileStream = new FileStream(_keyPath, TrakHoundFilePermissions.CreateNewOptions()))
                        using (var writer = new StreamWriter(fileStream, new UTF8Encoding(false)))
                        {
                            writer.Write(Convert.ToBase64String(key));
                        }

                        _key = key;
                    }
                }
                catch
                {
                    // Created by another process : read it next time
                }

                return _key;
            }
        }
    }
}
