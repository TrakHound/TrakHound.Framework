using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Clients;

namespace TrakHound.Packages.Api
{
    /// <summary>
    /// Reads Package Manifests from the stored Package files, and caches them so that a Package doesn't need to be downloaded from the Blobs service for each request.
    /// The Package file stays the source of truth, so Packages already stored need no migration and an entry can always be read again
    /// </summary>
    public class PackageManifestStore
    {
        private const string _blobCollection = "trakhound.packages";
        private const string _cacheCollection = "trakhound.packages";

        // A package with many files has a large Manifest, which is read from the Package file each time rather than stored in the Key Value service
        private const int _maxCachedSize = 1024 * 1024;

        private const int _maxConcurrentReads = 4;

        private readonly ITrakHoundClient _client;


        public PackageManifestStore(ITrakHoundClient client)
        {
            _client = client;
        }


        public static string GetCacheKey(string organization, string packageUuid) => $"{organization}:{packageUuid}:manifest";

        public static string GetBlobKey(string organization, string packageUuid) => $"{organization}:{packageUuid}".ToSHA256Hash();


        /// <summary>
        /// Read the Manifest of a Package
        /// </summary>
        /// <returns>The Manifest, or null if the Package file isn't stored</returns>
        public async Task<TrakHoundPackageManifestJson> Read(string organization, string packageUuid)
        {
            return (await Read(organization, new[] { packageUuid })).FirstOrDefault();
        }

        /// <summary>
        /// Read the Manifests of multiple Packages. A Package whose file isn't stored or can't be downloaded is left out, so that it doesn't fail the others
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackageManifestJson>> Read(string organization, IEnumerable<string> packageUuids)
        {
            var uuids = packageUuids.Where(o => !string.IsNullOrEmpty(o)).Distinct().ToList();
            if (uuids.Count < 1) return Enumerable.Empty<TrakHoundPackageManifestJson>();

            var results = new ConcurrentDictionary<string, TrakHoundPackageManifestJson>();

            var cached = (await _client.Services.KeyValues.Read(_cacheCollection, uuids.Select(o => GetCacheKey(organization, o)))).Result?.Values;
            foreach (var uuid in uuids)
            {
                var value = cached?.GetValueOrDefault(GetCacheKey(organization, uuid));
                var manifest = value != null ? Json.Convert<TrakHoundPackageManifestJson>(value) : null;
                if (manifest != null) results[uuid] = manifest;
            }

            await Parallel.ForEachAsync(uuids.Where(o => !results.ContainsKey(o)), new ParallelOptions { MaxDegreeOfParallelism = _maxConcurrentReads }, async (uuid, cancellationToken) =>
            {
                try
                {
                    var manifest = await ReadFromPackage(organization, uuid);
                    if (manifest != null) results[uuid] = manifest;
                }
                catch { } // Not cached, so it's read again next time
            });

            return uuids.Where(results.ContainsKey).Select(o => results[o]).ToList();
        }

        /// <summary>
        /// Remove a cached Manifest. Call this after the Package file changes, not before, or a read in between would cache the old Manifest again
        /// </summary>
        public async Task<bool> Remove(string organization, string packageUuid)
        {
            return (await _client.Services.KeyValues.Delete(_cacheCollection, new[] { GetCacheKey(organization, packageUuid) })).Success;
        }


        private async Task<TrakHoundPackageManifestJson> ReadFromPackage(string organization, string packageUuid)
        {
            var response = await _client.Services.Blobs.Download(_blobCollection, GetBlobKey(organization, packageUuid));
            if (!response.Success || response.Result == null) return null;

            var manifest = new TrakHoundPackageManifestJson { Uuid = packageUuid };

            using (var stream = response.Result)
            {
                try
                {
                    (manifest.Manifest, manifest.Signature) = TrakHoundPackageManifest.ReadFromArchive(stream);
                }
                catch (InvalidDataException ex)
                {
                    // Cached as well: this only changes when the Package is uploaded again
                    manifest.Error = ex.Message;
                }
            }

            if ((manifest.Manifest?.Length ?? 0) <= _maxCachedSize)
            {
                await _client.Services.KeyValues.Publish(_cacheCollection, GetCacheKey(organization, packageUuid), manifest.ToJson());
            }

            return manifest;
        }
    }
}
