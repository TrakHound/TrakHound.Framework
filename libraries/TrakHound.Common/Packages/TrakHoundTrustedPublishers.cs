// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// A publisher whose Package signatures are trusted
    /// </summary>
    public class TrakHoundTrustedPublisher
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The publisher's public key (Base64 of the encoded key)
        /// </summary>
        [JsonPropertyName("publicKey")]
        public string PublicKey { get; set; }

        /// <summary>
        /// The Package IDs this publisher can sign, by the start of the ID (ex. "Contoso."). Empty allows any ID.
        /// This stops one trusted publisher from signing a Package that claims another's ID
        /// </summary>
        [JsonPropertyName("packageIdPrefixes")]
        public List<string> PackageIdPrefixes { get; set; }


        /// <summary>
        /// ID of the key, calculated from the key itself
        /// </summary>
        [JsonIgnore]
        public string KeyId => TrakHoundPackageSigner.GetKeyId(Convert.FromBase64String(PublicKey));


        public bool AllowsPackage(string packageId)
        {
            return PackageIdPrefixes.IsNullOrEmpty() || PackageIdPrefixes.Any(o => packageId?.StartsWith(o, StringComparison.OrdinalIgnoreCase) == true);
        }
    }

    /// <summary>
    /// The publishers trusted by a Configuration Profile (the "security/trusted-publishers.json" file in the Profile directory)
    /// </summary>
    public class TrakHoundTrustedPublishers
    {
        public const string Directory = "security";
        public const string Filename = "trusted-publishers.json";


        [JsonPropertyName("publishers")]
        public List<TrakHoundTrustedPublisher> Publishers { get; set; } = new List<TrakHoundTrustedPublisher>();


        public static string GetPath(string profilePath) => Path.Combine(profilePath, Directory, Filename);

        /// <summary>
        /// Read the Trusted Publishers of a Configuration Profile. Returns an empty list if the file doesn't exist.
        /// Throws if it can't be read, so that a damaged file is reported rather than quietly trusting nobody
        /// </summary>
        public static TrakHoundTrustedPublishers Read(string profilePath)
        {
            var path = GetPath(profilePath);
            if (!File.Exists(path)) return new TrakHoundTrustedPublishers();

            var publishers = JsonSerializer.Deserialize<TrakHoundTrustedPublishers>(File.ReadAllBytes(path));
            if (publishers?.Publishers == null) throw new InvalidDataException($"Invalid Trusted Publishers file : {path}");

            // Calculates each Key ID, which also checks that each key can be decoded
            foreach (var publisher in publishers.Publishers) _ = publisher.KeyId;

            return publishers;
        }

        public void Save(string profilePath)
        {
            var path = GetPath(profilePath);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));

            // Moved into place so that a check never reads it partially written, which would refuse every Package under Require
            var tempPath = path + ".tmp";
            File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, path, true);
        }

        /// <summary>
        /// Add or replace a publisher. Throws an ArgumentException if the value isn't a P-256 public key
        /// </summary>
        /// <param name="name">The name of the publisher</param>
        /// <param name="publicKeyPem">The publisher's public key in PEM format</param>
        /// <param name="packageIdPrefixes">The Package IDs the publisher can sign, by the start of the ID. Null or empty allows any ID</param>
        public TrakHoundTrustedPublisher Add(string name, string publicKeyPem, IEnumerable<string> packageIdPrefixes = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("The publisher needs a name");

            // Reading a private key would also succeed, and would mean it had been shared
            if (publicKeyPem?.Contains("PRIVATE KEY") == true) throw new ArgumentException("This is a private key. Trust the public key instead");

            byte[] key;
            try
            {
                key = TrakHoundPackageSigner.ReadPublicKeyPem(publicKeyPem);
            }
            catch (Exception)
            {
                throw new ArgumentException("The Public Key can't be read. It must be an ECDSA P-256 public key in PEM format, as in the .public.pem file from th packages keygen");
            }

            var publisher = new TrakHoundTrustedPublisher
            {
                Name = name.Trim(),
                PublicKey = Convert.ToBase64String(key),
                PackageIdPrefixes = packageIdPrefixes?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList()
            };

            // Adding a key again replaces its name and prefixes
            Publishers.RemoveAll(o => o.KeyId == publisher.KeyId);
            Publishers.Add(publisher);

            return publisher;
        }

        /// <summary>
        /// The publishers of a Configuration Profile plus others trusted from elsewhere (ex. synced from a TrakHound Package Manager).
        /// A key the Profile already trusts keeps the Profile's name and Package IDs, and a key that can't be decoded is left out
        /// </summary>
        public static TrakHoundTrustedPublishers Combine(TrakHoundTrustedPublishers local, IEnumerable<TrakHoundTrustedPublisher> additional)
        {
            var combined = new TrakHoundTrustedPublishers();
            if (local?.Publishers != null) combined.Publishers.AddRange(local.Publishers);
            if (additional == null) return combined;

            var keyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var publisher in combined.Publishers)
            {
                try { keyIds.Add(publisher.KeyId); } catch { }
            }

            foreach (var publisher in additional)
            {
                if (publisher == null || string.IsNullOrEmpty(publisher.PublicKey)) continue;

                string keyId;
                try { keyId = publisher.KeyId; }
                catch { continue; } // Not a key, so it can't be trusted

                if (keyIds.Add(keyId)) combined.Publishers.Add(publisher);
            }

            return combined;
        }

        public TrakHoundTrustedPublisher Find(string keyId)
        {
            return Publishers.FirstOrDefault(o => string.Equals(o.KeyId, keyId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
