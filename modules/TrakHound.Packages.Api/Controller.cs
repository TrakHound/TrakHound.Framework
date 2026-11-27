using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Api;
using TrakHound.Packages.Api.Models;
using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.Tables;

namespace TrakHound.Packages.Api
{
    public class Controller : TrakHoundApiController
    {
        private const string _blobCollection = "trakhound.packages";
        private const int _maxManifestRequest = 200;
        private const int _maxSearchResults = 1000;

        // Organization names as stored (refreshed when a requested Organization isn't found)
        private volatile string[] _organizations;


        protected async override Task OnInitializeAsync()
        {
            var queryBuilder = new TrakHoundTableRequestBuilder();
            queryBuilder.AppendEnsureTable<PackageModel>();
            queryBuilder.AppendEnsureTable<PackageMetadataModel>();
            queryBuilder.AppendEnsureTable<PackageDependencyModel>();

            // Creating a Table doesn't change one that already exists, and only SQL Server has Tables from before content_hash was added
            queryBuilder.AppendQuery(new TrakHoundTableQueryVariants
            {
                [TrakHoundTableDialects.SqlServer] = $"if col_length('[{PackageModel.SchemaName}].[{PackageModel.TableName}]', 'content_hash') is null alter table [{PackageModel.SchemaName}].[{PackageModel.TableName}] add [content_hash] varchar(255) null;",
                [TrakHoundTableDialects.Ansi] = "select 1;"
            });

            // Versions were stored without their Prerelease Label before (ex. 0.3.4-dev.20261114153000 wasn't indexed). Add the Column to existing Tables
            queryBuilder.AppendQuery(new TrakHoundTableQueryVariants
            {
                [TrakHoundTableDialects.SqlServer] = $"if col_length('[{PackageModel.SchemaName}].[{PackageModel.TableName}]', 'version_prerelease') is null alter table [{PackageModel.SchemaName}].[{PackageModel.TableName}] add [version_prerelease] varchar(255) null;",
                [TrakHoundTableDialects.Postgres] = $"alter table \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}\" add column if not exists \"version_prerelease\" varchar(255) null;",
                [TrakHoundTableDialects.Ansi] = "select 1;"
            });

            // Metadata values were limited to 255 characters before. Widen the Column on existing Tables (col_length is -1 for max, and varchar to text doesn't rewrite the Table)
            queryBuilder.AppendQuery(new TrakHoundTableQueryVariants
            {
                [TrakHoundTableDialects.SqlServer] = $"if col_length('[{PackageMetadataModel.SchemaName}].[{PackageMetadataModel.TableName}]', 'value') <> -1 alter table [{PackageMetadataModel.SchemaName}].[{PackageMetadataModel.TableName}] alter column [value] nvarchar(max) null;",
                [TrakHoundTableDialects.Postgres] = $"alter table \"{PackageMetadataModel.SchemaName}\".\"{PackageMetadataModel.TableName}\" alter column \"value\" type text;",
                [TrakHoundTableDialects.Ansi] = "select 1;"
            });

            var response = await Client.Services.Tables.ExecuteNonQuery(queryBuilder.Build());

            if (response.Success)
            {
                // SQLite can't add a Column only if it's missing, so it's added on its own, and the error for a Table that already has it is ignored
                var sqliteResponse = await Client.Services.Tables.ExecuteNonQuery(TrakHoundTableRequest.Query(new TrakHoundTableQueryVariants
                {
                    [TrakHoundTableDialects.Sqlite] = $"alter table \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}\" add column \"version_prerelease\" varchar(255) null;",
                    [TrakHoundTableDialects.Ansi] = "select 1;"
                }));
                if (!sqliteResponse.Success) Log(TrakHoundLogLevel.Debug, $"version_prerelease Column not added (it may already exist) : {sqliteResponse.Message}");
            }

            if (response.Success)
            {
                var scriptPath = Path.Combine(AssemblyDirectory, "Scripts", "package_latest_view");
                Log(TrakHoundLogLevel.Debug, $"Initializing view from scripts : {scriptPath}");

                try
                {
                    response = await Client.Services.Tables.ExecuteNonQuery(TrakHoundTableRequest.Script(TrakHoundTableQueryVariants.FromDirectory(scriptPath)));
                }
                catch (IOException ex)
                {
                    response = TrakHoundServiceResponse.InternalError(ex.Message);
                }
            }

            if (response.Success)
            {
                Log(TrakHoundLogLevel.Information, "Tables Initialized Succesfully");
            }
            else
            {
                Log(TrakHoundLogLevel.Error, $"Error Initializing Tables : {response.Message}");
            }
        }


        /// <summary>
        /// List the Organizations that have Packages
        /// </summary>
        /// <returns>A list of Organization names</returns>
        [ApiGet("organizations", Description = "Lists the organizations that have packages.")]
        public async Task<TrakHoundApiResponse> ListOrganizations()
        {
            var query = $"select distinct \"organization\" from \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}\" order by \"organization\";";

            var tableResponse = await Client.Services.Tables.ReadMultipleString(TrakHoundTableRequest.Ansi(query));
            if (tableResponse.Success)
            {
                if (!tableResponse.Result.IsNullOrEmpty())
                {
                    return Ok(tableResponse.Result);
                }
                else
                {
                    return NotFound("No Organizations Found");
                }
            }
            else
            {
                return InternalError(tableResponse.Message);
            }
        }

