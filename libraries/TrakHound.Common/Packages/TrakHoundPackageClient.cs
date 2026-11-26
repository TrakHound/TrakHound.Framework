// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Http;

namespace TrakHound.Packages
{
    /// <summary>
    /// A client for the HTTP API of a TrakHound Package Server (ex. the Packages Api, or a TrakHound Package Manager):
    /// browse, download and upload Packages. Package Requests use the Client's Organization unless specified
    /// </summary>
    public class TrakHoundPackageClient
    {
        internal const string ApiHeaderKey = "X-TrakHound-Api";

        private const int _maxManifestRequest = 200; // The most Packages a server returns Manifests for in one request

        private readonly string _baseUrl;
        private string _apiToken;


        /// <summary>
        /// The base URL of the Package Server
        /// </summary>
        public string BaseUrl => _baseUrl;

        /// <summary>
        /// The Organization used for Requests (most Requests return null / false if it is not set)
        /// </summary>
        public string Organization { get; set; }

        private Dictionary<string, string> ApiTokenHeader
        {
            get
            {
                var x = new Dictionary<string, string>();
                x[ApiHeaderKey] = _apiToken;
                return x;
            }
        }


        /// <summary>
        /// Creates a client for the Package Server at the base URL. Note: sets the global TrakHoundHttpRequests.Timeout to 500000ms
        /// </summary>
        public TrakHoundPackageClient(string baseUrl, string organization = null)
        {
            TrakHoundHttpRequests.Timeout = 500000;

            _baseUrl = baseUrl;
            Organization = organization;
        }


        /// <summary>
        /// Sets the API Token sent (in the X-TrakHound-Api header) with each Request
        /// </summary>
        public void SetToken(string token) => _apiToken = token;


        /// <summary>
        /// Gets the Package with the specified Uuid
        /// </summary>
        public async Task<TrakHoundPackage> Get(string packageUuid)
        {
            if (!string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(packageUuid))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, packageUuid);

                var response = await TrakHoundHttpRequests.Get<TrakHoundPackageJson>(url, ApiTokenHeader);
                return response?.ToPackage();
            }

