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
    /// The server key pinned for a TrakHound Package Manager (a Package Source)
    /// </summary>
    public class TrakHoundPackageManagerPinnedKey : TrakHoundPackageManagerServerKey
    {
        [JsonPropertyName("sourceId")]
        public string SourceId { get; set; }

        /// <summary>
        /// The name of the Package Source when the key was pinned, for display
        /// </summary>
        [JsonPropertyName("sourceName")]
        public string SourceName { get; set; }

        /// <summary>
        /// When the key was pinned (UTC)
        /// </summary>
        [JsonPropertyName("pinned")]
        public DateTime Pinned { get; set; }
    }

    /// <summary>
    /// The server keys pinned for the TrakHound Package Managers an Instance uses (the "security/package-manager-key.json" file in the Profile directory).
    /// The first key read from a Package Manager is pinned, and a different key is refused until an administrator clears it
    /// </summary>
    public class TrakHoundPackageManagerKeys
    {
        public const string Filename = "package-manager-key.json";


        [JsonPropertyName("sources")]
        public List<TrakHoundPackageManagerPinnedKey> Sources { get; set; } = new List<TrakHoundPackageManagerPinnedKey>();


        public static string GetPath(string profilePath) => Path.Combine(profilePath, TrakHoundTrustedPublishers.Directory, Filename);

        /// <summary>
        /// Read the pinned keys of a Configuration Profile. Returns an empty list if the file doesn't exist, and throws if it can't be read
        /// </summary>
        public static TrakHoundPackageManagerKeys Read(string profilePath)
        {
            var path = GetPath(profilePath);
            if (!File.Exists(path)) return new TrakHoundPackageManagerKeys();

            var keys = JsonSerializer.Deserialize<TrakHoundPackageManagerKeys>(File.ReadAllBytes(path));
            if (keys?.Sources == null) throw new InvalidDataException($"Invalid Package Manager Key file : {path}");

            return keys;
        }

        public void Save(string profilePath) => TrakHoundPackageManagerApprovalFiles.Save(GetPath(profilePath), this);

        public TrakHoundPackageManagerPinnedKey Get(string sourceId)
        {
            return Sources.FirstOrDefault(o => string.Equals(o.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));
        }

        public void Pin(string sourceId, string sourceName, TrakHoundPackageManagerServerKey key, DateTime pinned)
        {
            Clear(sourceId);
            Sources.Add(new TrakHoundPackageManagerPinnedKey
            {
                SourceId = sourceId,
                SourceName = sourceName,
                ServerId = key.ServerId,
                KeyId = key.KeyId,
                PublicKeyPem = key.PublicKeyPem,
                Created = key.Created,
                Pinned = pinned
            });
        }

        /// <summary>
        /// Remove the pinned key of a Package Source, so that the next key read from it is pinned. Returns true if there was one
        /// </summary>
        public bool Clear(string sourceId)
        {
            return Sources.RemoveAll(o => string.Equals(o.SourceId, sourceId, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }

    /// <summary>
    /// The last valid attestation list read from a TrakHound Package Manager (a Package Source), as it was served so its signature can be checked again
    /// </summary>
    public class TrakHoundPackageManagerAttestationSource
    {
        [JsonPropertyName("sourceId")]
        public string SourceId { get; set; }

        [JsonPropertyName("sourceName")]
        public string SourceName { get; set; }

        /// <summary>
        /// When the list was read (UTC)
        /// </summary>
        [JsonPropertyName("received")]
        public DateTime Received { get; set; }

        [JsonPropertyName("envelope")]
        public TrakHoundPackageAttestationEnvelope Envelope { get; set; }
    }

    /// <summary>
    /// The last valid attestation list of each TrakHound Package Manager an Instance uses (the "security/package-manager-attestations.json" file in the Profile directory).
    /// Kept so that an Instance that starts without its Package Manager still has the list
    /// </summary>
    public class TrakHoundPackageManagerAttestations
    {
        public const string Filename = "package-manager-attestations.json";


        [JsonPropertyName("sources")]
        public List<TrakHoundPackageManagerAttestationSource> Sources { get; set; } = new List<TrakHoundPackageManagerAttestationSource>();


        public static string GetPath(string profilePath) => Path.Combine(profilePath, TrakHoundTrustedPublishers.Directory, Filename);

        /// <summary>
        /// Read the cached lists of a Configuration Profile. Returns an empty list if the file doesn't exist, and throws if it can't be read
        /// </summary>
        public static TrakHoundPackageManagerAttestations Read(string profilePath)
        {
            var path = GetPath(profilePath);
            if (!File.Exists(path)) return new TrakHoundPackageManagerAttestations();

            var attestations = JsonSerializer.Deserialize<TrakHoundPackageManagerAttestations>(File.ReadAllBytes(path));
            if (attestations?.Sources == null) throw new InvalidDataException($"Invalid Package Manager Attestations file : {path}");

            return attestations;
        }

        public void Save(string profilePath) => TrakHoundPackageManagerApprovalFiles.Save(GetPath(profilePath), this);

        public TrakHoundPackageManagerAttestationSource Get(string sourceId)
        {
            return Sources.FirstOrDefault(o => string.Equals(o.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));
        }

        public void Set(string sourceId, string sourceName, TrakHoundPackageAttestationEnvelope envelope, DateTime received)
        {
            Remove(sourceId);
            Sources.Add(new TrakHoundPackageManagerAttestationSource { SourceId = sourceId, SourceName = sourceName, Received = received, Envelope = envelope });
        }

        public bool Remove(string sourceId)
        {
            return Sources.RemoveAll(o => string.Equals(o.SourceId, sourceId, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        /// <summary>
        /// Remove the lists of Package Sources that aren't listed (ex. a Source that was removed or is no longer a Package Manager). Returns true if any were removed
        /// </summary>
        public bool RemoveOtherSources(IEnumerable<string> sourceIds)
        {
            var keep = new HashSet<string>(sourceIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return Sources.RemoveAll(o => o.SourceId == null || !keep.Contains(o.SourceId)) > 0;
        }
    }

    public static class TrakHoundPackageManagerApprovalFiles
    {
        /// <summary>
        /// Read the approvals kept in a Configuration Profile : each cached list whose signature matches its Source's pinned key, expired or not.
        /// Never throws : a file that can't be read gives no lists, so every Package is refused
        /// </summary>
        /// <param name="profilePath">The Configuration Profile directory</param>
        /// <param name="sourceIds">The Package Manager Sources to include (every Source if null)</param>
        public static TrakHoundPackageApprovals ReadApprovals(string profilePath, IEnumerable<string> sourceIds = null)
        {
            try
            {
                return CreateApprovals(TrakHoundPackageManagerKeys.Read(profilePath), TrakHoundPackageManagerAttestations.Read(profilePath), sourceIds, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                return new TrakHoundPackageApprovals(null, $"the Package Manager files can't be read : {ex.Message}");
            }
        }

        /// <summary>
        /// The approvals of the cached lists whose signature matches their Source's pinned key (expired or not)
        /// </summary>
        public static TrakHoundPackageApprovals CreateApprovals(TrakHoundPackageManagerKeys keys, TrakHoundPackageManagerAttestations attestations, IEnumerable<string> sourceIds, DateTime now)
        {
            var include = sourceIds != null ? new HashSet<string>(sourceIds, StringComparer.OrdinalIgnoreCase) : null;
            var lists = new List<(string, TrakHoundPackageAttestationList)>();
            var reasons = new List<string>();

            foreach (var source in attestations?.Sources ?? Enumerable.Empty<TrakHoundPackageManagerAttestationSource>())
            {
                if (source?.SourceId == null || (include != null && !include.Contains(source.SourceId))) continue;

                var key = keys?.Get(source.SourceId);
                var (list, error) = TrakHoundPackageAttestationVerifier.Read(source.Envelope, key, now, true);
                if (list != null) lists.Add((source.SourceName ?? source.SourceId, list));
                else reasons.Add($"{source.SourceName ?? source.SourceId} : {error}");
            }

            return new TrakHoundPackageApprovals(lists, reasons.Count > 0 ? string.Join("; ", reasons) : null);
        }

        internal static void Save(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // Moved into place so that a check never reads it partially written
            var tempPath = path + ".tmp";
            File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, path, true);
        }
    }
}