        /// <summary>
        /// Search the Latest Version of each Package in every Organization (see package_latest_view), ordered by Package ID and then Organization
        /// </summary>
        /// <param name="q">Optional text matched anywhere in a Package ID or description, ignoring case</param>
        /// <param name="category">Optional Category of the Packages (ex. api, app, function, service)</param>
        /// <param name="skip">The number of Packages to skip, for paging</param>
        /// <param name="take">The most Packages to return</param>
        /// <returns>A list of Packages with their Organizations</returns>
        [ApiGet("search", Description = "Searches the latest version of each package in every organization (the most recently built version of each), ordered by package ID and then organization.")]
        [ApiMcpTool]
        public async Task<TrakHoundApiResponse> Search(
            [FromQuery(Description = "Text matched anywhere in a package ID or description, ignoring case")] string q = null,
            [FromQuery(Description = "The package category, ex. api, app, function, service or worker")] string category = null,
            [FromQuery(Description = "The number of packages to skip, for paging")] int skip = 0,
            [FromQuery(Description = "The most packages to return (up to 1,000)")] int take = 100
            )
        {
            if (skip < 0 || take < 1) return BadRequest("skip can't be negative and take must be at least 1");
            take = Math.Min(take, _maxSearchResults);

            var parameters = new TrakHoundTableParameterCollection();
            var conditions = new List<string>();

            if (!string.IsNullOrEmpty(category))
            {
                conditions.Add("\"latest\".\"category\" = @category");
                parameters.Add("category", category);
            }

            if (!string.IsNullOrEmpty(q))
            {
                // lower() on both sides, since PostgreSQL compares case-sensitively
                var metadataTable = $"\"{PackageMetadataModel.SchemaName}\".\"{PackageMetadataModel.TableName}\"";
                conditions.Add($"(lower(\"latest\".\"id\") like @q escape '\\' or exists (select 1 from {metadataTable} as \"metadata\" where \"metadata\".\"organization\" = \"latest\".\"organization\" and \"metadata\".\"package_uuid\" = \"latest\".\"uuid\" and \"metadata\".\"name\" = 'description' and lower(\"metadata\".\"value\") like @q escape '\\'))");
                parameters.Add("q", $"%{EscapeLike(q.ToLowerInvariant())}%");
            }

            var query = $"select \"latest\".\"organization\", \"latest\".\"uuid\", \"latest\".\"id\" from \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}_latest_view\" as \"latest\"";
            if (conditions.Count > 0) query += $" where {string.Join(" and ", conditions)}";
            query += " order by \"latest\".\"id\", \"latest\".\"organization\";";

            var matchResponse = await Client.Services.Tables.ReadMultiple<PackageModel>(TrakHoundTableRequest.Ansi(query, parameters, skip, take));
            if (!matchResponse.Success) return InternalError(matchResponse.Message);

            var matches = matchResponse.Result?.ToList() ?? new List<PackageModel>();
            if (matches.Count == 0) return Ok(Array.Empty<TrakHoundPackageSearchResultJson>());

            // Packages are read (and cached) per Organization, then put back in the order of the matches
            var packages = new Dictionary<(string Organization, string Uuid), TrakHoundPackageJson>();
            foreach (var organization in matches.GroupBy(o => o.Organization))
            {
                var organizationPackages = await ReadPackages(organization.Key, organization.Select(o => o.Uuid).ToList());
                if (organizationPackages == null) continue;

                foreach (var package in organizationPackages) packages[(organization.Key, package.Uuid)] = package;
            }

            var results = new List<TrakHoundPackageSearchResultJson>();
            foreach (var match in matches)
            {
                if (packages.TryGetValue((match.Organization, match.Uuid), out var package))
                {
                    results.Add(new TrakHoundPackageSearchResultJson { Organization = match.Organization, Package = package });
                }
            }

            return Ok(results);
        }


        /// <summary>
        /// Get a Package by its UUID
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="uuid">UUID of the Package</param>
        /// <returns>The Package information</returns>
        [ApiGet("{organization}/{uuid}", Description = "Gets the information of a package version by its UUID.")]
        public async Task<TrakHoundApiResponse> Get(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package UUID, which identifies one version of a package")] string uuid
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(uuid))
            {
                var package = await ReadPackage(organization, uuid);
                if (package != null)
                {
                    return Ok(package);
                }
                else
                {
                    return NotFound("Package Not Found");
                }
            }
            else
            {
                return BadRequest();
            }
        }

        /// <summary>
        /// Get a specific Version of a Package. A Version with a wildcard (ex. 1.*) returns the highest matching Version
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="id">ID of the Package</param>
        /// <param name="version">Version of the Package (ex. 1.2.3 or *)</param>
        /// <returns>The Package information</returns>
        [ApiGet("{organization}/{id}/{version}", Description = "Gets the information of a version of a package. A version with a * wildcard (ex. 1.* or *) returns the highest matching version.")]
        public async Task<TrakHoundApiResponse> Get(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID, ex. TrakHound.Functions.Api")] string id,
            [FromRoute(Description = "The version, ex. 1.2.3, 1.* or * (the latest)")] string version
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
            {
                if (version.Contains('*'))
                {
                    var (success, packageUuid, message) = await FindMatchingPackage(organization, id, version);
                    if (!success) return InternalError(message);
                    if (string.IsNullOrEmpty(packageUuid)) return NotFound("Package Not Found");

                    var package = await ReadPackage(organization, packageUuid);
                    if (package != null) return Ok(package);

                    return NotFound("Error Reading Package");
                }
                else
                {
                    // An exact Version (ex. 1.2.3 or 1.2.3-dev.20261114153000). A Stable Version doesn't match its Prereleases
                    if (version.ToBaseVersion() == null) return BadRequest("Invalid Package Version");

                    var (success, packages, message) = await ReadPackageVersions(organization, id);
                    if (!success) return InternalError(message);

                    var match = FindPackage(packages, version);
                    if (match == null) return NotFound("Package Not Found");

                    var package = await ReadPackage(organization, match.Uuid);
                    if (package != null) return Ok(package);

                    return NotFound("Error Reading Package");
                }
            }
            else
            {
                return BadRequest();
            }
        }