            return null;
        }

        /// <summary>
        /// Gets the Package with the specified Id and Version
        /// </summary>
        public async Task<TrakHoundPackage> Get(string packageId, string packageVersion)
        {
            if (!string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(packageVersion))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, packageId);
                url = Url.Combine(url, packageVersion);

                var response = await TrakHoundHttpRequests.Get<TrakHoundPackageJson>(url, ApiTokenHeader);
                return response?.ToPackage();
            }

            return null;
        }

        /// <summary>
        /// Gets the latest Version of the Package with the specified Id
        /// </summary>
        public async Task<TrakHoundPackage> GetLatest(string packageId)
        {
            if (!string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(packageId))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, packageId);
                url = Url.Combine(url, "latest");

                var response = await TrakHoundHttpRequests.Get<TrakHoundPackageJson>(url, ApiTokenHeader);
                return response?.ToPackage();
            }

            return null;
        }

        /// <summary>
        /// Gets the available Versions of the Package with the specified Id
        /// </summary>
        public async Task<IEnumerable<string>> GetVersions(string packageId)
        {
            if (!string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(packageId))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, packageId);
                url = Url.Combine(url, "versions");

                return await TrakHoundHttpRequests.Get<IEnumerable<string>>(url, ApiTokenHeader);
            }

            return null;
        }

        /// <summary>
        /// Gets the available Versions of the Package with the specified Id, telling a Package with no Versions apart from a failed request.
        /// Success with no Versions when the Package has none (404). Not successful (with the error in Message) when the request fails
        /// </summary>
        public async Task<(bool Success, IEnumerable<string> Versions, string Message)> TryGetVersions(string packageId)
        {
            if (string.IsNullOrEmpty(Organization)) return (false, null, "No Organization");
            if (string.IsNullOrEmpty(packageId)) return (false, null, "No Package ID");

            var url = Url.Combine(BaseUrl, "packages");
            url = Url.Combine(url, Organization);
            url = Url.Combine(url, packageId);
            url = Url.Combine(url, "versions");

            var response = await TrakHoundHttpRequests.GetResponse(url, ApiTokenHeader);
            switch (response.StatusCode)
            {
                case 200:
                    string body = null;
                    if (response.Content != null)
                    {
                        using (var reader = new StreamReader(response.Content)) body = await reader.ReadToEndAsync();
                    }

                    var versions = Json.Convert<List<string>>(body);
                    return versions != null ? (true, versions, null) : (false, null, "Invalid Response");

                case 404:
                    return (true, Enumerable.Empty<string>(), null);

                default:
                    return (false, null, !string.IsNullOrEmpty(response.Message) ? response.Message : $"Status {response.StatusCode}");
            }
        }

        /// <summary>
        /// Gets the Packages in the Category, optionally filtered by a search query
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackage>> Query(string category, string query = null)
        {
            if (!string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(category))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, "category");
                url = Url.Combine(url, category);
                url = Url.AddQueryParameter(url, "q", query);

                var responses = await TrakHoundHttpRequests.Get<IEnumerable<TrakHoundPackageJson>>(url, ApiTokenHeader);
                return responses?.Select(o => o.ToPackage());
            }

            return null;
        }

        /// <summary>
        /// Gets the Packages that match the Package Queries
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackage>> Query(IEnumerable<TrakHoundPackageQuery> queries)
        {
            if (!string.IsNullOrEmpty(Organization) && !queries.IsNullOrEmpty())
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);

                var responses = await TrakHoundHttpRequests.Post<IEnumerable<TrakHoundPackageJson>>(url, queries, headers: ApiTokenHeader);
                return responses?.Select(o => o.ToPackage());
            }

            return null;
        }

        /// <summary>
        /// Gets the latest Version of each Package in the Organization
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackage>> QueryLatestByOrganization()
        {
            if (!string.IsNullOrEmpty(Organization))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, "latest");

                var responses = await TrakHoundHttpRequests.Get<IEnumerable<TrakHoundPackageJson>>(url, ApiTokenHeader);
                return responses?.Select(o => o.ToPackage());
            }

            return null;
        }

        /// <summary>
        /// Gets the latest Version of each Package in the specified Organization
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackage>> QueryLatestByOrganization(string organization)
        {
            if (!string.IsNullOrEmpty(organization))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, organization);
                url = Url.Combine(url, "latest");

                var responses = await TrakHoundHttpRequests.Get<IEnumerable<TrakHoundPackageJson>>(url, ApiTokenHeader);
                return responses?.Select(o => o.ToPackage());
            }

            return null;
        }

        /// <summary>
        /// Gets the Packages that are updates for the Package Queries
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackage>> QueryUpdates(IEnumerable<TrakHoundPackageQuery> queries)
        {
            if (!string.IsNullOrEmpty(Organization) && !queries.IsNullOrEmpty())
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, "updates");

                var responses = await TrakHoundHttpRequests.Post<IEnumerable<TrakHoundPackageJson>>(url, queries, headers: ApiTokenHeader);
                return responses?.Select(o => o.ToPackage());
            }

            return null;
        }

        /// <summary>
        /// Gets the Manifest and signature of a Package without downloading it (null if the Package isn't found or the server doesn't provide Manifests)
        /// </summary>
        public async Task<TrakHoundPackageManifestJson> GetManifest(string packageId, string packageVersion)
        {
            if (!string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(packageVersion))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, packageId);
                url = Url.Combine(url, packageVersion);
                url = Url.Combine(url, "manifest");

                return await TrakHoundHttpRequests.Get<TrakHoundPackageManifestJson>(url, ApiTokenHeader);
            }

            return null;
        }

        /// <summary>
        /// Gets the Manifests and signatures of multiple Packages without downloading them. A Package that isn't found is left out.
        /// Null if the server doesn't provide Manifests
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackageManifestJson>> GetManifests(IEnumerable<string> packageUuids)
        {
            if (!string.IsNullOrEmpty(Organization) && !packageUuids.IsNullOrEmpty())
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, "manifests");

                List<TrakHoundPackageManifestJson> manifests = null;
                foreach (var chunk in packageUuids.Distinct().Chunk(_maxManifestRequest))
                {
                    var responses = await TrakHoundHttpRequests.Post<IEnumerable<TrakHoundPackageManifestJson>>(url, chunk, headers: ApiTokenHeader);
                    if (responses != null) (manifests ??= new List<TrakHoundPackageManifestJson>()).AddRange(responses);
                }

                return manifests;
            }

            return null;
        }

        /// <summary>
        /// Gets the URL to download the Package with the specified Uuid (null if the Organization or Uuid is not set)
        /// </summary>
        public string GetDownloadUrl(string packageUuid)
        {
            if (!string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(packageUuid))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, packageUuid);
                url = Url.Combine(url, "download");
                return url;
            }

            return null;
        }

        /// <summary>
        /// Downloads the Package with the specified Uuid (null on error)
        /// </summary>
        public async Task<byte[]> Download(string packageUuid)
        {
            if (!string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(packageUuid))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, packageUuid);
                url = Url.Combine(url, "download");

                try
                {
                    await using var stream = await TrakHoundHttpRequests.GetStream(url, headers: ApiTokenHeader);
                    if (stream != null)
                    {
                        using var memoryStream = new MemoryStream();
                        await stream.CopyToAsync(memoryStream);
                        return memoryStream.ToArray();
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Downloads the Package with the specified Id and Version (null on error)
        /// </summary>
        public async Task<byte[]> Download(string packageId, string packageVersion)
        {
            if (!string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(packageVersion))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, packageId);
                url = Url.Combine(url, packageVersion);
                url = Url.Combine(url, "download");

                try
                {
                    await using var stream = await TrakHoundHttpRequests.GetStream(url, headers: ApiTokenHeader);
                    if (stream != null)
                    {
                        using var memoryStream = new MemoryStream();
                        await stream.CopyToAsync(memoryStream);
                        return memoryStream.ToArray();
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Downloads the latest Version of the Package with the specified Id (null on error)
        /// </summary>
        public async Task<byte[]> DownloadLatest(string packageId)
        {
            if (!string.IsNullOrEmpty(Organization) && !string.IsNullOrEmpty(packageId))
            {
                // The server has no "{id}/latest/download" route, so resolve the latest Version first and download it by Uuid
                try
                {
                    var package = await GetLatest(packageId);
                    if (package != null) return await Download(package.Uuid);
                }
                catch { }
            }

            return null;
        }


        /// <summary>
        /// Downloads a single archive containing the Packages that match the Package Queries (null on a non-success response)
        /// </summary>
        public async Task<byte[]> DownloadArchive(IEnumerable<TrakHoundPackageQuery> queries)
        {
            if (!string.IsNullOrEmpty(Organization) && !queries.IsNullOrEmpty())
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);
                url = Url.Combine(url, "download");

                var response = await TrakHoundHttpRequests.PostResponse(url, queries, headers: ApiTokenHeader);
                if (response.StatusCode >= 200 && response.StatusCode < 300)
                {
                    return response.GetContentBytes();
                }
            }

            return null;
        }


        /// <summary>
        /// Uploads a Package file and returns the Package created on the server
        /// </summary>
        public async Task<TrakHoundPackage> Upload(byte[] packageBytes)
        {
            if (!string.IsNullOrEmpty(Organization) && !packageBytes.IsNullOrEmpty())
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);

                var packageJson = await TrakHoundHttpRequests.Post<TrakHoundPackageJson>(url, packageBytes, "application/octet-stream", headers:ApiTokenHeader);
                return packageJson?.ToPackage();
            }

            return null;
        }

        /// <summary>
        /// Uploads a Package file. Returns the Status Code (0 if the server couldn't be reached), the Package created on the server (null unless
        /// successful), and the server's error when it isn't successful (ex. why an upload was refused), so it can be shown to the user
        /// </summary>
        public async Task<(int StatusCode, TrakHoundPackage Package, string Message)> UploadPackage(byte[] packageBytes, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(Organization)) return (0, null, "No Organization");
            if (packageBytes.IsNullOrEmpty()) return (0, null, "The Package file is empty");

            var url = Url.Combine(BaseUrl, "packages");
            url = Url.Combine(url, Organization);

            var response = await TrakHoundHttpRequests.PostResponse(url, packageBytes, "application/octet-stream", ApiTokenHeader, cancellationToken);
            if (response.StatusCode >= 200 && response.StatusCode < 300)
            {
                try
                {
                    var packageJson = JsonSerializer.Deserialize<TrakHoundPackageJson>(response.GetContentBytes(), Json.SharedOptions);
                    if (packageJson != null) return (response.StatusCode, packageJson.ToPackage(), null);
                }
                catch { }

                return (response.StatusCode, null, "Invalid Response");
            }

            return (response.StatusCode, null, GetErrorMessage(response));
        }

        /// <summary>
        /// The error a server returned : its plain text body (ex. "The Package is already stored"), else the Reason Phrase or the Status Code
        /// </summary>
        private static string GetErrorMessage(TrakHoundHttpResponse response)
        {
            const int maxLength = 500;

            if (response.ContentType == null || response.ContentType.StartsWith("text/", System.StringComparison.OrdinalIgnoreCase))
            {
                string body = null;
                try
                {
                    body = response.GetContentUtf8String()?.Trim();
                }
                catch { }

                // An HTML error page isn't a message
                if (!string.IsNullOrEmpty(body) && !body.StartsWith("<"))
                {
                    return body.Length > maxLength ? body.Substring(0, maxLength) + "..." : body;
                }
            }

            if (!string.IsNullOrEmpty(response.Message)) return response.StatusCode > 0 ? $"{response.Message} ({response.StatusCode})" : response.Message;

            return $"Status {response.StatusCode}";
        }

        /// <summary>
        /// Uploads a Package file from the Stream and returns the Package created on the server
        /// </summary>
        public async Task<TrakHoundPackage> Upload(Stream packageStream)
        {
            if (!string.IsNullOrEmpty(Organization) && packageStream != null)
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, Organization);

                var packageJson = await TrakHoundHttpRequests.Post<TrakHoundPackageJson>(url, packageStream, headers: ApiTokenHeader);
                return packageJson?.ToPackage();
            }

            return null;
        }


        /// <summary>
        /// Sends the Packages installed on an Instance to a TrakHound Package Manager (POST {BaseUrl}/inventory). Doesn't use the Organization.
        /// Returns the Status Code (0 if the server couldn't be reached), the number of Packages it recorded, and the error when it isn't successful
        /// </summary>
        public async Task<(int StatusCode, int Received, string Message)> ReportInventory(TrakHoundPackageInventory inventory, CancellationToken cancellationToken = default)
        {
            if (inventory == null) return (0, 0, "No Inventory");
            if (string.IsNullOrEmpty(BaseUrl)) return (0, 0, "No Base URL");

            var url = Url.Combine(BaseUrl, "inventory");

            // Serialized without the shared options, which leave out false and null values the Package Manager reads
            var body = JsonSerializer.Serialize(inventory);

            var response = await TrakHoundHttpRequests.PostResponse(url, body, "application/json", ApiTokenHeader, cancellationToken);
            if (response.StatusCode >= 200 && response.StatusCode < 300)
            {
                var received = 0;
                try
                {
                    var bytes = response.GetContentBytes();
                    if (!bytes.IsNullOrEmpty()) received = JsonSerializer.Deserialize<TrakHoundPackageInventoryResponse>(bytes)?.Received ?? 0;
                }
                catch { } // Recorded, whatever the body says

                return (response.StatusCode, received, null);
            }

            return (response.StatusCode, 0, !string.IsNullOrEmpty(response.Message) ? response.Message : $"Status {response.StatusCode}");
        }

        /// <summary>
        /// Gets the Trusted Publishers of a TrakHound Package Manager (GET {BaseUrl}/trusted-publishers), whose keys it verified. Doesn't use the Organization.
        /// Returns the Status Code (0 if the server couldn't be reached), the publishers (null unless successful), and the error when it isn't successful
        /// </summary>
        public async Task<(int StatusCode, TrakHoundTrustedPublishers Publishers, string Message)> GetTrustedPublishers(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(BaseUrl)) return (0, null, "No Base URL");

            var url = Url.Combine(BaseUrl, "trusted-publishers");

            var response = await TrakHoundHttpRequests.GetResponse(url, ApiTokenHeader, cancellationToken);
            if (response.StatusCode == 200)
            {
                try
                {
                    var publishers = JsonSerializer.Deserialize<TrakHoundTrustedPublishers>(response.GetContentBytes());
                    if (publishers?.Publishers != null) return (200, publishers, null);
                }
                catch { }

                return (200, null, "Invalid Response");
            }

            return (response.StatusCode, null, !string.IsNullOrEmpty(response.Message) ? response.Message : $"Status {response.StatusCode}");
        }

        /// <summary>
        /// Gets the server key of a TrakHound Package Manager (GET {BaseUrl}/server), which signs its attestation lists. Doesn't use the Organization.
        /// Returns the Status Code (0 if the server couldn't be reached), the key (null unless successful), and the error when it isn't successful
        /// </summary>
        public async Task<(int StatusCode, TrakHoundPackageManagerServerKey Key, string Message)> GetServerKey(CancellationToken cancellationToken = default)
        {
            return await GetJson<TrakHoundPackageManagerServerKey>("server", o => !string.IsNullOrEmpty(o.PublicKeyPem), cancellationToken);
        }

        /// <summary>
        /// Gets the signed attestation list of a TrakHound Package Manager (GET {BaseUrl}/attestations) : the builds approved in the Instance's Channel. Doesn't use the Organization.
        /// Returns the Status Code (0 if the server couldn't be reached), the list as served (null unless successful, and not yet checked), and the error when it isn't successful
        /// </summary>
        public async Task<(int StatusCode, TrakHoundPackageAttestationEnvelope Envelope, string Message)> GetAttestations(CancellationToken cancellationToken = default)
        {
            return await GetJson<TrakHoundPackageAttestationEnvelope>("attestations", o => !string.IsNullOrEmpty(o.Payload), cancellationToken);
        }

        private async Task<(int StatusCode, T Value, string Message)> GetJson<T>(string path, System.Func<T, bool> isValid, CancellationToken cancellationToken) where T : class
        {
            if (string.IsNullOrEmpty(BaseUrl)) return (0, null, "No Base URL");

            var url = Url.Combine(BaseUrl, path);

            var response = await TrakHoundHttpRequests.GetResponse(url, ApiTokenHeader, cancellationToken);
            if (response.StatusCode == 200)
            {
                try
                {
                    var value = JsonSerializer.Deserialize<T>(response.GetContentBytes());
                    if (value != null && isValid(value)) return (200, value, null);
                }
                catch { }

                return (200, null, "Invalid Response");
            }

            return (response.StatusCode, null, !string.IsNullOrEmpty(response.Message) ? response.Message : $"Status {response.StatusCode}");
        }


        /// <summary>
        /// Deletes all Versions of the Package from the specified Organization
        /// </summary>
        public async Task<bool> DeletePackage(string organization, string packageId)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageId))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, organization);
                url = Url.Combine(url, packageId);

                return await TrakHoundHttpRequests.Delete(url, headers: ApiTokenHeader);
            }

            return false;
        }

        /// <summary>
        /// Deletes the single Package Version with the specified Uuid from the specified Organization
        /// </summary>
        public async Task<bool> DeletePackageVersion(string organization, string packageUuid)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageUuid))
            {
                var url = Url.Combine(BaseUrl, "packages");
                url = Url.Combine(url, organization);
                url = Url.AddQueryParameter(url, "packageUuid", packageUuid);

                return await TrakHoundHttpRequests.Delete(url, headers: ApiTokenHeader);
            }

            return false;
        }
    }
}
