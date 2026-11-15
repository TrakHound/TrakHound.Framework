// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// What happens when a Package is installed or loaded
    /// </summary>
    public enum TrakHoundPackageVerificationMode
    {
        /// <summary>
        /// Packages aren't checked
        /// </summary>
        Off = 0,

        /// <summary>
        /// Packages are checked and problems are reported, but every Package is used
        /// </summary>
        Warn = 1,

        /// <summary>
        /// Only Packages whose files match their Manifest and that are signed by a Trusted Publisher are used
        /// </summary>
        Require = 2,

        /// <summary>
        /// As Require, and the build must be on a valid attestation list of a TrakHound Package Manager Source, so a Package copied onto the Instance by hand is refused
        /// </summary>
        RequireApproval = 3
    }

    /// <summary>
    /// The result of a check in a single word, for status displays
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TrakHoundPackageVerificationState
    {
        /// <summary>
        /// The Package hasn't been checked (ex. Verification Mode is Off)
        /// </summary>
        NotChecked,

        /// <summary>
        /// The files match the Manifest, and a Trusted Publisher signed it
        /// </summary>
        Verified,

        /// <summary>
        /// The files match the Manifest, but nobody signed it
        /// </summary>
        Unsigned,

        /// <summary>
        /// The Package has no Manifest, so its files can't be checked
        /// </summary>
        NoManifest,

        /// <summary>
        /// The files match the Manifest, but it was signed by a key that isn't trusted
        /// </summary>
        Untrusted,

        /// <summary>
        /// The files or the signature don't match, so the Package isn't what was published
        /// </summary>
        Modified,

        /// <summary>
        /// The files match the Manifest and a Trusted Publisher signed it, but a TrakHound Package Manager hasn't approved the build (RequireApproval Mode)
        /// </summary>
        NotApproved
    }

    /// <summary>
    /// The result of the last check of a Package, for the Admin Interface and the System API
    /// </summary>
    public class TrakHoundPackageVerificationInformation
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// The UUID of the Package version (from its ID and Version, so two builds of one Version share it)
        /// </summary>
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        /// <summary>
        /// The content hash recorded when the installed build was packed, which tells two builds of one Version apart. Null for a Package without a Manifest
        /// </summary>
        [JsonPropertyName("contentHash")]
        public string ContentHash { get; set; }

        /// <summary>
        /// When the Version was installed (UTC)
        /// </summary>
        [JsonPropertyName("installed")]
        public DateTime? Installed { get; set; }

        /// <summary>
        /// True if a Configuration uses this Version
        /// </summary>
        [JsonPropertyName("inUse")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool InUse { get; set; }

        [JsonPropertyName("state")]
        public TrakHoundPackageVerificationState State { get; set; }

        /// <summary>
        /// True if the Package failed under the Require or RequireApproval Mode, so it isn't used
        /// </summary>
        [JsonPropertyName("refused")]
        public bool Refused { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The name of the Trusted Publisher that signed the Package
        /// </summary>
        [JsonPropertyName("publisher")]
        public string Publisher { get; set; }

        /// <summary>
        /// The ID of the key the Package says signed it. Only proven if the State is Verified
        /// </summary>
        [JsonPropertyName("keyId")]
        public string KeyId { get; set; }

        [JsonPropertyName("repository")]
        public string Repository { get; set; }

        [JsonPropertyName("repositoryBranch")]
        public string RepositoryBranch { get; set; }

        [JsonPropertyName("repositoryCommit")]
        public string RepositoryCommit { get; set; }

        /// <summary>
        /// True if the working tree had uncommitted changes when the Package was built, so it may not match the Commit
        /// </summary>
        [JsonPropertyName("repositoryDirty")]
        public bool? RepositoryDirty { get; set; }

        /// <summary>
        /// What built the Package (ex. the URL of a CI run). Not set for a build on a developer's machine
        /// </summary>
        [JsonPropertyName("builder")]
        public string Builder { get; set; }

        [JsonPropertyName("buildConfiguration")]
        public string BuildConfiguration { get; set; }

        [JsonPropertyName("dotnetSdkVersion")]
        public string DotnetSdkVersion { get; set; }

        /// <summary>
        /// True if the Manifest lists an SBOM
        /// </summary>
        [JsonPropertyName("hasSbom")]
        public bool HasSbom { get; set; }

        [JsonPropertyName("changed")]
        public IEnumerable<string> Changed { get; set; }

        [JsonPropertyName("missing")]
        public IEnumerable<string> Missing { get; set; }

        [JsonPropertyName("added")]
        public IEnumerable<string> Added { get; set; }

        [JsonPropertyName("errors")]
        public IEnumerable<string> Errors { get; set; }
    }

    /// <summary>
    /// The Verification Mode and the result of the last check of each Package
    /// </summary>
    public class TrakHoundPackageVerificationReport
    {
        /// <summary>
        /// The Mode in use, which can differ from the saved Setting until the Instance is restarted
        /// </summary>
        [JsonPropertyName("mode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TrakHoundPackageVerificationMode Mode { get; set; }

        [JsonPropertyName("packages")]
        public IEnumerable<TrakHoundPackageVerificationInformation> Packages { get; set; }
    }

    public static class TrakHoundPackageVerificationPolicy
    {
        private const int _maxListedFiles = 20;


        public static TrakHoundPackageVerificationState GetState(TrakHoundPackageVerificationResult result)
        {
            if (result == null) return TrakHoundPackageVerificationState.NotChecked;

            switch (result.Status)
            {
                case TrakHoundPackageVerificationStatus.NoManifest: return TrakHoundPackageVerificationState.NoManifest;
                case TrakHoundPackageVerificationStatus.Invalid: return TrakHoundPackageVerificationState.Modified;
            }

            switch (result.SignatureStatus)
            {
                case TrakHoundPackageSignatureStatus.Trusted: return result.Approval != null && !result.Approval.Approved ? TrakHoundPackageVerificationState.NotApproved : TrakHoundPackageVerificationState.Verified;
                case TrakHoundPackageSignatureStatus.Untrusted: return TrakHoundPackageVerificationState.Untrusted;
                default: return TrakHoundPackageVerificationState.Unsigned;
            }
        }

        /// <summary>
        /// The name of a State, for status displays
        /// </summary>
        public static string GetStateText(TrakHoundPackageVerificationState state)
        {
            switch (state)
            {
                case TrakHoundPackageVerificationState.Verified: return "Verified";
                case TrakHoundPackageVerificationState.Unsigned: return "Unsigned";
                case TrakHoundPackageVerificationState.NoManifest: return "No Manifest";
                case TrakHoundPackageVerificationState.Untrusted: return "Untrusted";
                case TrakHoundPackageVerificationState.Modified: return "Modified";
                case TrakHoundPackageVerificationState.NotApproved: return "Not Approved";
                default: return "Not Checked";
            }
        }

        public static TrakHoundPackageVerificationInformation CreateInformation(TrakHoundPackage package, TrakHoundPackageVerificationResult result, bool refused, DateTime? installed = null, bool inUse = false)
        {
            return new TrakHoundPackageVerificationInformation
            {
                Id = package.Id,
                Version = package.Version,
                Uuid = package.Uuid,
                Category = package.Category,
                ContentHash = package.ContentHash,
                Installed = installed,
                InUse = inUse,
                State = GetState(result),
                Refused = refused,
                Description = Describe(result),
                Publisher = result?.Publisher?.Name,
                KeyId = result?.SignatureKeyId,
                Repository = result?.Manifest?.Repository,
                RepositoryBranch = result?.Manifest?.RepositoryBranch,
                RepositoryCommit = result?.Manifest?.RepositoryCommit,
                RepositoryDirty = result?.Manifest?.RepositoryDirty,
                Builder = result?.Manifest?.Builder,
                BuildConfiguration = result?.Manifest?.BuildConfiguration,
                DotnetSdkVersion = result?.Manifest?.DotnetSdkVersion,
                HasSbom = result?.Manifest?.Files?.Any(o => o.Path == TrakHoundPackage.SbomFilename) ?? false,
                Changed = result?.Changed,
                Missing = result?.Missing,
                Added = result?.Added,
                Errors = result?.Errors
            };
        }


        /// <summary>
        /// Returns true if the Mode refuses Packages that aren't signed by a Trusted Publisher (Require and RequireApproval)
        /// </summary>
        public static bool RequiresTrust(TrakHoundPackageVerificationMode mode)
        {
            return mode == TrakHoundPackageVerificationMode.Require || mode == TrakHoundPackageVerificationMode.RequireApproval;
        }

        /// <summary>
        /// Record on a result whether a Package Manager approved the build (for the RequireApproval Mode). Only a Package whose files match and that a Trusted Publisher signed
        /// is checked (anything else is refused anyway), since only then is its Manifest's Content Hash proven
        /// </summary>
        /// <param name="result">The result of checking the Package</param>
        /// <param name="approvals">The Package Managers' lists. If null, the build isn't approved</param>
        /// <param name="install">True if the Package is being installed rather than loaded (a build only on an expired list isn't installed)</param>
        public static void CheckApproval(TrakHoundPackageVerificationResult result, TrakHoundPackageApprovals approvals, bool install)
        {
            if (result == null || result.Status != TrakHoundPackageVerificationStatus.Valid || result.SignatureStatus != TrakHoundPackageSignatureStatus.Trusted) return;

            approvals ??= new TrakHoundPackageApprovals(null, "no Package Source is a TrakHound Package Manager");

            var manifest = result.Manifest;
            result.Approval = manifest != null
                ? approvals.Check(manifest.Id, manifest.Version, manifest.GetContentHash(), install, DateTime.UtcNow)
                : new TrakHoundPackageApproval(false, false, null, "Not approved : the Package has no Manifest");
        }

        /// <summary>
        /// Returns true if a Package with this result can be used. Under RequireApproval, the result's <see cref="TrakHoundPackageVerificationResult.Approval"/> must be set and approved
        /// </summary>
        public static bool IsAllowed(TrakHoundPackageVerificationMode mode, TrakHoundPackageVerificationResult result)
        {
            if (!RequiresTrust(mode)) return true;

            var trusted = result != null && result.Status == TrakHoundPackageVerificationStatus.Valid && result.SignatureStatus == TrakHoundPackageSignatureStatus.Trusted;
            if (mode == TrakHoundPackageVerificationMode.Require) return trusted;

            return trusted && result.Approval?.Approved == true;
        }

        /// <summary>
        /// Describe a result in a line for logs and install messages
        /// </summary>
        public static string Describe(TrakHoundPackageVerificationResult result)
        {
            if (result == null) return "Not checked";

            switch (result.Status)
            {
                case TrakHoundPackageVerificationStatus.NoManifest:
                    return "No Manifest";

                case TrakHoundPackageVerificationStatus.Invalid:
                    var problems = new List<string>(result.Errors);
                    problems.AddRange(result.Changed.Select(o => $"Changed {o}"));
                    problems.AddRange(result.Missing.Select(o => $"Missing {o}"));
                    problems.AddRange(result.Added.Select(o => $"Added {o}"));

                    var listed = string.Join(", ", problems.Take(_maxListedFiles));
                    return problems.Count > _maxListedFiles ? $"{listed} (and {problems.Count - _maxListedFiles} more)" : listed;
            }

            switch (result.SignatureStatus)
            {
                case TrakHoundPackageSignatureStatus.Trusted:
                    var signed = $"Signed by {result.Publisher?.Name} ({result.Publisher?.KeyId})";
                    if (result.Approval == null) return signed;
                    return result.Approval.Approved ? $"{signed}, {result.Approval.Description}" : result.Approval.Description;
                case TrakHoundPackageSignatureStatus.Untrusted: return $"Signed by an untrusted key ({result.SignatureKeyId})";
                default: return "Unsigned";
            }
        }
    }

    /// <summary>
    /// The result of checking a Package when it was installed or loaded
    /// </summary>
    public class TrakHoundPackageVerifiedArgs : EventArgs
    {
        public TrakHoundPackage Package { get; }

        public TrakHoundPackageVerificationResult Result { get; }

        /// <summary>
        /// False if the Package was refused, so it won't be used
        /// </summary>
        public bool Allowed { get; }

        /// <summary>
        /// Set if the Trusted Publishers couldn't be read, in which case no signature was trusted
        /// </summary>
        public string TrustedPublishersError { get; }

        /// <summary>
        /// True if the Package was checked for an install rather than when it was loaded
        /// </summary>
        public bool Install { get; }


        public TrakHoundPackageVerifiedArgs(TrakHoundPackage package, TrakHoundPackageVerificationResult result, bool allowed, string trustedPublishersError, bool install = false)
        {
            Package = package;
            Result = result;
            Allowed = allowed;
            TrustedPublishersError = trustedPublishersError;
            Install = install;
        }
    }
}
