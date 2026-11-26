// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TrakHound.Api;
using TrakHound.Security;

namespace TrakHound.Packages.Dev.Api
{
    /// <summary>
    /// A Package Server for development that keeps Packages as files in the Api's Volume. It serves the same endpoints as TrakHound.Packages.Api,
    /// so the CLI (th packages push) and an Instance's Package Sources use it the same way, but it needs no Tables, Blobs or Key Values service.
    /// It only stores Packages : installing one is still done by an Admin, from the Instance's Packages page
    /// </summary>
    public class Controller : TrakHoundApiController
    {
        /// <summary>
        /// The Header the CLI and TrakHoundPackageClient send their Api Token in
        /// </summary>
        public const string TokenHeader = "X-TrakHound-Api";

        public const string UploadTokenParameter = "uploadToken";
        public const string AllowAnonymousUploadsParameter = "allowAnonymousUploads";
        public const string AllowOverwriteParameter = "allowOverwrite";
        public const string KeepVersionsParameter = "keepVersions";
        public const string MaxUploadSizeParameter = "maxUploadSize";

        public const int DefaultMaxUploadSize = 256; // MB

        private const int _maxManifestRequest = 200;

        private DevPackageStore _store;


        /// <summary>
        /// The store the Packages are kept in
        /// </summary>
        public DevPackageStore Store => _store ??= new DevPackageStore(Volume);


        protected override Task OnInitializeAsync()
        {
            _store = new DevPackageStore(Volume);

            if (!string.IsNullOrEmpty(GetParameter(UploadTokenParameter)))
            {
                Log(TrakHoundLogLevel.Information, $"Uploads need the '{UploadTokenParameter}' (or an Instance API Key)");
            }
            else if (GetBool(AllowAnonymousUploadsParameter))
            {
                Log(TrakHoundLogLevel.Warning, $"Anyone who can reach the Instance can upload and delete Packages ('{AllowAnonymousUploadsParameter}'). Only use this on a development Instance that nothing else can reach");
            }
            else
            {
                Log(TrakHoundLogLevel.Information, $"Uploads need an Instance API Key (X-API-Key), since the '{UploadTokenParameter}' Parameter isn't set");
            }

            return Task.CompletedTask;
        }


        #region "Read"

        /// <summary>
        /// List the Organizations that have Packages
        /// </summary>
        [ApiGet("organizations", Description = "Lists the organizations that have packages.")]
        [TrakHoundPermission("read")]
        public async Task<TrakHoundApiResponse> ListOrganizations()
        {
            var organizations = await Store.GetOrganizations();
            return organizations.Count > 0 ? Ok(organizations) : NotFound("No Organizations Found");
        }

        /// <summary>
        /// Get a Package by its UUID
        /// </summary>
        [ApiGet("{organization}/{uuid}", Description = "Gets the information of a package version by its UUID.")]
        [TrakHoundPermission("read")]
        public async Task<TrakHoundApiResponse> Get(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package UUID, which identifies one version of a package")] string uuid
            )
        {
            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(uuid)) return BadRequest();

            var entry = await Store.Find(organization, uuid);
            return entry != null ? Ok(entry.ToJson()) : NotFound("Package Not Found");
        }

        /// <summary>
        /// Get a specific Version of a Package. A Version with a wildcard (ex. 1.*) returns the highest matching Version
        /// </summary>
        [ApiGet("{organization}/{id}/{version}", Description = "Gets the information of a version of a package. A version with a * wildcard (ex. 1.* or *) returns the highest matching version.")]
        [TrakHoundPermission("read")]
        public async Task<TrakHoundApiResponse> Get(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID, ex. Contoso.Hello.App")] string id,
            [FromRoute(Description = "The version, ex. 1.2.3, 1.* or * (the latest)")] string version
            )
        {
            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(version)) return BadRequest();
            if (!version.Contains('*') && version.ToBaseVersion() == null) return BadRequest("Invalid Package Version");

