// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// Why an Instance sent its Package Inventory
    /// </summary>
    public static class TrakHoundPackageInventoryReason
    {
        /// <summary>
        /// The Instance started
        /// </summary>
        public const string Startup = "startup";

        /// <summary>
        /// Packages were installed, updated or removed
        /// </summary>
        public const string Change = "change";

        /// <summary>
        /// Sent on an interval, so the Package Manager knows the Instance is still running
        /// </summary>
        public const string Heartbeat = "heartbeat";
    }

    /// <summary>
    /// A Package version installed on an Instance, as reported to a TrakHound Package Manager
    /// </summary>
    public class TrakHoundPackageInventoryItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("uuid")]
        public string Uuid { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        /// <summary>
        /// The content hash recorded when the build was packed. Two builds of one Version share a UUID, so this tells them apart
        /// </summary>
        [JsonPropertyName("contentHash")]
        public string ContentHash { get; set; }

        [JsonPropertyName("installed")]
        public DateTime? Installed { get; set; }

        [JsonPropertyName("inUse")]
        public bool InUse { get; set; }

        /// <summary>
        /// The name of the <see cref="TrakHoundPackageVerificationState"/> (ex. Verified)
        /// </summary>
        [JsonPropertyName("state")]
        public string State { get; set; }

        [JsonPropertyName("publisher")]
        public string Publisher { get; set; }

        [JsonPropertyName("keyId")]
        public string KeyId { get; set; }

        [JsonPropertyName("repository")]
        public string Repository { get; set; }

        [JsonPropertyName("repositoryCommit")]
        public string RepositoryCommit { get; set; }


        public static TrakHoundPackageInventoryItem Create(TrakHoundPackageVerificationInformation information)
        {
            if (information == null) return null;

            return new TrakHoundPackageInventoryItem
            {
                Id = information.Id,
                Version = information.Version,
                Uuid = information.Uuid,
                Category = information.Category,
                ContentHash = information.ContentHash,
                Installed = information.Installed,
                InUse = information.InUse,
                State = information.State.ToString(),
                Publisher = information.Publisher,
                KeyId = information.KeyId,
                Repository = information.Repository,
                RepositoryCommit = information.RepositoryCommit
            };
        }
    }

    /// <summary>
    /// Every Package version installed on an Instance (used or not), sent to a TrakHound Package Manager (POST {Source}/inventory)
    /// so it keeps a record of each Instance's Packages for reports and drift detection
    /// </summary>
    public class TrakHoundPackageInventory
    {
        [JsonPropertyName("instanceId")]
        public string InstanceId { get; set; }

        [JsonPropertyName("instanceName")]
        public string InstanceName { get; set; }

        [JsonPropertyName("hostName")]
        public string HostName { get; set; }

        [JsonPropertyName("frameworkVersion")]
        public string FrameworkVersion { get; set; }

        [JsonPropertyName("operatingSystem")]
        public string OperatingSystem { get; set; }

        /// <summary>
        /// The name of the <see cref="TrakHoundPackageVerificationMode"/> in use (ex. Warn)
        /// </summary>
        [JsonPropertyName("verificationMode")]
        public string VerificationMode { get; set; }

        /// <summary>
        /// Why it was sent (see <see cref="TrakHoundPackageInventoryReason"/>)
        /// </summary>
        [JsonPropertyName("reason")]
        public string Reason { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("packages")]
        public List<TrakHoundPackageInventoryItem> Packages { get; set; } = new List<TrakHoundPackageInventoryItem>();


        /// <summary>
        /// Create the Inventory from a Verification Report (which lists every Package version on disk, including those that were refused)
        /// </summary>
        public static TrakHoundPackageInventory Create(TrakHoundPackageVerificationReport report, string reason)
        {
            return new TrakHoundPackageInventory
            {
                VerificationMode = report?.Mode.ToString(),
                Reason = reason,
                Timestamp = DateTime.UtcNow,
                Packages = report?.Packages?.Select(TrakHoundPackageInventoryItem.Create).Where(o => o != null).ToList() ?? new List<TrakHoundPackageInventoryItem>()
            };
        }
    }

    /// <summary>
    /// The response of a TrakHound Package Manager to an Inventory
    /// </summary>
    public class TrakHoundPackageInventoryResponse
    {
        /// <summary>
        /// The number of Packages the Package Manager recorded
        /// </summary>
        [JsonPropertyName("received")]
        public int Received { get; set; }
    }
}