        /// <summary>
        /// Get the Latest Version of a Package : the highest Stable Version, or the highest Prerelease if the Package has no Stable Version
        /// (the Version that "*" selects, so the same one an Instance installs without a Version)
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="id">ID of the Package</param>
        /// <returns>The Package information</returns>
        [ApiGet("{organization}/{id}/latest", Description = "Gets the information of a package's latest version: the highest stable version, or the highest prerelease if the package has no stable version (in semantic versioning order, so 1.0.0-dev.2 is newer than 1.0.0-dev.1).")]
        public async Task<TrakHoundApiResponse> GetLatest(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID, ex. TrakHound.Functions.Api")] string id
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(id))
            {
                var (success, packageUuid, message) = await FindMatchingPackage(organization, id, TrakHoundPackage.WilcardVersion);
                if (!success) return InternalError(message);
                if (string.IsNullOrEmpty(packageUuid)) return NotFound("Package Not Found");

                var package = await ReadPackage(organization, packageUuid);
                if (package != null) return Ok(package);

                return NotFound("Error Reading Package");
            }
            else
            {
                return BadRequest();
            }
        }

        /// <summary>
        /// List all Versions of a Package, with their Prerelease Labels, in Semantic Versioning order
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="id">ID of the Package</param>
        /// <returns>A list of Version strings (ex. 1.2.3 and 1.2.4-dev.20261114153000)</returns>
        [ApiGet("{organization}/{id}/versions", Description = "Lists every version of a package, including prereleases (ex. 1.2.4-dev.20261114153000), oldest first in semantic versioning order.")]
        public async Task<TrakHoundApiResponse> GetVersions(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID, ex. TrakHound.Functions.Api")] string id
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(id))
            {
                var (success, packages, message) = await ReadPackageVersions(organization, id);
                if (!success) return InternalError(message);
                if (packages.Count == 0) return NotFound("No Versions found for Package");

                var versions = packages
                    .Select(GetVersion)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(o => o, TrakHound.Extensions.VersionComparer.Default)
                    .ToList();

                return Ok(versions);
            }
            else
            {
                return BadRequest();
            }
        }

        /// <summary>
        /// Get the Latest Version of each Package in an Organization. The Latest Version is the most recently built Version (see package_latest_view)
        /// </summary>
        /// <param name="organization">Organization the Packages belong to</param>
        /// <param name="q">Optional query that matches an exact Package UUID or a Package ID prefix</param>
        /// <returns>A list of Package information</returns>
        [ApiGet("{organization}/latest", Description = "Lists the latest version of each package in an organization (the most recently built version of each), optionally filtered by q.")]
        public async Task<TrakHoundApiResponse> QueryLatest(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromQuery(Description = "A package UUID (matched exactly), or the start of a package ID")] string q = null
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization))
            {
                if (!string.IsNullOrEmpty(q))
                {
                    // Test for Exact Match (UUID)
                    var exact = await ReadPackage(organization, q);
                    if (exact != null)
                    {
                        return Ok(new[] { exact });
                    }
                    else
                    {
                        var query = $"select \"uuid\" from \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}_latest_view\" where \"organization\" = @organization and \"id\" like @q escape '\\';";

                        var parameters = new TrakHoundTableParameterCollection();
                        parameters.Add("organization", organization);
                        parameters.Add("q", $"{EscapeLike(q)}%");

                        var uuidResponse = await Client.Services.Tables.ReadMultipleString(TrakHoundTableRequest.Ansi(query, parameters));
                        if (uuidResponse.Success)
                        {
                            if (!uuidResponse.Result.IsNullOrEmpty())
                            {
                                var packages = await ReadPackages(organization, uuidResponse.Result);
                                if (!packages.IsNullOrEmpty())
                                {
                                    return Ok(packages);
                                }
                                else
                                {
                                    return NotFound("Error Reading Packages");
                                }
                            }
                            else
                            {
                                return Ok(Array.Empty<TrakHoundPackageJson>());
                            }
                        }
                        else
                        {
                            return InternalError(uuidResponse.Message);
                        }
                    }
                }
                else
                {
                    var query = $"select \"uuid\" from \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}_latest_view\" where \"organization\" = @organization;";

                    var parameters = new TrakHoundTableParameterCollection();
                    parameters.Add("organization", organization);

                    var uuidResponse = await Client.Services.Tables.ReadMultipleString(TrakHoundTableRequest.Ansi(query, parameters));
                    if (uuidResponse.Success)
                    {
                        if (!uuidResponse.Result.IsNullOrEmpty())
                        {
                            var packages = await ReadPackages(organization, uuidResponse.Result);
                            if (!packages.IsNullOrEmpty())
                            {
                                return Ok(packages);
                            }
                            else
                            {
                                return NotFound("Error Reading Packages");
                            }
                        }
                        else
                        {
                            return Ok(Array.Empty<TrakHoundPackageJson>());
                        }
                    }
                    else
                    {
                        return InternalError(uuidResponse.Message);
                    }
                }
            }
            else
            {
                return BadRequest();
            }
        }

        /// <summary>
        /// Get the Latest Version of each Package in an Organization filtered by Category. The Latest Version is the most recently built Version (see package_latest_view)
        /// </summary>
        /// <param name="organization">Organization the Packages belong to</param>
        /// <param name="category">Category of the Packages (ex. api, app, function, service)</param>
        /// <param name="q">Optional query that matches an exact Package UUID or a Package ID prefix</param>
        /// <returns>A list of Package information</returns>
        [ApiGet("{organization}/category/{category}", Description = "Lists the latest version of each package of a category in an organization (the most recently built version of each), optionally filtered by q.")]
        public async Task<TrakHoundApiResponse> QueryByCategory(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package category, ex. api, app, function, service or worker")] string category,
            [FromQuery(Description = "A package UUID (matched exactly), or the start of a package ID")] string q = null
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(category))
            {
                if (!string.IsNullOrEmpty(q))
                {
                    // Test for Exact Match (UUID)
                    var exact = await ReadPackage(organization, q);
                    if (exact != null)
                    {
                        return Ok(new[] { exact });
                    }
                    else
                    {
                        var query = $"select \"uuid\" from \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}_latest_view\" where \"organization\" = @organization and \"category\" = @category and \"id\" like @q escape '\\';";

                        var parameters = new TrakHoundTableParameterCollection();
                        parameters.Add("organization", organization);
                        parameters.Add("category", category);
                        parameters.Add("q", $"{EscapeLike(q)}%");

                        var uuidResponse = await Client.Services.Tables.ReadMultipleString(TrakHoundTableRequest.Ansi(query, parameters));
                        if (uuidResponse.Success)
                        {
                            if (!uuidResponse.Result.IsNullOrEmpty())
                            {
                                var packages = await ReadPackages(organization, uuidResponse.Result);
                                if (!packages.IsNullOrEmpty())
                                {
                                    return Ok(packages);
                                }
                                else
                                {
                                    return NotFound("Error Reading Packages");
                                }
                            }
                            else
                            {
                                return Ok(Array.Empty<TrakHoundPackageJson>());
                            }
                        }
                        else
                        {
                            return InternalError(uuidResponse.Message);
                        }
                    }
                }
                else
                {
                    var query = $"select \"uuid\" from \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}_latest_view\" where \"organization\" = @organization and \"category\" = @category;";

                    var parameters = new TrakHoundTableParameterCollection();
                    parameters.Add("organization", organization);
                    parameters.Add("category", category);

                    var uuidResponse = await Client.Services.Tables.ReadMultipleString(TrakHoundTableRequest.Ansi(query, parameters));
                    if (uuidResponse.Success)
                    {
                        if (!uuidResponse.Result.IsNullOrEmpty())
                        {
                            var packages = await ReadPackages(organization, uuidResponse.Result);
                            if (!packages.IsNullOrEmpty())
                            {
                                return Ok(packages);
                            }
                            else
                            {
                                return NotFound("Error Reading Packages");
                            }
                        }
                        else
                        {
                            return Ok(Array.Empty<TrakHoundPackageJson>());
                        }
                    }
                    else
                    {
                        return InternalError(uuidResponse.Message);
                    }
                }
            }
            else
            {
                return BadRequest();
            }
        }


        /// <summary>
        /// Download a Package file (.thp) by its UUID
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="uuid">UUID of the Package</param>
        /// <returns>The Package file as application/octet-stream</returns>
        [ApiGet("{organization}/{uuid}/download", Description = "Downloads a package file (.thp) by its UUID.")]
        public async Task<TrakHoundApiResponse> Download(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package UUID, which identifies one version of a package")] string uuid
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(uuid))
            {
                var package = await ReadPackage(organization, uuid);
                if (package != null)
                {
                    var filename = package.Id + TrakHoundPackage.FileExtension;

                    var stream = await ReadPackageStream(organization, package.Uuid);
                    if (stream != null)
                    {
                        return File(stream, "application/octet-stream", filename);
                    }
                    else
                    {
                        return NotFound("Error Reading Package Contents");
                    }
                }
                else
                {
                    return NotFound("Package Not Found");
                }
            }
            else
            {
                return BadRequest();
            }
        }

        /// <summary>
        /// Download a Package file (.thp) by its ID and Version. A Version with a wildcard (ex. 1.*) downloads the highest matching Version,
        /// which is how Instances download a Dependency
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="id">ID of the Package</param>
        /// <param name="version">Version of the Package (ex. 1.2.3 or 1.*)</param>
        /// <returns>The Package file as application/octet-stream</returns>
        [ApiGet("{organization}/{id}/{version}/download", Description = "Downloads a package file (.thp) by its ID and version. A version with a * wildcard (ex. 1.*) downloads the highest matching version.")]
        public async Task<TrakHoundApiResponse> Download(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID, ex. TrakHound.Functions.Api")] string id,
            [FromRoute(Description = "The version, ex. 1.2.3, 1.* or *")] string version
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
            {
                string uuid;
                if (version.Contains('*'))
                {
                    var (success, packageUuid, message) = await FindMatchingPackage(organization, id, version);
                    if (!success) return InternalError(message);
                    uuid = packageUuid;
                }
                else
                {
                    uuid = TrakHoundPackage.GenerateUuid(id, version);
                }

                var information = !string.IsNullOrEmpty(uuid) ? await ReadPackage(organization, uuid) : null;
                if (information != null)
                {
                    var filename = information.Id + TrakHoundPackage.FileExtension;

                    var stream = await ReadPackageStream(organization, information.Uuid);
                    if (stream != null)
                    {
                        return File(stream, "application/octet-stream", filename);
                    }
                    else
                    {
                        return NotFound("Error Reading Package Contents");
                    }
                }
                else
                {
                    return NotFound("Package Not Found");
                }
            }
            else
            {
                return BadRequest();
            }
        }

        /// <summary>
        /// Download multiple Packages as a ZIP archive. Not yet implemented (returns NotFound)
        /// </summary>
        /// <param name="organization">Organization the Packages belong to</param>
        /// <param name="packageQueries">Queries identifying the Packages to include in the archive</param>
        /// <returns>The ZIP archive as application/octet-stream</returns>
        [ApiPost("{organization}/download", Description = "Not available in this version (always returns 404). Download each package from {organization}/{uuid}/download instead.")]
        public async Task<TrakHoundApiResponse> DownloadArchive(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromBody(Description = "The packages to include")] IEnumerable<TrakHoundPackageQuery> packageQueries
            )
        {
            if (!string.IsNullOrEmpty(organization) && !packageQueries.IsNullOrEmpty())
            {
                var stream = await DownloadArchiveStream(organization, packageQueries);
                if (stream != null)
                {
                    var filename = $"trakhound-packages-{DateTime.Now.ToISO8601String()}.zip";
                    return File(stream, "application/octet-stream", filename);
                }
                else
                {
                    return NotFound();
                }
            }

            return InternalError();
        }

        /// <summary>
        /// Get the Manifest and signature of a Package without downloading the Package. A Version with a wildcard (ex. 1.*) returns the highest matching Version
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="id">ID of the Package</param>
        /// <param name="version">Version of the Package (ex. 1.2.3 or 1.*)</param>
        /// <returns>The exact Manifest and signature bytes</returns>
        [ApiGet("{organization}/{id}/{version}/manifest", Description = "Gets the manifest and signature of a package version, without downloading the package, as the exact signed bytes (Base64). Shows who signed the package, but not that the package's files match. A version with a * wildcard returns the highest matching version.")]
        public async Task<TrakHoundApiResponse> GetManifest(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID, ex. TrakHound.Functions.Api")] string id,
            [FromRoute(Description = "The version, ex. 1.2.3, 1.* or *")] string version
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
            {
                string uuid;
                if (version.Contains('*'))
                {
                    var (success, packageUuid, message) = await FindMatchingPackage(organization, id, version);
                    if (!success) return InternalError(message);
                    uuid = packageUuid;
                }
                else
                {
                    uuid = TrakHoundPackage.GenerateUuid(id, version);
                }

                var manifest = !string.IsNullOrEmpty(uuid) ? await new PackageManifestStore(Client).Read(organization, uuid) : null;
                if (manifest != null) return Ok(manifest);

                return NotFound("Package Not Found");
            }
            else
            {
                return BadRequest();
            }
        }

        /// <summary>
        /// Get the Manifests and signatures of multiple Packages. A Package that isn't found is left out of the result
        /// </summary>
        /// <param name="organization">Organization the Packages belong to</param>
        /// <param name="packageUuids">UUIDs of the Packages (up to 200)</param>
        /// <returns>The exact Manifest and signature bytes of each Package</returns>
        [ApiPost("{organization}/manifests", Description = "Gets the manifests and signatures of up to 200 package versions, without downloading the packages. A package that isn't found is left out of the result.")]
        public async Task<TrakHoundApiResponse> GetManifests(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromBody(Description = "The UUIDs of the package versions (up to 200)")] IEnumerable<string> packageUuids
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization) && !packageUuids.IsNullOrEmpty())
            {
                var uuids = packageUuids.Distinct().ToList();
                if (uuids.Count > _maxManifestRequest) return BadRequest($"Up to {_maxManifestRequest} Packages can be requested");

                return Ok(await new PackageManifestStore(Client).Read(organization, uuids));
            }
            else
            {
                return BadRequest();
            }
        }

        /// <summary>
        /// Upload a Package file (.thp). The Package information is read from the file and written to the Tables, and the file contents are stored as a Blob
        /// </summary>
        /// <param name="organization">Organization to upload the Package to</param>
        /// <param name="packageStream">Raw Package file contents (application/octet-stream)</param>
        /// <returns>The uploaded Package information</returns>
        [ApiPost("{organization}", Description = "Uploads a package file (.thp) to an organization. The package's information is read from the file and indexed, and the file is stored so it can be downloaded. Returns the package's information.")]
        public async Task<TrakHoundApiResponse> Upload(
            [FromRoute(Description = "The organization to upload the package to")] string organization,
            [FromBody(ContentType = "application/octet-stream", Description = "The package file contents (.thp, built by the TrakHound CLI or TrakHoundPackageBuilder)")] Stream packageStream
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization))
            {
                try
                {
                    var inputStream = packageStream;
                    if (inputStream != null)
                    {
                        using (var stream = new MemoryStream())
                        {
                            await inputStream.CopyToAsync(stream);

                            // Read PackageInformation from package bytes
                            var package = TrakHoundPackage.ReadInformationFromStream(stream);
                            if (package != null)
                            {
                                // A Package that isn't indexed couldn't be found by its ID or Version, so its file isn't stored either
                                if (package.Version.ToBaseVersion() == null) return BadRequest($"Invalid Package Version ({package.Version})");
                                if (!await AddPackage(organization, package)) return InternalError($"Unable to index {package.Id} v{package.Version}");

                                // A package uploaded again keeps its UUID, so drop its cached information to read the new rows
                                await Client.Services.KeyValues.Delete("trakhound.packages", new[] { $"{organization}:{package.Uuid}" });

                                stream.Seek(0, SeekOrigin.Begin);

                                if (await UploadPackage(organization, package, stream))
                                {
                                    // After the upload, or a read in between would cache the replaced Manifest again
                                    if (!await new PackageManifestStore(Client).Remove(organization, package.Uuid))
                                    {
                                        Log(TrakHoundLogLevel.Warning, $"Unable to remove the cached Manifest of {package.Id} v{package.Version}, so the previous one may still be returned");
                                    }

                                    return Ok(package);
                                }
                                else
                                {
                                    return InternalError();
                                }
                            }
                            else
                            {
                                return BadRequest();
                            }
                        }
                    }
                    else
                    {
                        return BadRequest();
                    }
                }
                catch (Exception ex)
                {
                    return InternalError(ex.Message);
                }
            }
            else
            {
                return BadRequest();
            }
        }


        /// <summary>
        /// Delete all Versions of a Package. Not supported (returns InternalError)
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="packageId">ID of the Package</param>
        [ApiDelete("{organization}/{packageId}", Description = "Not available in this version (always returns 500 Not Supported). Delete each version with DELETE {organization}?packageUuid= instead.")]
        public async Task<TrakHoundApiResponse> DeletePackage(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromRoute(Description = "The package ID")] string packageId
            )
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageId))
            {
                return InternalError("Not Supported");

                //var service = new Service(Client);

                //if (await service.DeletePackage(organization, packageId))
                //{
                //    return Ok();
                //}
                //else
                //{
                //    return NotFound();
                //}
            }

            return BadRequest();
        }

        /// <summary>
        /// Delete a single Package Version, including its Blob, Table rows, and Cache entry
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="packageUuid">UUID of the Package Version to delete</param>
        /// <returns>Ok if the Package Version was deleted</returns>
        [ApiDelete("{organization}", Description = "Deletes a version of a package: its file, information and cache entry.")]
        public async Task<TrakHoundApiResponse> DeletePackageVersion(
            [FromRoute(Description = "The organization the packages belong to")] string organization,
            [FromQuery(Description = "The UUID of the package version to delete")] string packageUuid
            )
        {
            organization = await ResolveOrganization(organization);

            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageUuid))
            {
                var blobKey = $"{organization}:{packageUuid}".ToSHA256Hash();

                var blobResponse = await Client.Services.Blobs.Delete(_blobCollection, blobKey);
                if (blobResponse.Success || blobResponse.StatusCode == 404)
                {
                    var tableQueryBuilder = new TrakHoundTableRequestBuilder();
                    tableQueryBuilder.AppendQuery($"delete from \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}\" where \"organization\" = @organization and \"uuid\" = @packageUuid;");
                    tableQueryBuilder.AppendQuery($"delete from \"{PackageMetadataModel.SchemaName}\".\"{PackageMetadataModel.TableName}\" where \"organization\" = @organization and \"package_uuid\" = @packageUuid;");
                    tableQueryBuilder.AppendQuery($"delete from \"{PackageDependencyModel.SchemaName}\".\"{PackageDependencyModel.TableName}\" where \"organization\" = @organization and \"package_uuid\" = @packageUuid;");

                    tableQueryBuilder.Parameters.Add("organization", organization);
                    tableQueryBuilder.Parameters.Add("packageUuid", packageUuid);

                    var tableResponse = await Client.Services.Tables.ExecuteNonQuery(tableQueryBuilder.Build());
                    if (tableResponse.Success)
                    {
                        var cacheKey = $"{organization}:{packageUuid}";

                        var cacheResponse = await Client.Services.KeyValues.Delete("trakhound.packages", new[] { cacheKey, PackageManifestStore.GetCacheKey(organization, packageUuid) });
                        if (cacheResponse.Success)
                        {
                            return Ok();
                        }
                        else
                        {
                            return InternalError("CACHE ERROR : " + cacheResponse.Message);
                        }
                    }
                    else
                    {
                        return InternalError("TABLE ERROR : " + tableResponse.Message);
                    }
                }
                else
                {
                    return InternalError("BLOB ERROR : " + blobResponse.Message);
                }
            }

            return BadRequest();
        }


        /// <summary>
        /// Resolve an Organization to the casing it is stored with, so Organizations are matched case-insensitively.
        /// Package Blobs and Cache entries are keyed by the stored name, so it has to be used exactly.
        /// An exact match wins (for Organizations that differ only by case). Returns the Organization unchanged if it doesn't exist yet
        /// </summary>
        /// <param name="organization">Organization as requested</param>
        /// <returns>The stored Organization name</returns>
        private async Task<string> ResolveOrganization(string organization)
        {
            if (string.IsNullOrEmpty(organization)) return organization;

            var match = MatchOrganization(_organizations, organization);
            if (match != null) return match;

            // Not known (or added since the last read), so read the stored Organizations again
            var response = await Client.Services.Tables.ReadMultipleString(TrakHoundTableRequest.Ansi($"select distinct \"organization\" from \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}\" order by \"organization\";"));
            if (response.Success)
            {
                _organizations = response.Result?.Where(o => !string.IsNullOrEmpty(o)).ToArray() ?? Array.Empty<string>();
            }

            return MatchOrganization(_organizations, organization) ?? organization;
        }

        private static string MatchOrganization(string[] organizations, string organization)
        {
            if (organizations == null) return null;

            return organizations.FirstOrDefault(o => o == organization)
                ?? organizations.FirstOrDefault(o => string.Equals(o, organization, StringComparison.OrdinalIgnoreCase));
        }

        // Escape LIKE wildcards so the value is matched literally
        private static string EscapeLike(string value)
        {
            return value?.Replace("\\", "\\\\").Replace("[", "\\[").Replace("%", "\\%").Replace("_", "\\_");
        }

        /// <summary>
        /// Read a Package from the Cache, falling back to the Tables if it is not cached
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="packageUuid">UUID of the Package</param>
        /// <returns>The Package information, or null if not found</returns>
        public async Task<TrakHoundPackageJson> ReadPackage(string organization, string packageUuid)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageUuid))
            {
                var package = await GetCache(organization, packageUuid);
                if (package == null) package = await GetDatabase(organization, packageUuid);
                return package;
            }

            return null;
        }

        /// <summary>
        /// Read multiple Packages from the Cache, falling back to the Tables if any are not cached
        /// </summary>
        /// <param name="organization">Organization the Packages belong to</param>
        /// <param name="packageUuids">UUIDs of the Packages</param>
        /// <returns>The Package information, or null if not found</returns>
        public async Task<IEnumerable<TrakHoundPackageJson>> ReadPackages(string organization, IEnumerable<string> packageUuids)
        {
            if (!string.IsNullOrEmpty(organization) && !packageUuids.IsNullOrEmpty())
            {
                var packages = await GetCache(organization, packageUuids);

                var notFoundPackageUuids = new List<string>();
                if (!packages.IsNullOrEmpty())
                {
                    foreach (var packageUuid in packageUuids)
                    {
                        if (!packages.Any(o => o.Uuid == packageUuid))
                        {
                            notFoundPackageUuids.Add(packageUuid);
                        }
                    }
                }
                else
                {
                    notFoundPackageUuids.AddRange(packageUuids);
                }

                if (!notFoundPackageUuids.IsNullOrEmpty())
                {
                    packages = await GetDatabase(organization, packageUuids);
                }

                return packages;
            }

            return null;
        }

        /// <summary>
        /// Read a Package from the KeyValues Cache
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="packageUuid">UUID of the Package</param>
        /// <returns>The cached Package information, or null if not cached</returns>
        public async Task<TrakHoundPackageJson> GetCache(string organization, string packageUuid)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageUuid))
            {
                var cacheKey = $"{organization}:{packageUuid}";

                var json = (await Client.Services.KeyValues.Read("trakhound.packages", cacheKey)).Result;
                if (json != null)
                {
                    return Json.Convert<TrakHoundPackageJson>(json);
                }
            }

            return null;
        }

        /// <summary>
        /// Read multiple Packages from the KeyValues Cache
        /// </summary>
        /// <param name="organization">Organization the Packages belong to</param>
        /// <param name="packageUuids">UUIDs of the Packages</param>
        /// <returns>The cached Package information, or null if none are cached</returns>
        public async Task<IEnumerable<TrakHoundPackageJson>> GetCache(string organization, IEnumerable<string> packageUuids)
        {
            if (!string.IsNullOrEmpty(organization) && !packageUuids.IsNullOrEmpty())
            {
                var cacheKeys = new List<string>();
                foreach (var packageUuid in packageUuids)
                {
                    cacheKeys.Add($"{organization}:{packageUuid}");
                }

                var response = (await Client.Services.KeyValues.Read("trakhound.packages", cacheKeys)).Result;
                if (response != null && !response.Values.IsNullOrEmpty())
                {
                    var packages = new List<TrakHoundPackageJson>();

                    foreach (var value in response.Values)
                    {
                        var package = Json.Convert<TrakHoundPackageJson>(value.Value);
                        if (package != null) packages.Add(package);
                    }

                    return packages;
                }
            }

            return null;
        }

        /// <summary>
        /// Read a Package and its Metadata from the Tables and update the Cache
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="packageUuid">UUID of the Package</param>
        /// <returns>The Package information, or null if not found</returns>
        public async Task<TrakHoundPackageJson> GetDatabase(string organization, string packageUuid)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageUuid))
            {
                var queryBuilder = new TrakHoundTableRequestBuilder();
                queryBuilder.AppendQuery($"select * from \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}\" where \"organization\" = @organization and \"uuid\" = @packageUuid;");
                queryBuilder.AppendQuery($"select * from \"{PackageMetadataModel.SchemaName}\".\"{PackageMetadataModel.TableName}\" where \"organization\" = @organization and \"package_uuid\" = @packageUuid;");
                queryBuilder.AppendQuery($"select * from \"{PackageDependencyModel.SchemaName}\".\"{PackageDependencyModel.TableName}\" where \"organization\" = @organization and \"package_uuid\" = @packageUuid;");
                queryBuilder.Parameters.Add("organization", organization);
                queryBuilder.Parameters.Add("packageUuid", packageUuid);

                var queryResponse = await Client.Services.Tables.ExecuteQuery(queryBuilder.Build(false, true));
                if (queryResponse.Success)
                {
                    if (queryResponse.Result != null)
                    {
                        var packageModel = queryResponse.Result[0].ReadSingle<PackageModel>();
                        if (packageModel == null) return null;

                        var metadataModels = queryResponse.Result[1].ReadMultiple<PackageMetadataModel>();
                        var dependencyModels = queryResponse.Result[2].ReadMultiple<PackageDependencyModel>();

                        var package = new TrakHoundPackageJson();
                        package.Uuid = packageModel.Uuid;
                        package.Id = packageModel.Id;
                        package.Version = GetVersion(packageModel);
                        package.Category = packageModel.Category;
                        package.BuildDate = packageModel.BuildDate;
                        package.Hash = packageModel.Hash;
                        package.ContentHash = packageModel.ContentHash;

                        if (!metadataModels.IsNullOrEmpty())
                        {
                            var packageMetadata = new Dictionary<string, object>();
                            foreach (var packageMetadataModel in metadataModels)
                            {
                                packageMetadata.Add(packageMetadataModel.Name, packageMetadataModel.Value);
                            }
                            package.Metadata = packageMetadata;
                        }

                        package.Dependencies = CreateDependencies(dependencyModels);

                        // Update Cache
                        var cacheKey = $"{organization}:{packageModel.Uuid}";
                        var json = package.ToJson(true);
                        await Client.Services.KeyValues.Publish("trakhound.packages", cacheKey, json);

                        return package;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {

                }
            }

            return null;
        }

        /// <summary>
        /// Read multiple Packages and their Metadata from the Tables and update the Cache
        /// </summary>
        /// <param name="organization">Organization the Packages belong to</param>
        /// <param name="packageUuids">UUIDs of the Packages</param>
        /// <returns>The Package information, or null if not found</returns>
        public async Task<IEnumerable<TrakHoundPackageJson>> GetDatabase(string organization, IEnumerable<string> packageUuids)
        {
            if (!string.IsNullOrEmpty(organization) && !packageUuids.IsNullOrEmpty())
            {
                var packageModels = new List<PackageModel>();
                var metadataModels = new List<PackageMetadataModel>();
                var dependencyModels = new List<PackageDependencyModel>();

                var packageColumns = string.Join(", ", TrakHoundTables.GetColumnNames<PackageModel>().Select(o => $"\"package\".\"{o}\""));
                var metadataColumns = string.Join(", ", TrakHoundTables.GetColumnNames<PackageMetadataModel>().Select(o => $"\"metadata\".\"{o}\""));
                var dependencyColumns = string.Join(", ", TrakHoundTables.GetColumnNames<PackageDependencyModel>().Select(o => $"\"dependency\".\"{o}\""));

                // SQL Server allows a maximum of 2100 Parameters per command so query in chunks
                foreach (var chunk in packageUuids.Distinct().Chunk(1000))
                {
                    var parameters = new TrakHoundTableParameterCollection();
                    parameters.Add("organization", organization);

                    var uuidParameters = new string[chunk.Length];
                    for (var i = 0; i < chunk.Length; i++)
                    {
                        uuidParameters[i] = $"@uuid{i}";
                        parameters.Add($"uuid{i}", chunk[i]);
                    }
                    var uuids = string.Join(", ", uuidParameters);

                    var query = $"select {packageColumns} from \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}\" as \"package\"";
                    query += $" where \"package\".\"organization\" = @organization and \"package\".\"uuid\" in ({uuids});";

                    query += $"select {metadataColumns} from \"{PackageMetadataModel.SchemaName}\".\"{PackageMetadataModel.TableName}\" as \"metadata\"";
                    query += $" where \"metadata\".\"organization\" = @organization and \"metadata\".\"package_uuid\" in ({uuids});";

                    query += $"select {dependencyColumns} from \"{PackageDependencyModel.SchemaName}\".\"{PackageDependencyModel.TableName}\" as \"dependency\"";
                    query += $" where \"dependency\".\"organization\" = @organization and \"dependency\".\"package_uuid\" in ({uuids});";

                    var queryResponse = await Client.Services.Tables.ExecuteQuery(TrakHoundTableRequest.Ansi(query, parameters));
                    if (!queryResponse.Success || queryResponse.Result == null) return null;

                    var chunkPackageModels = queryResponse.Result[0].ReadMultiple<PackageModel>();
                    if (chunkPackageModels != null) packageModels.AddRange(chunkPackageModels);

                    var chunkMetadataModels = queryResponse.Result[1].ReadMultiple<PackageMetadataModel>();
                    if (chunkMetadataModels != null) metadataModels.AddRange(chunkMetadataModels);

                    var chunkDependencyModels = queryResponse.Result[2].ReadMultiple<PackageDependencyModel>();
                    if (chunkDependencyModels != null) dependencyModels.AddRange(chunkDependencyModels);
                }

                if (!packageModels.IsNullOrEmpty())
                {
                    var packages = new List<TrakHoundPackageJson>();

                    foreach (var packageModel in packageModels)
                    {
                        var package = new TrakHoundPackageJson();
                        package.Uuid = packageModel.Uuid;
                        package.Id = packageModel.Id;
                        package.Version = GetVersion(packageModel);
                        package.Category = packageModel.Category;
                        package.BuildDate = packageModel.BuildDate;
                        package.Hash = packageModel.Hash;
                        package.ContentHash = packageModel.ContentHash;

                        var packageMetadataModels = metadataModels?.Where(o => o.Organization == organization && o.PackageUuid == packageModel.Uuid);
                        if (!packageMetadataModels.IsNullOrEmpty())
                        {
                            var packageMetadata = new Dictionary<string, object>();
                            foreach (var packageMetadataModel in packageMetadataModels)
                            {
                                packageMetadata.Add(packageMetadataModel.Name, packageMetadataModel.Value);
                            }
                            package.Metadata = packageMetadata;
                        }

                        package.Dependencies = CreateDependencies(dependencyModels.Where(o => o.Organization == organization && o.PackageUuid == packageModel.Uuid));

                        packages.Add(package);
                    }

                    if (!packages.IsNullOrEmpty())
                    {
                        var entries = new Dictionary<string, string>();
                        foreach (var package in packages)
                        {
                            var cacheKey = $"{organization}:{package.Uuid}";
                            var json = package.ToJson(true);
                            entries.Add(cacheKey, json);
                        }

                        // Update Cache
                        await Client.Services.KeyValues.Publish("trakhound.packages", entries);
                    }

                    return packages;
                }
            }

            return null;
        }


        /// <summary>
        /// Read every Version of a Package (null if the Tables can't be read)
        /// </summary>
        private async Task<(bool Success, List<PackageModel> Packages, string Message)> ReadPackageVersions(string organization, string id)
        {
            var query = $"select * from \"{PackageModel.SchemaName}\".\"{PackageModel.TableName}\" where \"organization\" = @organization and \"id\" = @id;";

            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("organization", organization);
            parameters.Add("id", id);

            var response = await Client.Services.Tables.ExecuteQuery(TrakHoundTableRequest.Ansi(query, parameters));
            if (!response.Success || response.Result == null) return (false, null, response.Message);

            return (true, response.Result[0].ReadMultiple<PackageModel>()?.ToList() ?? new List<PackageModel>(), null);
        }

        /// <summary>
        /// Find the Version of a Package that a Version with wildcards (ex. 1.*, 1.2.*, * or *-*) selects, the same way an Instance selects one
        /// (<see cref="TrakHoundPackage.SelectVersion"/>) : the highest Stable Version that matches, else the highest Prerelease, and with "-*"
        /// the highest of either. Returns a null UUID when no Version matches
        /// </summary>
        private async Task<(bool Success, string Uuid, string Message)> FindMatchingPackage(string organization, string id, string version)
        {
            var (success, packages, message) = await ReadPackageVersions(organization, id);
            if (!success) return (false, null, message);

            var selected = TrakHoundPackage.SelectVersion(packages.Select(GetVersion).Distinct(), version);
            return (true, FindPackage(packages, selected)?.Uuid, null);
        }

        /// <summary>
        /// The Package with the Version (ex. 1.2.3 or 1.2.3-dev.20261114153000). If it was uploaded more than once, the most recently built
        /// </summary>
        private static PackageModel FindPackage(IEnumerable<PackageModel> packages, string version)
        {
            if (string.IsNullOrEmpty(version)) return null;

            return packages?
                .Where(o => string.Equals(GetVersion(o), version, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.BuildDate)
                .FirstOrDefault();
        }

        /// <summary>
        /// The Version of a Package, with its Prerelease Label (ex. 1.2.3 or 1.2.3-dev.20261114153000)
        /// </summary>
        private static string GetVersion(PackageModel package)
        {
            var version = new Version(package.VersionMajor, package.VersionMinor, package.VersionPatch).ToString();
            return !string.IsNullOrEmpty(package.VersionPrerelease) ? $"{version}-{package.VersionPrerelease}" : version;
        }

        /// <summary>
        /// The Prerelease Label of a Version (ex. "dev.20261114153000" for 1.2.3-dev.20261114153000), without any Build Metadata. Null for a Stable Version
        /// </summary>
        private static string GetPrereleaseLabel(string version)
        {
            if (string.IsNullOrEmpty(version)) return null;

            var buildIndex = version.IndexOf('+');
            if (buildIndex >= 0) version = version.Substring(0, buildIndex);

            var labelIndex = version.IndexOf('-');
            return labelIndex >= 0 && labelIndex < version.Length - 1 ? version.Substring(labelIndex + 1) : null;
        }

        private static List<TrakHoundPackageDependency> CreateDependencies(IEnumerable<PackageDependencyModel> dependencyModels)
        {
            if (dependencyModels.IsNullOrEmpty()) return null;

            return dependencyModels
                .OrderBy(o => o.DependencyId)
                .Select(o => new TrakHoundPackageDependency(o.DependencyId, o.Version, o.MinimumVersion))
                .ToList();
        }


        /// <summary>
        /// Write a Package, its Metadata and its Dependencies to the Tables
        /// </summary>
        /// <param name="organization">Organization to add the Package to</param>
        /// <param name="package">Package to add</param>
        /// <returns>True if the Package was written successfully</returns>
        public async Task<bool> AddPackage(string organization, TrakHoundPackage package)
        {
            if (package != null && !string.IsNullOrEmpty(package.Category) && !string.IsNullOrEmpty(package.Id) && !string.IsNullOrEmpty(package.Version))
            {
                // A Prerelease (ex. 0.3.4-dev.20261114153000) keeps its Label, so it isn't listed as (or confused with) the Stable Version
                var packageVersion = package.Version.ToBaseVersion();
                if (packageVersion != null)
                {
                    var packageModel = new PackageModel();
                    packageModel.Organization = organization;
                    packageModel.Uuid = package.Uuid;
                    packageModel.Category = package.Category;
                    packageModel.Id = package.Id;
                    packageModel.VersionMajor = packageVersion.Major;
                    packageModel.VersionMinor = packageVersion.Minor;
                    packageModel.VersionPatch = Math.Max(packageVersion.Build, 0);
                    packageModel.VersionPrerelease = GetPrereleaseLabel(package.Version);
                    packageModel.BuildDate = package.BuildDate;
                    packageModel.Hash = package.Hash;
                    packageModel.ContentHash = package.ContentHash;

                    var queryBuilder = new TrakHoundTableRequestBuilder();
                    queryBuilder.AppendEnsureTable<PackageModel>();
                    queryBuilder.AppendEnsureTable<PackageMetadataModel>();
                    queryBuilder.AppendEnsureTable<PackageDependencyModel>();

                    // A Package uploaded again replaces its rows, so that what's listed matches the new Package file (an old build date wouldn't match its Manifest)
                    queryBuilder.Parameters.Add("packageOrganization", organization);
                    queryBuilder.Parameters.Add("packageUuid", package.Uuid);
                    queryBuilder.AppendQuery($"delete from \"{PackageMetadataModel.SchemaName}\".\"{PackageMetadataModel.TableName}\" where \"organization\" = @packageOrganization and \"package_uuid\" = @packageUuid;");
                    queryBuilder.AppendQuery($"delete from \"{PackageDependencyModel.SchemaName}\".\"{PackageDependencyModel.TableName}\" where \"organization\" = @packageOrganization and \"package_uuid\" = @packageUuid;");

                    queryBuilder.AppendInsert(new[] { packageModel }, true);

                    //var queries = new List<string>();
                    //queries.Add(TrakHoundTables.GenerateCreate<PackageModel>());
                    //queries.Add(TrakHoundTables.GenerateInsert(packageModel));

                    if (!package.Metadata.IsNullOrEmpty())
                    {
                        var packageMetadatas = new List<PackageMetadataModel>();

                        foreach (var metadata in package.Metadata)
                        {
                            if (metadata.Value != null)
                            {
                                var packageMetadata = new PackageMetadataModel();
                                packageMetadata.Organization = organization;
                                packageMetadata.PackageUuid = package.Uuid;
                                packageMetadata.Name = metadata.Key;
                                packageMetadata.Value = metadata.Value.ToString();
                                packageMetadatas.Add(packageMetadata);
                            }
                        }

                        queryBuilder.AppendInsert(packageMetadatas);

                        //queries.Add(TrakHoundTables.GenerateCreate<PackageMetadataModel>());
                        //queries.AddRange(TrakHoundTables.GenerateInserts(packageMetadatas));
                    }

                    var packageDependencies = package.Dependencies?
                        .Where(o => !string.IsNullOrEmpty(o?.Id))
                        .GroupBy(o => o.Id)
                        .Select(o => o.First())
                        .Select(o => new PackageDependencyModel
                        {
                            Organization = organization,
                            PackageUuid = package.Uuid,
                            DependencyId = o.Id,
                            Version = o.Version,
                            MinimumVersion = o.MinimumVersion
                        })
                        .ToList();

                    if (!packageDependencies.IsNullOrEmpty())
                    {
                        queryBuilder.AppendInsert(packageDependencies);
                    }

                    //var query = string.Join("\r\n", queries);

                    var writeResponse = await Client.Services.Tables.ExecuteNonQuery(queryBuilder.Build());
                    //var writeResponse = await Client.Services.Tables.ExecuteNonQuery(query);
                    if (writeResponse.Success)
                    {
                        return true;
                    }
                    else
                    {

                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Read the contents of a Package file from the Blobs service
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="packageUuid">UUID of the Package</param>
        /// <returns>A Stream of the Package file contents, or null if not found</returns>
        public async Task<Stream> ReadPackageStream(string organization, string packageUuid)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageUuid))
            {
                var key = $"{organization}:{packageUuid}".ToSHA256Hash();

                var blobResponse = await Client.Services.Blobs.Download(_blobCollection, key);
                if (blobResponse.Success)
                {
                    return blobResponse.Result;
                }
            }

            return null;
        }

        /// <summary>
        /// Create a ZIP archive containing multiple Packages. Not yet implemented (always returns null)
        /// </summary>
        /// <param name="organization">Organization the Packages belong to</param>
        /// <param name="packageQueries">Queries identifying the Packages to include in the archive</param>
        /// <returns>A Stream of the ZIP archive contents</returns>
        public async Task<Stream> DownloadArchiveStream(string organization, IEnumerable<TrakHoundPackageQuery> packageQueries)
        {
            //var bytes = await DownloadArchiveBytes(organization, packageQueries);
            //if (bytes != null)
            //{
            //    try
            //    {
            //        return new MemoryStream(bytes);
            //    }
            //    catch { }
            //}

            return null;
        }

        /// <summary>
        /// Upload the contents of a Package file to the Blobs service
        /// </summary>
        /// <param name="organization">Organization the Package belongs to</param>
        /// <param name="package">Package information used to generate the Blob key</param>
        /// <param name="packageContent">Package file contents</param>
        /// <returns>True if the upload was successful</returns>
        public async Task<bool> UploadPackage(string organization, TrakHoundPackage package, Stream packageContent)
        {
            if (!string.IsNullOrEmpty(organization) && package != null && package.Uuid != null && packageContent != null)
            {
                var key = $"{organization}:{package.Uuid}".ToSHA256Hash();

                var blobResponse = await Client.Services.Blobs.Upload(_blobCollection, key, packageContent);
                return blobResponse.Success;
            }

            return false;
        }


        //public async Task<bool> DeletePackage(string organization, string packageId)
        //{
        //    if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageId))
        //    {
        //        //var path = $"{RootPath}/{organization}/Packages/*/{packageId}";

        //        //return await _client.Entities.DeleteObject(path);
        //    }

        //    return false;
        //}

        //public async Task<bool> DeletePackageVersion(string organization, string packageUuid)
        //{
        //    if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageUuid))
        //    {
        //        //var path = $"{RootPath}/{organization}/packages/*/{packageUuid}";

        //        //return await _client.Entities.DeleteObject(path);
        //    }

        //    return false;
        //}
    }
}