            var entry = await Store.FindVersion(organization, id, version);
            return entry != null ? Ok(entry.ToJson()) : NotFound("Package Not Found");
        }

        /// <summary>
        /// Get the Latest Version of a Package : the highest Stable Version, or the highest Prerelease if the Package has no Stable Version
        /// </summary>
        [ApiGet("{organization}/{id}/latest", Description = "Gets the information of a package's latest version: the highest stable version, or the highest prerelease if the package has no stable version (in semantic versioning order, so 1.0.0-dev.2 is newer than 1.0.0-dev.1).")]
        [TrakHoundPermission("read")]
        public async Task<TrakHoundApiResponse> GetLatest(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID, ex. Contoso.Hello.App")] string id
            )
        {
            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(id)) return BadRequest();

            var entry = await Store.FindVersion(organization, id, TrakHoundPackage.WilcardVersion);
            return entry != null ? Ok(entry.ToJson()) : NotFound("Package Not Found");
        }

        /// <summary>
        /// List all Versions of a Package, with their Prerelease Labels, in Semantic Versioning order
        /// </summary>
        [ApiGet("{organization}/{id}/versions", Description = "Lists every version of a package, including prereleases (ex. 1.2.4-dev.20261114153000), oldest first in semantic versioning order.")]
        [TrakHoundPermission("read")]
        public async Task<TrakHoundApiResponse> GetVersions(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID, ex. Contoso.Hello.App")] string id
            )
        {
            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(id)) return BadRequest();

            var versions = await Store.GetVersions(organization, id);
            return versions.Count > 0 ? Ok(versions.Select(o => o.Version).ToList()) : NotFound("No Versions found for Package");
        }

        /// <summary>
        /// Get the Latest Version of each Package in an Organization (the most recently built Version of each)
        /// </summary>
        [ApiGet("{organization}/latest", Description = "Lists the latest version of each package in an organization (the most recently built version of each), optionally filtered by q.")]
        [TrakHoundPermission("read")]
        public async Task<TrakHoundApiResponse> QueryLatest(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromQuery(Description = "A package UUID (matched exactly), or the start of a package ID")] string q = null
            )
        {
            if (string.IsNullOrEmpty(organization)) return BadRequest();

            return await Query(organization, null, q);
        }

        /// <summary>
        /// Get the Latest Version of each Package in an Organization filtered by Category
        /// </summary>
        [ApiGet("{organization}/category/{category}", Description = "Lists the latest version of each package of a category in an organization (the most recently built version of each), optionally filtered by q.")]
        [TrakHoundPermission("read")]
        public async Task<TrakHoundApiResponse> QueryByCategory(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package category, ex. api, app, function, service or worker")] string category,
            [FromQuery(Description = "A package UUID (matched exactly), or the start of a package ID")] string q = null
            )
        {
            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(category)) return BadRequest();

            return await Query(organization, category, q);
        }

        private async Task<TrakHoundApiResponse> Query(string organization, string category, string q)
        {
            if (!string.IsNullOrEmpty(q))
            {
                // Test for an Exact Match (UUID)
                var exact = await Store.Find(organization, q);
                if (exact != null) return Ok(new[] { exact.ToJson() });
            }

            var packages = (await Store.GetLatest(organization))
                .Where(o => category == null || string.Equals(o.Package.Category, category, StringComparison.OrdinalIgnoreCase))
                .Where(o => string.IsNullOrEmpty(q) || o.Id.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                .Select(o => o.ToJson())
                .ToList();

            return packages.Count > 0 ? Ok(packages) : NotFound("No Packages Found Matching Query");
        }

        #endregion

        #region "Download"

        /// <summary>
        /// Download a Package file (.thp) by its UUID
        /// </summary>
        [ApiGet("{organization}/{uuid}/download", Description = "Downloads a package file (.thp) by its UUID.")]
        [TrakHoundPermission("read")]
        public async Task<TrakHoundApiResponse> Download(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package UUID, which identifies one version of a package")] string uuid
            )
        {
            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(uuid)) return BadRequest();

            return await Download(await Store.Find(organization, uuid));
        }

        /// <summary>
        /// Download a Package file (.thp) by its ID and Version. A Version with a wildcard (ex. 1.*) downloads the highest matching Version,
        /// which is how Instances download a Dependency
        /// </summary>
        [ApiGet("{organization}/{id}/{version}/download", Description = "Downloads a package file (.thp) by its ID and version. A version with a * wildcard (ex. 1.*) downloads the highest matching version.")]
        [TrakHoundPermission("read")]
        public async Task<TrakHoundApiResponse> Download(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID, ex. Contoso.Hello.App")] string id,
            [FromRoute(Description = "The version, ex. 1.2.3, 1.* or *")] string version
            )
        {
            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(version)) return BadRequest();

            return await Download(await FindPackage(organization, id, version));
        }

        private async Task<TrakHoundApiResponse> Download(DevPackageEntry entry)
        {
            if (entry == null) return NotFound("Package Not Found");

            var stream = await Store.OpenRead(entry);
            if (stream == null) return NotFound("Error Reading Package Contents");

            return File(stream, "application/octet-stream", entry.Id + TrakHoundPackage.FileExtension);
        }

        /// <summary>
        /// Get the Manifest and signature of a Package without downloading the Package. A Version with a wildcard (ex. 1.*) returns the highest matching Version
        /// </summary>
        [ApiGet("{organization}/{id}/{version}/manifest", Description = "Gets the manifest and signature of a package version, without downloading the package, as the exact signed bytes (Base64). Shows who signed the package, but not that the package's files match. A version with a * wildcard returns the highest matching version.")]
        [TrakHoundPermission("read")]
        public async Task<TrakHoundApiResponse> GetManifest(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID, ex. Contoso.Hello.App")] string id,
            [FromRoute(Description = "The version, ex. 1.2.3, 1.* or *")] string version
            )
        {
            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(version)) return BadRequest();

            var manifest = await Store.ReadManifest(await FindPackage(organization, id, version));
            return manifest != null ? Ok(manifest) : NotFound("Package Not Found");
        }

        /// <summary>
        /// Get the Manifests and signatures of multiple Packages. A Package that isn't found is left out of the result
        /// </summary>
        [ApiPost("{organization}/manifests", Description = "Gets the manifests and signatures of up to 200 package versions, without downloading the packages. A package that isn't found is left out of the result.")]
        [TrakHoundPermission("read")]
        public async Task<TrakHoundApiResponse> GetManifests(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromBody(Description = "The UUIDs of the package versions (up to 200)")] IEnumerable<string> packageUuids
            )
        {
            if (string.IsNullOrEmpty(organization) || packageUuids.IsNullOrEmpty()) return BadRequest();

            var uuids = packageUuids.Where(o => !string.IsNullOrEmpty(o)).Distinct().ToList();
            if (uuids.Count > _maxManifestRequest) return BadRequest($"Up to {_maxManifestRequest} Packages can be requested");

            var manifests = new List<TrakHoundPackageManifestJson>();
            foreach (var uuid in uuids)
            {
                var manifest = await Store.ReadManifest(await Store.Find(organization, uuid));
                if (manifest != null) manifests.Add(manifest);
            }

            return Ok(manifests);
        }

        // An exact Version is found by its UUID (as the Packages Api does), so 1.2 finds the Package uploaded as 1.2
        private async Task<DevPackageEntry> FindPackage(string organization, string id, string version)
        {
            if (version.Contains('*')) return await Store.FindVersion(organization, id, version);

            return await Store.Find(organization, TrakHoundPackage.GenerateUuid(id, version)) ?? await Store.FindVersion(organization, id, version);
        }

        #endregion

        #region "Upload / Delete"

        /// <summary>
        /// Upload a Package file (.thp). It's kept in the Volume as packages/{organization}/{id}/{version}.thp, from the information in the file
        /// </summary>
        [ApiPost("{organization}", Description = "Uploads a package file (.thp) to an organization. The package's information is read from the file, and the file is kept in the Api's Volume. Returns the package's information. Needs the Api's uploadToken (in X-TrakHound-Api, which th packages push sends) or an Instance API Key, unless allowAnonymousUploads is set.")]
        [TrakHoundPermission("write")]
        public async Task<TrakHoundApiResponse> Upload(
            [FromRoute(Description = "The organization to upload the package to")] string organization,
            [FromBody(ContentType = "application/octet-stream", Description = "The package file contents (.thp, built by the TrakHound CLI or TrakHoundPackageBuilder)")] Stream packageStream,
            [FromHeader(TokenHeader, Description = "The Api's uploadToken (th packages push sends TRAKHOUND_API_TOKEN, or apiToken in cli.config.yaml)")] string apiToken = null
            )
        {
            var denied = AuthorizeWrite(apiToken, out var caller);
            if (denied.HasValue) return denied.Value;

            if (string.IsNullOrEmpty(organization)) return BadRequest();
            if (!TrakHoundPackage.IsValidPathSegment(organization)) return BadRequest($"Invalid Organization ({organization})");
            if (packageStream == null) return BadRequest("No Package file was sent");

            var maxUploadSize = GetMaxUploadSize();
            byte[] packageBytes;
            try
            {
                packageBytes = await ReadLimited(packageStream, maxUploadSize);
            }
            catch (Exception ex)
            {
                return InternalError($"The Package file could not be read : {ex.Message}");
            }
            if (packageBytes == null) return new TrakHoundApiResponse(413, $"The Package file is larger than {maxUploadSize / (1024 * 1024)} MB (the '{MaxUploadSizeParameter}' Parameter)".ToUtf8Bytes(), "text/plain");

            var result = await Store.Add(organization, packageBytes, GetBool(AllowOverwriteParameter), GetInt(KeepVersionsParameter));
            switch (result.Status)
            {
                case DevPackageAddStatus.Added:
                case DevPackageAddStatus.Replaced:
                    var replaced = result.Status == DevPackageAddStatus.Replaced ? " (replaced)" : "";
                    Log(TrakHoundLogLevel.Information, $"Package Uploaded : {result.Entry.Organization} : {result.Entry.Id} v{result.Entry.Version}{replaced} by {caller}");
                    foreach (var removed in result.Removed)
                    {
                        Log(TrakHoundLogLevel.Information, $"Package Removed : {removed.Organization} : {removed.Id} v{removed.Version} (only the newest {GetInt(KeepVersionsParameter)} Versions are kept)");
                    }
                    return Ok(result.Entry.ToJson());

                case DevPackageAddStatus.Exists:
                    return new TrakHoundApiResponse(409, result.Message.ToUtf8Bytes(), "text/plain");

                case DevPackageAddStatus.Invalid:
                    return BadRequest(result.Message);

                default:
                    Log(TrakHoundLogLevel.Error, $"Package Upload Failed : {result.Message}");
                    return InternalError(result.Message);
            }
        }

        /// <summary>
        /// Delete all Versions of a Package
        /// </summary>
        [ApiDelete("{organization}/{packageId}", Description = "Deletes every version of a package. Needs the Api's uploadToken or an Instance API Key, unless allowAnonymousUploads is set.")]
        [TrakHoundPermission("write")]
        public async Task<TrakHoundApiResponse> DeletePackage(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID")] string packageId,
            [FromHeader(TokenHeader, Description = "The Api's uploadToken")] string apiToken = null
            )
        {
            var denied = AuthorizeWrite(apiToken, out var caller);
            if (denied.HasValue) return denied.Value;

            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(packageId)) return BadRequest();

            var deleted = await Store.DeleteAll(organization, packageId);
            if (deleted < 1) return NotFound("Package Not Found");

            Log(TrakHoundLogLevel.Information, $"Package Deleted : {organization} : {packageId} ({deleted} files) by {caller}");
            return Ok();
        }

        /// <summary>
        /// Delete a single Package Version
        /// </summary>
        [ApiDelete("{organization}", Description = "Deletes a version of a package. Needs the Api's uploadToken or an Instance API Key, unless allowAnonymousUploads is set.")]
        [TrakHoundPermission("write")]
        public async Task<TrakHoundApiResponse> DeletePackageVersion(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromQuery(Description = "The UUID of the package version to delete")] string packageUuid,
            [FromHeader(TokenHeader, Description = "The Api's uploadToken")] string apiToken = null
            )
        {
            var denied = AuthorizeWrite(apiToken, out var caller);
            if (denied.HasValue) return denied.Value;

            if (string.IsNullOrEmpty(organization) || string.IsNullOrEmpty(packageUuid)) return BadRequest();

            var entry = await Store.Find(organization, packageUuid);
            if (entry == null) return NotFound("Package Not Found");

            await Store.Delete(organization, packageUuid);

            Log(TrakHoundLogLevel.Information, $"Package Deleted : {entry.Organization} : {entry.Id} v{entry.Version} by {caller}");
            return Ok();
        }

        #endregion

        #region "Authorization"

        /// <summary>
        /// Uploads and deletes need one of :
        /// <list type="bullet">
        /// <item>The uploadToken Parameter, in the X-TrakHound-Api Header (what th packages push sends)</item>
        /// <item>An Instance Identity (ex. an API Key in X-API-Key). The Instance checks it before the request reaches the Api, and a Security Profile
        /// can limit which Identities can upload with the 'write' Permission</item>
        /// <item>Nothing, when the allowAnonymousUploads Parameter is true and uploadToken isn't set (ex. the Docker image's TRAKHOUND_DEV_PACKAGES).
        /// Installing an uploaded Package still needs an Admin, but an Admin may install one that looks like an update, so it's only for
        /// development Instances that nothing else can reach</item>
        /// </list>
        /// Returns null when allowed, with who made the Request (for the Log)
        /// </summary>
        private TrakHoundApiResponse? AuthorizeWrite(string apiToken, out string caller)
        {
            caller = null;
            var context = TrakHoundSecurityContext.Current;

            // Not called for a Request (ex. the Api's own code or a test), so trusted like any other code in the Instance
            if (context == null)
            {
                caller = "the Instance";
                return null;
            }

            if (context.Identity != null)
            {
                caller = !string.IsNullOrEmpty(context.Identity.Name) ? context.Identity.Name : context.Identity.UserId;
                return null;
            }

            var uploadToken = GetParameter(UploadTokenParameter);
            if (!string.IsNullOrEmpty(uploadToken))
            {
                // A token is always required when it's set, whatever allowAnonymousUploads says
                if (!string.IsNullOrEmpty(apiToken) && FixedTimeEquals(uploadToken, apiToken))
                {
                    caller = "uploadToken";
                    return null;
                }

                return NotAuthorized(!string.IsNullOrEmpty(apiToken)
                    ? "The Api Token isn't this Api's uploadToken"
                    : $"Send the Api's uploadToken in the {TokenHeader} Header (th packages push sends TRAKHOUND_API_TOKEN), or an Instance API Key in X-API-Key");
            }

            if (GetBool(AllowAnonymousUploadsParameter))
            {
                caller = "anonymous";
                return null;
            }

            return NotAuthorized($"Uploads need a credential : send an Instance API Key in X-API-Key, or set the Api's '{UploadTokenParameter}' Parameter and send it in the {TokenHeader} Header (th packages push sends TRAKHOUND_API_TOKEN). For a development Instance that nothing else can reach, set '{AllowAnonymousUploadsParameter}' to true instead");
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
        }

        #endregion

        #region "Parameters"

        private string GetParameter(string name) => Configuration?.GetParameter(name);

        private bool GetBool(string name)
        {
            var value = GetParameter(name)?.Trim();
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) || value == "1";
        }

        private int GetInt(string name)
        {
            return int.TryParse(GetParameter(name)?.Trim(), out var value) && value > 0 ? value : 0;
        }

        private long GetMaxUploadSize()
        {
            var megabytes = GetInt(MaxUploadSizeParameter);
            return (megabytes > 0 ? megabytes : DefaultMaxUploadSize) * 1024L * 1024L;
        }

        // Null if the Stream is longer than the limit, so a large upload is stopped instead of being read into memory
        private static async Task<byte[]> ReadLimited(Stream stream, long limit)
        {
            using (var memory = new MemoryStream())
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    if (memory.Length + read > limit) return null;
                    memory.Write(buffer, 0, read);
                }

                return memory.ToArray();
            }
        }

        #endregion
    }
}
