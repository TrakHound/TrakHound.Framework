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
    /// The Trusted Publishers last read from a TrakHound Package Manager (a Package Source)
    /// </summary>
    public class TrakHoundPackageManagerPublisherSource
    {
        /// <summary>
        /// The ID of the Package Source
        /// </summary>
        [JsonPropertyName("sourceId")]
        public string SourceId { get; set; }

        /// <summary>
        /// The name of the Package Source when the publishers were read, for display
        /// </summary>
        [JsonPropertyName("sourceName")]
        public string SourceName { get; set; }

        /// <summary>
        /// When the publishers were last read successfully (UTC)
        /// </summary>
        [JsonPropertyName("updated")]
        public DateTime Updated { get; set; }

        [JsonPropertyName("publishers")]
        public List<TrakHoundTrustedPublisher> Publishers { get; set; } = new List<TrakHoundTrustedPublisher>();
    }

    /// <summary>
    /// The Trusted Publishers synced from the TrakHound Package Managers an Instance uses (the "security/package-manager-publishers.json" file in the Profile directory).
    /// Kept so that the last copy that was read is still trusted when a Package Manager can't be reached (ex. when the Instance starts offline)
    /// </summary>
    public class TrakHoundPackageManagerPublishers
    {
        public const string Filename = "package-manager-publishers.json";


        [JsonPropertyName("sources")]
        public List<TrakHoundPackageManagerPublisherSource> Sources { get; set; } = new List<TrakHoundPackageManagerPublisherSource>();


        public static string GetPath(string profilePath) => Path.Combine(profilePath, TrakHoundTrustedPublishers.Directory, Filename);

        /// <summary>
        /// Read the synced publishers of a Configuration Profile. Returns an empty list if the file doesn't exist, and throws if it can't be read
        /// </summary>
        public static TrakHoundPackageManagerPublishers Read(string profilePath)
        {
            var path = GetPath(profilePath);
            if (!File.Exists(path)) return new TrakHoundPackageManagerPublishers();

            var publishers = JsonSerializer.Deserialize<TrakHoundPackageManagerPublishers>(File.ReadAllBytes(path));
            if (publishers?.Sources == null) throw new InvalidDataException($"Invalid Package Manager Publishers file : {path}");

            foreach (var source in publishers.Sources) source.Publishers ??= new List<TrakHoundTrustedPublisher>();

            return publishers;
        }

        public void Save(string profilePath)
        {
            var path = GetPath(profilePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            // Moved into place so that a check never reads it partially written
            var tempPath = path + ".tmp";
            File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, path, true);
        }

        public TrakHoundPackageManagerPublisherSource GetSource(string sourceId)
        {
            return Sources.FirstOrDefault(o => string.Equals(o.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Replace the publishers read from a Package Source. Returns true if the trusted keys or their Package IDs changed
        /// </summary>
        public bool Set(string sourceId, string sourceName, IEnumerable<TrakHoundTrustedPublisher> publishers, DateTime updated)
        {
            var existing = GetSource(sourceId);
            var list = publishers?.Where(o => o != null && !string.IsNullOrEmpty(o.PublicKey)).ToList() ?? new List<TrakHoundTrustedPublisher>();
            var changed = existing == null ? list.Count > 0 : GetSignature(existing.Publishers) != GetSignature(list);

            Sources.RemoveAll(o => string.Equals(o.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));
            Sources.Add(new TrakHoundPackageManagerPublisherSource { SourceId = sourceId, SourceName = sourceName, Updated = updated, Publishers = list });

            return changed;
        }

        /// <summary>
        /// Remove the publishers of Package Sources that aren't listed (ex. a Source that was removed or is no longer a Package Manager). Returns true if any were removed
        /// </summary>
        public bool RemoveOtherSources(IEnumerable<string> sourceIds)
        {
            var keep = new HashSet<string>(sourceIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return Sources.RemoveAll(o => o.SourceId == null || !keep.Contains(o.SourceId)) > 0;
        }

        /// <summary>
        /// The publishers of the listed Package Sources (every Source if null)
        /// </summary>
        public IEnumerable<TrakHoundTrustedPublisher> GetPublishers(IEnumerable<string> sourceIds = null)
        {
            var include = sourceIds != null ? new HashSet<string>(sourceIds, StringComparer.OrdinalIgnoreCase) : null;
            return Sources.Where(o => include == null || (o.SourceId != null && include.Contains(o.SourceId))).SelectMany(o => o.Publishers).ToList();
        }


        private static string GetSignature(IEnumerable<TrakHoundTrustedPublisher> publishers)
        {
            return string.Join("\n", publishers
                .Select(o => $"{o.Name}|{o.PublicKey}|{string.Join(",", o.PackageIdPrefixes ?? new List<string>())}")
                .OrderBy(o => o, StringComparer.Ordinal));
        }
    }
}
