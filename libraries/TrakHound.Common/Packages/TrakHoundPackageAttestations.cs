// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// The server key of a TrakHound Package Manager (GET {Source}/server), which signs its attestation lists. An Instance pins the first one it reads
    /// </summary>
    public class TrakHoundPackageManagerServerKey
    {
        [JsonPropertyName("serverId")]
        public string ServerId { get; set; }

        [JsonPropertyName("keyId")]
        public string KeyId { get; set; }

        /// <summary>
        /// The ECDSA P-256 public key (PEM)
        /// </summary>
        [JsonPropertyName("publicKeyPem")]
        public string PublicKeyPem { get; set; }

        [JsonPropertyName("created")]
        public DateTime? Created { get; set; }


        /// <summary>
        /// True if both are the same key
        /// </summary>
        public bool IsSameKey(TrakHoundPackageManagerServerKey other)
        {
            if (other == null) return false;

            try
            {
                return TrakHoundPackageSigner.ReadPublicKeyPem(PublicKeyPem).AsSpan().SequenceEqual(TrakHoundPackageSigner.ReadPublicKeyPem(other.PublicKeyPem));
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// A signed attestation list as a TrakHound Package Manager serves it (GET {Source}/attestations). The signature is over the decoded Payload bytes
    /// </summary>
    public class TrakHoundPackageAttestationEnvelope
    {
        public const string FormatName = "trakhound-package-attestations";
        public const int CurrentFormatVersion = 1;


        [JsonPropertyName("format")]
        public string Format { get; set; }

        [JsonPropertyName("formatVersion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int FormatVersion { get; set; }

        /// <summary>
        /// The <see cref="TrakHoundPackageAttestationList"/> as UTF-8 JSON, Base64
        /// </summary>
        [JsonPropertyName("payload")]
        public string Payload { get; set; }

        [JsonPropertyName("keyId")]
        public string KeyId { get; set; }

        /// <summary>
        /// ECDSA P-256 / SHA-256 over the Payload bytes, Base64
        /// </summary>
        [JsonPropertyName("signature")]
        public string Signature { get; set; }
    }

    /// <summary>
    /// The builds a TrakHound Package Manager approved (or deprecated) in an Instance's Channel. Revoked builds aren't listed
    /// </summary>
    public class TrakHoundPackageAttestationList
    {
        [JsonPropertyName("serverId")]
        public string ServerId { get; set; }

        [JsonPropertyName("keyId")]
        public string KeyId { get; set; }

        [JsonPropertyName("channel")]
        public string Channel { get; set; }

        [JsonPropertyName("instanceId")]
        public string InstanceId { get; set; }

        [JsonPropertyName("issued")]
        public DateTime Issued { get; set; }

        [JsonPropertyName("expires")]
        public DateTime Expires { get; set; }

        [JsonPropertyName("packages")]
        public List<TrakHoundPackageAttestation> Packages { get; set; } = new List<TrakHoundPackageAttestation>();
    }

    /// <summary>
    /// One build on an attestation list
    /// </summary>
    public class TrakHoundPackageAttestation
    {
        public const string Approved = "approved";
        public const string Deprecated = "deprecated";


        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("uuid")]
        public string Uuid { get; set; }

        /// <summary>
        /// The build's Content Hash (from its Manifest), so another build of the Version isn't covered
        /// </summary>
        [JsonPropertyName("contentHash")]
        public string ContentHash { get; set; }

        /// <summary>
        /// approved or deprecated (both can be used)
        /// </summary>
        [JsonPropertyName("state")]
        public string State { get; set; }
    }

    /// <summary>
    /// Checks the server keys and attestation lists of TrakHound Package Managers
    /// </summary>
    public static class TrakHoundPackageAttestationVerifier
    {
        /// <summary>
        /// Check a server key as read from a Package Manager. Returns the problem, or null if it can be pinned
        /// </summary>
        public static string CheckServerKey(TrakHoundPackageManagerServerKey key)
        {
            if (key == null || string.IsNullOrEmpty(key.PublicKeyPem)) return "No public key";

            byte[] publicKey;
            try
            {
                publicKey = TrakHoundPackageSigner.ReadPublicKeyPem(key.PublicKeyPem);
            }
            catch (Exception ex)
            {
                return $"The public key can't be read : {ex.Message}";
            }

            // The key ID is what an administrator compares with the Package Manager's, so it must be the key's own
            var keyId = TrakHoundPackageSigner.GetKeyId(publicKey);
            if (!string.Equals(keyId, key.KeyId, StringComparison.OrdinalIgnoreCase)) return $"The key ID ({key.KeyId}) isn't the key's ({keyId})";

            return null;
        }

        /// <summary>
        /// Check an attestation list : a known format, signed by the pinned key over the payload bytes, the payload naming the key that signed it, and not expired.
        /// Returns the list, or null with the reason
        /// </summary>
        /// <param name="envelope">The list as served</param>
        /// <param name="pinnedKey">The Package Manager's pinned server key</param>
        /// <param name="now">The time to check the expiry against (UTC)</param>
        /// <param name="allowExpired">Accept a list that has expired (ex. reading back a list that was valid when it was received)</param>
        public static (TrakHoundPackageAttestationList List, string Error) Read(TrakHoundPackageAttestationEnvelope envelope, TrakHoundPackageManagerServerKey pinnedKey, DateTime now, bool allowExpired = false)
        {
            if (envelope == null || envelope.Format != TrakHoundPackageAttestationEnvelope.FormatName) return (null, "It isn't an attestation list");
            if (envelope.FormatVersion != TrakHoundPackageAttestationEnvelope.CurrentFormatVersion) return (null, $"Its format version ({envelope.FormatVersion}) isn't supported");
            if (pinnedKey == null || string.IsNullOrEmpty(pinnedKey.PublicKeyPem)) return (null, "The Package Manager's server key isn't pinned");

            byte[] payload;
            byte[] signature;
            try
            {
                payload = Convert.FromBase64String(envelope.Payload ?? string.Empty);
                signature = Convert.FromBase64String(envelope.Signature ?? string.Empty);
            }
            catch (FormatException)
            {
                return (null, "Its payload or signature can't be read");
            }

            bool verified;
            try
            {
                verified = payload.Length > 0 && signature.Length > 0 && TrakHoundPackageSigner.Verify(TrakHoundPackageSigner.ReadPublicKeyPem(pinnedKey.PublicKeyPem), payload, signature);
            }
            catch
            {
                verified = false;
            }

            if (!verified) return (null, $"Its signature doesn't match the pinned server key ({pinnedKey.KeyId})");

            TrakHoundPackageAttestationList list;
            try
            {
                list = JsonSerializer.Deserialize<TrakHoundPackageAttestationList>(payload);
            }
            catch (JsonException)
            {
                return (null, "Its list can't be read");
            }

            if (list == null) return (null, "Its list can't be read");
            if (!string.Equals(list.KeyId, envelope.KeyId, StringComparison.Ordinal)) return (null, "Its list names another key than the one that signed it");
            if (!allowExpired && ToUtc(list.Expires) <= now) return (null, $"It expired at {ToUtc(list.Expires):u}");

            list.Packages ??= new List<TrakHoundPackageAttestation>();

            return (list, null);
        }

        internal static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
    }

    /// <summary>
    /// Whether a TrakHound Package Manager approved a Package build, under the RequireApproval Verification Mode
    /// </summary>
    public class TrakHoundPackageApproval
    {
        /// <summary>
        /// True if the build can be used (or installed, if it was checked for an install)
        /// </summary>
        public bool Approved { get; }

        /// <summary>
        /// True if the build is only on lists that have expired. It keeps working, but nothing is installed until a new list arrives
        /// </summary>
        public bool Expired { get; }

        /// <summary>
        /// The name of the Package Source whose list approved it
        /// </summary>
        public string Source { get; }

        public string Description { get; }


        public TrakHoundPackageApproval(bool approved, bool expired, string source, string description)
        {
            Approved = approved;
            Expired = expired;
            Source = source;
            Description = description;
        }
    }

    /// <summary>
    /// The attestation lists of the TrakHound Package Managers an Instance uses, at one time, for checking Packages under the RequireApproval Verification Mode
    /// </summary>
    public class TrakHoundPackageApprovals
    {
        public const string NotApprovedDescription = "Not approved by the Package Manager";

        private readonly List<(string Source, TrakHoundPackageAttestationList List)> _lists;
        private readonly string _noListReason;


        /// <param name="lists">The last valid list of each Package Manager Source (expired ones included)</param>
        /// <param name="noListReason">Why there's no list, for display when there are none (ex. "the server key isn't pinned")</param>
        public TrakHoundPackageApprovals(IEnumerable<(string Source, TrakHoundPackageAttestationList List)> lists, string noListReason = null)
        {
            _lists = lists?.Where(o => o.List != null).ToList() ?? new List<(string, TrakHoundPackageAttestationList)>();
            _noListReason = noListReason;
        }

        /// <summary>
        /// True if there's at least one list (expired or not)
        /// </summary>
        public bool HasList => _lists.Count > 0;

        /// <summary>
        /// Why there's no list (ex. "the server key hasn't been read"), when <see cref="HasList"/> is false
        /// </summary>
        public string NoListReason => _lists.Count > 0 ? null : (!string.IsNullOrEmpty(_noListReason) ? _noListReason : "no TrakHound Package Manager has sent one");


        /// <summary>
        /// Check a build against the lists. A build on a list that has expired keeps working, but isn't installed
        /// </summary>
        /// <param name="packageId">The ID from the Package's Manifest</param>
        /// <param name="packageVersion">The Version from the Package's Manifest</param>
        /// <param name="contentHash">The Content Hash of the Package's Manifest</param>
        /// <param name="install">True if the Package is being installed rather than loaded</param>
        /// <param name="now">The time to check the expiry against (UTC)</param>
        public TrakHoundPackageApproval Check(string packageId, string packageVersion, string contentHash, bool install, DateTime now)
        {
            if (string.IsNullOrEmpty(contentHash)) return new TrakHoundPackageApproval(false, false, null, "Not approved : the Package has no Manifest");

            if (_lists.Count < 1)
            {
                return new TrakHoundPackageApproval(false, false, null, $"Not approved : there's no valid approval list ({NoListReason})");
            }

            (string Source, TrakHoundPackageAttestationList List) expiredMatch = default;
            foreach (var entry in _lists)
            {
                if (!entry.List.Packages.Any(o => IsMatch(o, packageId, packageVersion, contentHash))) continue;

                if (TrakHoundPackageAttestationVerifier.ToUtc(entry.List.Expires) > now)
                {
                    return new TrakHoundPackageApproval(true, false, entry.Source, $"Approved by the Package Manager ({entry.Source})");
                }

                if (expiredMatch.List == null) expiredMatch = entry;
            }

            if (expiredMatch.List != null)
            {
                var expires = TrakHoundPackageAttestationVerifier.ToUtc(expiredMatch.List.Expires);
                if (install) return new TrakHoundPackageApproval(false, true, expiredMatch.Source, $"Not installed : the Package Manager's approval list ({expiredMatch.Source}) expired at {expires:u}, so nothing new is installed until a new list arrives");

                return new TrakHoundPackageApproval(true, true, expiredMatch.Source, $"Approved by the Package Manager ({expiredMatch.Source}) on a list that expired at {expires:u}");
            }

            return new TrakHoundPackageApproval(false, false, null, NotApprovedDescription);
        }

        private static bool IsMatch(TrakHoundPackageAttestation attestation, string packageId, string packageVersion, string contentHash)
        {
            if (attestation == null) return false;
            if (attestation.State != TrakHoundPackageAttestation.Approved && attestation.State != TrakHoundPackageAttestation.Deprecated) return false;
            if (!string.Equals(attestation.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)) return false;

            // The Content Hash is of the files, so the ID and Version are matched too (the same files packed as another Version weren't approved)
            if (!string.IsNullOrEmpty(attestation.Id) && !string.Equals(attestation.Id, packageId, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(attestation.Version) && !string.Equals(attestation.Version, packageVersion, StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }
    }
}
