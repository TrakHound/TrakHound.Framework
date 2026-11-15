// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrakHound.Packages
{
    public enum TrakHoundPackageVerificationStatus
    {
        /// <summary>
        /// The files match the Manifest
        /// </summary>
        Valid,

        /// <summary>
        /// The Package has no Manifest, so its files can't be checked
        /// </summary>
        NoManifest,

        /// <summary>
        /// The files don't match the Manifest, the signature of a Trusted Publisher doesn't match, or the Package can't be read
        /// </summary>
        Invalid
    }

    public enum TrakHoundPackageSignatureStatus
    {
        /// <summary>
        /// The Package has no signature
        /// </summary>
        Unsigned,

        /// <summary>
        /// The Package is signed by a key that isn't trusted, so the signature can't be checked. A bad signature from a Trusted Publisher, or one for a Package ID it isn't allowed to sign, also makes the Package Invalid
        /// </summary>
        Untrusted,

        /// <summary>
        /// The Package has a good signature from a Trusted Publisher
        /// </summary>
        Trusted
    }

    /// <summary>
    /// The result of checking a Package's files against its Manifest, and its Manifest against its signature
    /// </summary>
    public class TrakHoundPackageVerificationResult
    {
        public TrakHoundPackageVerificationStatus Status => Manifest == null && Errors.Count == 0
            ? TrakHoundPackageVerificationStatus.NoManifest
            : Errors.Count == 0 && Changed.Count == 0 && Missing.Count == 0 && Added.Count == 0
                ? TrakHoundPackageVerificationStatus.Valid
                : TrakHoundPackageVerificationStatus.Invalid;

        public TrakHoundPackageSignatureStatus SignatureStatus { get; internal set; }

        /// <summary>
        /// ID of the key the Package says signed it. Unproven unless the <see cref="SignatureStatus"/> is Trusted
        /// </summary>
        public string SignatureKeyId { get; internal set; }

        /// <summary>
        /// The Trusted Publisher that signed the Package. Null unless the <see cref="SignatureStatus"/> is Trusted
        /// </summary>
        public TrakHoundTrustedPublisher Publisher { get; internal set; }

        /// <summary>
        /// The Manifest that was read. Null if the Package has none or it can't be read
        /// </summary>
        public TrakHoundPackageManifest Manifest { get; internal set; }

        /// <summary>
        /// Whether a TrakHound Package Manager approved the build. Only set under the RequireApproval Mode, for a Package whose files match and that a Trusted Publisher signed
        /// </summary>
        public TrakHoundPackageApproval Approval { get; internal set; }

        /// <summary>
        /// Files whose contents don't match the Manifest
        /// </summary>
        public List<string> Changed { get; } = new List<string>();

        /// <summary>
        /// Files in the Manifest that aren't in the Package
        /// </summary>
        public List<string> Missing { get; } = new List<string>();

        /// <summary>
        /// Files in the Package that aren't in the Manifest
        /// </summary>
        public List<string> Added { get; } = new List<string>();

        /// <summary>
        /// Other problems (ex. duplicate entries, or a package.json that doesn't match the Manifest)
        /// </summary>
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>
    /// The result of checking a Manifest and its signature without the Package's files
    /// </summary>
    public enum TrakHoundPackageManifestState
    {
        /// <summary>
        /// The Package has no Manifest
        /// </summary>
        NoManifest,

        /// <summary>
        /// The Manifest has no signature
        /// </summary>
        Unsigned,

        /// <summary>
        /// The Manifest is signed by a key that isn't trusted, so the signature can't be checked
        /// </summary>
        Untrusted,

        /// <summary>
        /// The Manifest has a good signature from a Trusted Publisher. Nothing is known about the files until they're checked
        /// </summary>
        Trusted,

        /// <summary>
        /// The Manifest can't be read, the signature of a Trusted Publisher doesn't match, or the Publisher isn't allowed to sign this Package
        /// </summary>
        Invalid
    }

    /// <summary>
    /// The result of <see cref="TrakHoundPackageVerifier.VerifyManifest"/>. It has its own State so that it can't be taken for a check of the files, which is what makes a Package Verified
    /// </summary>
    public class TrakHoundPackageManifestCheck
    {
        public TrakHoundPackageManifestState State { get; }

        /// <summary>
        /// The Manifest, if it could be read. Its contents are only proven if the State is Trusted
        /// </summary>
        public TrakHoundPackageManifest Manifest { get; }

        /// <summary>
        /// The Trusted Publisher that signed the Manifest
        /// </summary>
        public TrakHoundTrustedPublisher Publisher { get; }

        /// <summary>
        /// The ID of the key the Manifest says signed it. Only proven if the State is Trusted
        /// </summary>
        public string KeyId { get; }

        public IReadOnlyList<string> Errors { get; }


        internal TrakHoundPackageManifestCheck(TrakHoundPackageVerificationResult result)
        {
            Manifest = result.Manifest;
            Publisher = result.Publisher;
            KeyId = result.SignatureKeyId;
            Errors = result.Errors;

            if (result.Errors.Count > 0) State = TrakHoundPackageManifestState.Invalid;
            else if (result.Manifest == null) State = TrakHoundPackageManifestState.NoManifest;
            else if (result.SignatureStatus == TrakHoundPackageSignatureStatus.Trusted) State = TrakHoundPackageManifestState.Trusted;
            else if (result.SignatureStatus == TrakHoundPackageSignatureStatus.Untrusted) State = TrakHoundPackageManifestState.Untrusted;
            else State = TrakHoundPackageManifestState.Unsigned;
        }
    }

    /// <summary>
    /// Checks a Package's files against its Manifest, and its Manifest against its signature.
    /// </summary>
    /// <remarks>
    /// Without a Trusted signature this only finds files that changed after packing: someone can also rewrite the Manifest.
    /// </remarks>
    public static class TrakHoundPackageVerifier
    {
        /// <summary>
        /// Check the contents of a package (.thp) file without extracting it
        /// </summary>
        /// <param name="packageBytes">Contents of the package (.thp) file</param>
        /// <param name="trustedPublishers">The publishers to trust. If null, no signature is trusted</param>
        public static TrakHoundPackageVerificationResult VerifyArchive(byte[] packageBytes, TrakHoundTrustedPublishers trustedPublishers = null)
        {
            var result = new TrakHoundPackageVerificationResult();

            try
            {
                var information = TrakHoundPackage.ReadInformationFromPackage(packageBytes);
                if (information == null)
                {
                    result.Errors.Add("The Package Information can't be read");
                    return result;
                }

                var files = new Dictionary<string, TrakHoundPackageManifestFile>(StringComparer.OrdinalIgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byte[] manifestBytes = null;
                byte[] signatureBytes = null;

                using (var inputStream = new MemoryStream(packageBytes))
                using (var archive = new ZipArchive(inputStream, ZipArchiveMode.Read))
                {
                    var rootPath = Path.GetTempPath();

                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue; // Directory Entry

                        var path = TrakHoundPackageManifest.NormalizePath(entry.FullName);

                        // Install would reject this entry, or extract it over another one on a case-insensitive file system
                        if (TrakHoundPackage.GetEntryPath(rootPath, entry.FullName) == null) result.Errors.Add($"Invalid entry : {entry.FullName}");
                        if (!seen.Add(path)) result.Errors.Add($"Duplicate entry : {path}");

                        if (entry.FullName == TrakHoundPackage.InformationFilename) continue;

                        using (var entryStream = entry.Open())
                        {
                            if (entry.FullName == TrakHoundPackage.ManifestFilename) manifestBytes = ReadAll(entryStream);
                            else if (entry.FullName == TrakHoundPackage.SignatureFilename) signatureBytes = ReadAll(entryStream);
                            else files[path] = new TrakHoundPackageManifestFile { Path = path, Sha256 = TrakHoundPackageManifest.GetHash(entryStream), Size = entry.Length };
                        }
                    }
                }

                Compare(result, information, manifestBytes, signatureBytes, files, trustedPublishers);
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Check the files of an installed Package version
        /// </summary>
        /// <param name="packageDirectory">Package version directory containing the package.json file</param>
        /// <param name="trustedPublishers">The publishers to trust. If null, no signature is trusted</param>
        public static TrakHoundPackageVerificationResult VerifyDirectory(string packageDirectory, TrakHoundTrustedPublishers trustedPublishers = null)
        {
            var result = new TrakHoundPackageVerificationResult();

            try
            {
                var information = TrakHoundPackage.ReadInformation(packageDirectory);
                if (information == null)
                {
                    result.Errors.Add("The Package Information can't be read");
                    return result;
                }

                var manifestPath = Path.Combine(packageDirectory, TrakHoundPackage.ManifestFilename);
                var manifestBytes = File.Exists(manifestPath) ? File.ReadAllBytes(manifestPath) : null;

                var signaturePath = Path.Combine(packageDirectory, TrakHoundPackage.SignatureFilename);
                var signatureBytes = File.Exists(signaturePath) ? File.ReadAllBytes(signaturePath) : null;

                var files = new Dictionary<string, TrakHoundPackageManifestFile>(StringComparer.OrdinalIgnoreCase);

                // There's nothing to compare the files with, and reading them can take minutes on an Instance with many Packages
                if (manifestBytes == null)
                {
                    Compare(result, information, manifestBytes, signatureBytes, files, trustedPublishers);
                    return result;
                }

                foreach (var file in Directory.GetFiles(packageDirectory, "*", SearchOption.AllDirectories))
                {
                    var path = TrakHoundPackageManifest.NormalizePath(Path.GetRelativePath(packageDirectory, file));
                    if (path == TrakHoundPackage.InformationFilename || path == TrakHoundPackage.ManifestFilename || path == TrakHoundPackage.SignatureFilename) continue;

                    using (var stream = File.OpenRead(file))
                    {
                        files[path] = new TrakHoundPackageManifestFile { Path = path, Sha256 = TrakHoundPackageManifest.GetHash(stream), Size = stream.Length };
                    }
                }

                Compare(result, information, manifestBytes, signatureBytes, files, trustedPublishers);
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
            }

            return result;
        }


        /// <summary>
        /// Check a Manifest and its signature without the Package's files, ex. for a Package on a Package Source that hasn't been downloaded.
        /// This shows who signed the Manifest, not that any files match it
        /// </summary>
        /// <param name="manifestBytes">The exact bytes of the Manifest. Null if the Package doesn't have one</param>
        /// <param name="signatureBytes">The exact bytes of the signature. Null if the Package isn't signed</param>
        /// <param name="trustedPublishers">The publishers to trust. If null, no signature is trusted</param>
        public static TrakHoundPackageManifestCheck VerifyManifest(byte[] manifestBytes, byte[] signatureBytes, TrakHoundTrustedPublishers trustedPublishers = null)
        {
            var result = new TrakHoundPackageVerificationResult();

            try
            {
                ReadManifest(result, manifestBytes, signatureBytes, trustedPublishers);
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
            }

            return new TrakHoundPackageManifestCheck(result);
        }

        /// <summary>
        /// Check a Manifest from a Package Source against what the Source lists for that Package, so that a genuine Manifest of another Version or build isn't accepted for it
        /// </summary>
        /// <param name="manifest">The Manifest and signature from the Package Source</param>
        /// <param name="package">The Package as listed by the Package Source</param>
        /// <param name="trustedPublishers">The publishers to trust. If null, no signature is trusted</param>
        public static TrakHoundPackageManifestCheck VerifyListedManifest(TrakHoundPackageManifestJson manifest, TrakHoundPackage package, TrakHoundTrustedPublishers trustedPublishers = null)
        {
            var result = new TrakHoundPackageVerificationResult();

            try
            {
                if (!string.IsNullOrEmpty(manifest.Error))
                {
                    result.Errors.Add(manifest.Error);
                }
                else if (ReadManifest(result, manifest.Manifest, manifest.Signature, trustedPublishers) is TrakHoundPackageManifest read)
                {
                    CompareIdentity(result, "Category", package.Category, read.Category);
                    CompareIdentity(result, "ID", package.Id, read.Id);
                    CompareIdentity(result, "Version", package.Version, read.Version);
                    if (package.BuildDate.ToUniversalTime() != read.BuildDate.ToUniversalTime()) result.Errors.Add("The Build Date doesn't match the Manifest");

                    // A Source that doesn't report the Content Hash can't be compared with it
                    if (!string.IsNullOrEmpty(package.ContentHash) && !string.Equals(package.ContentHash, read.GetContentHash(), StringComparison.OrdinalIgnoreCase)) result.Errors.Add("The Content Hash doesn't match the Manifest");
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
            }

            return new TrakHoundPackageManifestCheck(result);
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (var outputStream = new MemoryStream())
            {
                stream.CopyTo(outputStream);
                return outputStream.ToArray();
            }
        }

        private static void Compare(TrakHoundPackageVerificationResult result, TrakHoundPackage information, byte[] manifestBytes, byte[] signatureBytes, Dictionary<string, TrakHoundPackageManifestFile> files, TrakHoundTrustedPublishers trustedPublishers)
        {
            var manifest = ReadManifest(result, manifestBytes, signatureBytes, trustedPublishers);
            if (manifest == null) return;

            // The package.json is what gets read and used, but only the Manifest is signed, so they must agree
            CompareIdentity(result, "Category", information.Category, manifest.Category);
            CompareIdentity(result, "ID", information.Id, manifest.Id);
            CompareIdentity(result, "Version", information.Version, manifest.Version);
            if (information.BuildDate.ToUniversalTime() != manifest.BuildDate.ToUniversalTime()) result.Errors.Add("The Build Date doesn't match the Manifest");
            if (!string.Equals(information.ContentHash, manifest.GetContentHash(), StringComparison.OrdinalIgnoreCase)) result.Errors.Add("The Content Hash doesn't match the Manifest");

            var informationJson = new TrakHoundPackageJson(information);
            if (!JsonEquals(informationJson.Metadata, manifest.Metadata)) result.Errors.Add("The Metadata doesn't match the Manifest");
            if (!JsonEquals(informationJson.Dependencies, manifest.Dependencies)) result.Errors.Add("The Dependencies don't match the Manifest");

            var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.Files)
            {
                if (!listed.Add(file.Path)) result.Errors.Add($"Duplicate Manifest file : {file.Path}");

                if (!files.TryGetValue(file.Path, out var actual)) result.Missing.Add(file.Path);
                else if (actual.Size != file.Size || !string.Equals(actual.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase)) result.Changed.Add(file.Path);
            }

            result.Added.AddRange(files.Keys.Where(o => !listed.Contains(o)).OrderBy(o => o, StringComparer.Ordinal));
        }

        /// <summary>
        /// Read a Manifest and check its signature. Returns null if there's no Manifest or it can't be used
        /// </summary>
        private static TrakHoundPackageManifest ReadManifest(TrakHoundPackageVerificationResult result, byte[] manifestBytes, byte[] signatureBytes, TrakHoundTrustedPublishers trustedPublishers)
        {
            if (manifestBytes == null)
            {
                // A signature signs the Manifest, so removing the Manifest must not make a signed Package look merely unsigned
                if (signatureBytes != null) result.Errors.Add("The Package is signed but has no Manifest");
                return null;
            }

            TrakHoundPackageManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<TrakHoundPackageManifest>(manifestBytes);
            }
            catch (JsonException ex)
            {
                result.Errors.Add($"The Manifest can't be read : {ex.Message}");
                return null;
            }

            if (manifest == null || manifest.Files == null)
            {
                result.Errors.Add("The Manifest can't be read");
                return null;
            }

            if (manifest.FormatVersion != TrakHoundPackageManifest.CurrentFormatVersion)
            {
                result.Errors.Add($"Unsupported Manifest format : {manifest.FormatVersion}");
                return null;
            }

            result.Manifest = manifest;

            CheckSignature(result, manifest, manifestBytes, signatureBytes, trustedPublishers);

            return manifest;
        }

        private static void CheckSignature(TrakHoundPackageVerificationResult result, TrakHoundPackageManifest manifest, byte[] manifestBytes, byte[] signatureBytes, TrakHoundTrustedPublishers trustedPublishers)
        {
            if (signatureBytes == null)
            {
                result.SignatureStatus = TrakHoundPackageSignatureStatus.Unsigned;
                return;
            }

            result.SignatureStatus = TrakHoundPackageSignatureStatus.Untrusted;

            TrakHoundPackageSignature signature;
            try
            {
                signature = JsonSerializer.Deserialize<TrakHoundPackageSignature>(signatureBytes);
            }
            catch (JsonException ex)
            {
                result.Errors.Add($"The Signature can't be read : {ex.Message}");
                return;
            }

            if (signature?.Algorithm != TrakHoundPackageSignature.EcdsaP256Sha256 || string.IsNullOrEmpty(signature.Signature))
            {
                result.Errors.Add($"Unsupported Signature : {signature?.Algorithm}");
                return;
            }

            result.SignatureKeyId = signature.KeyId;

            var publisher = trustedPublishers?.Find(signature.KeyId);
            if (publisher == null) return;

            // A bad signature from a known key means the Manifest was changed after signing
            byte[] signatureValue;
            try
            {
                signatureValue = Convert.FromBase64String(signature.Signature);
            }
            catch (FormatException)
            {
                result.Errors.Add("The Signature can't be read");
                return;
            }

            if (!TrakHoundPackageSigner.Verify(Convert.FromBase64String(publisher.PublicKey), manifestBytes, signatureValue))
            {
                result.Errors.Add($"The Signature doesn't match the Manifest (key {signature.KeyId})");
                return;
            }

            if (!publisher.AllowsPackage(manifest.Id))
            {
                result.Errors.Add($"Publisher '{publisher.Name}' isn't allowed to sign Package '{manifest.Id}'");
                return;
            }

            result.SignatureStatus = TrakHoundPackageSignatureStatus.Trusted;
            result.Publisher = publisher;
        }

        private static void CompareIdentity(TrakHoundPackageVerificationResult result, string name, string information, string manifest)
        {
            if (!string.Equals(information ?? "", manifest ?? "", StringComparison.Ordinal)) result.Errors.Add($"The {name} doesn't match the Manifest : '{information}' != '{manifest}'");
        }

        /// <summary>
        /// Compare the JSON of two values (the order of properties doesn't matter, and null equals empty)
        /// </summary>
        private static bool JsonEquals<T>(T a, T b)
        {
            return JsonNode.DeepEquals(ToComparableNode(a), ToComparableNode(b));
        }

        private static JsonNode ToComparableNode<T>(T value)
        {
            var node = JsonSerializer.SerializeToNode(value);
            if (node is JsonObject jsonObject && jsonObject.Count == 0) return null;
            if (node is JsonArray jsonArray && jsonArray.Count == 0) return null;
            return node;
        }
    }
}
