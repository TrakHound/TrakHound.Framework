using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Packages.Api.Models;
using TrakHound.Services.Tables;

namespace TrakHound.Packages.Api
{
    public class Service
    {
        private const string _blobCollection = "trakhound.packages";

        private readonly ITrakHoundClient _client;


        public struct PackageRequest
        {
            public string Id { get; set; }
            public string Version { get; set; }
        }


        public Service(ITrakHoundClient client)
        {
            _client = client;
        }


        //public async Task<TrakHoundPackageJson> Get(string organization, string packageUuid)
        //{
        //    if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageUuid))
        //    {
        //        var queryBuilder = TrakHoundTable.CreateBuilder();
        //        queryBuilder.Add($"select * from [{PackageModel.TableName}] where [organization] = '{organization}' and [uuid] = '{packageUuid}';");
        //        queryBuilder.Add($"select * from [{PackageMetadataModel.TableName}] where [organization] = '{organization}' and [package_uuid] = '{packageUuid}';");
        //        var query = queryBuilder.Build();

        //        var queryResponse = await _client.Services.Tables.ExecuteQuery(query);
        //        if (queryResponse.Success)
        //        {
        //            if (queryResponse.Result != null)
        //            {
        //                var packageModel = queryResponse.Result[0].ReadSingle<PackageModel>();
        //                var metadataModels = queryResponse.Result[1].ReadMultiple<PackageMetadataModel>();

        //                var package = new TrakHoundPackageJson();
        //                package.Uuid = packageModel.Uuid;
        //                package.Id = packageModel.Id;
        //                package.Version = new System.Version(packageModel.VersionMajor, packageModel.VersionMinor, packageModel.VersionPatch).ToString();
        //                package.Category = packageModel.Category;
        //                package.BuildDate = packageModel.BuildDate;
        //                package.Hash = packageModel.Hash;

        //                if (!metadataModels.IsNullOrEmpty())
        //                {
        //                    var packageMetadata = new Dictionary<string, object>();
        //                    foreach (var packageMetadataModel in metadataModels)
        //                    {
        //                        packageMetadata.Add(packageMetadataModel.Name, packageMetadataModel.Value);
        //                    }
        //                    package.Metadata = packageMetadata;
        //                }

        //                return package;
        //            }
        //            else
        //            {
        //                return null;
        //            }
        //        }
        //        else
        //        {

        //        }
        //    }

        //    return null;
        //}

        


        //public async Task<TrakHoundPackageJson> Get(string organization, string packageId, string packageVersion)
        //{
        //    if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(packageVersion))
        //    {
        //        if (packageVersion.Contains("*"))
        //        {
        //            return await GetLatest(organization, packageId);
        //        }
        //        else
        //        {
        //            var version = packageVersion.ToVersion();
        //            if (version != null)
        //            {
        //                var versionMajor = version.Major;
        //                var versionMinor = version.Minor;
        //                var versionPatch = version.Build;

        //                var query = $"select top 1 [uuid] from [{PackageModel.TableName}] where [organization] = '{organization}' and [id] = '{packageId}' and [version_major] = {versionMajor} and [version_minor] = {versionMinor} and [version_patch] = {versionPatch};";

        //                var uuidResponse = await _client.Services.Tables.ReadSingleString(query);
        //                if (uuidResponse.Success)
        //                {
        //                    var packageUuid = uuidResponse.Result;
        //                    if (!string.IsNullOrEmpty(packageUuid))
        //                    {
        //                        return await Get(organization, packageUuid);
        //                    }
        //                }
        //            }
        //        }
        //    }

        //    return null;
        //}

		public async Task<IEnumerable<TrakHoundPackage>> Get(string organization, IEnumerable<PackageRequest> requests)
		{
			if (!string.IsNullOrEmpty(organization) && !requests.IsNullOrEmpty())
			{
    //            var packages = new List<TrakHoundPackage>();

    //            var latestRequests = requests.Where(o => o.Version.Contains("*"));
    //            var versionRequests = requests.Where(o => !o.Version.Contains("*"));

    //            // Get Latest Packages
    //            if (!latestRequests.IsNullOrEmpty())
    //            {
    //                packages.AddRange(await GetLatest(organization, latestRequests.Select(o => o.Id).Distinct()));
    //            }

    //            // Get Packages by Version
				//if (!versionRequests.IsNullOrEmpty())
				//{
    //                var paths = new List<string>();
    //                foreach (var request in versionRequests) paths.Add($"{RootPath}/{organization}/Packages/*/{request.Id}/Versions/{request.Version}");
    //                //foreach (var request in versionRequests) paths.Add($"{RootPath}/{organization}/**/Packages/*/{request.Id}/Versions/{request.Version}");

    //                var objs = await _client.Entities.GetObjects(paths);
    //                if (!objs.IsNullOrEmpty())
    //                {
    //                    var packagePaths = objs.Select(o => o.Path);
    //                    packages.AddRange(await CreatePackages(packagePaths));
    //                }
				//}

    //            if (!packages.IsNullOrEmpty())
    //            {
    //                return packages;
    //            }
			}

			return null;
		}

		public async Task<IEnumerable<TrakHoundPackage>> Get(string organization, string category, IEnumerable<PackageRequest> requests)
		{
			if (!string.IsNullOrEmpty(organization) && !requests.IsNullOrEmpty())
			{
				//var packages = new List<TrakHoundPackage>();

				//var latestRequests = requests.Where(o => o.Version.Contains("*"));
				//var versionRequests = requests.Where(o => !o.Version.Contains("*"));

				//// Get Latest Packages
				//if (!latestRequests.IsNullOrEmpty())
				//{
				//	packages.AddRange(await GetLatest(organization, category, latestRequests.Select(o => o.Id).Distinct()));
				//}

				//// Get Packages by Version
				//if (!versionRequests.IsNullOrEmpty())
				//{
				//	var paths = new List<string>();
				//	foreach (var request in versionRequests) paths.Add($"{RootPath}/{organization}/Packages/{category}/{request.Id}/Versions/{request.Version}");
				//	//foreach (var request in versionRequests) paths.Add($"{RootPath}/{organization}/**/Packages/*/{request.Id}/Versions/{request.Version}");

				//	var objs = await _client.Entities.GetObjects(paths);
				//	if (!objs.IsNullOrEmpty())
				//	{
				//		var packagePaths = objs.Select(o => o.Path);
				//		packages.AddRange(await CreatePackages(packagePaths));
				//	}
				//}

				//if (!packages.IsNullOrEmpty())
				//{
				//	return packages;
				//}
			}

			return null;
		}


		//public async Task<TrakHoundPackageJson> GetLatest(string organization, string packageId)
  //      {
  //          if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageId))
  //          {
  //              var query = $"select top 1 [uuid] from [{PackageModel.TableName}] where [organization] = '{organization}' and [id] = '{packageId}' order by [version_major] desc, [version_minor] desc, [version_patch] desc;";

  //              var uuidResponse = await _client.Services.Tables.ReadSingleString(query);
  //              if (uuidResponse.Success)
  //              {
  //                  var packageUuid = uuidResponse.Result;
  //                  if (!string.IsNullOrEmpty(packageUuid))
  //                  {
  //                      return await Get(organization, packageUuid);
  //                  }
  //              }
  //          }

  //          return null;
  //      }

		public async Task<IEnumerable<TrakHoundPackage>> GetLatest(string organization, IEnumerable<string> packageIds)
		{
			if (!string.IsNullOrEmpty(organization) && !packageIds.IsNullOrEmpty())
			{
    //            var paths = new List<string>();
    //            foreach (var packageId in packageIds)
    //            {
    //                paths.Add($"{RootPath}/{organization}/Packages/*/{packageId}/Latest");
    //                //paths.Add($"{RootPath}/{organization}/Packages/**/{packageId}/Latest");
    //            }

				//var packageVersions = (await _client.Entities.GetStrings(paths))?.ToDictionary(o=> o.Object, o => o.Value);
				//if (!packageVersions.IsNullOrEmpty())
				//{
    //                var requests = new List<PackageRequest>();
    //                foreach (var packageVersion in packageVersions)
    //                {
    //                    var request = new PackageRequest();
    //                    request.Id = TrakHoundPath.GetObject(TrakHoundPath.GetParentPath(packageVersion.Key));
    //                    request.Version = packageVersion.Value;
    //                    requests.Add(request);
    //                }

				//	return await Get(organization, requests);
				//}
			}

			return null;
		}

	
		public async Task<IEnumerable<string>> GetVersions(string organization, string packageId)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageId))
            {
                var query = $"select concat([version_major], '.', [version_minor], '.', [version_patch]) as [version] from [{PackageModel.TableName}] where [organization] = '{organization}' and [id] = '{packageId}' order by [version];";

                var versionsResponse = await _client.Services.Tables.ReadMultipleString(query);
                if (versionsResponse.Success)
                {
                    return versionsResponse.Result;
                }
            }

            return null;
        }


		private async Task<TrakHoundPackage> CreatePackage(string packagePath)
        {
            //var package = new TrakHoundPackage();
            //package.Category = TrakHoundPath.GetPathParameter(packagePath, $"{RootPath}/{{organization}}/Packages/{{category}}", "category");
            //package.Id = TrakHoundPath.GetPathParameter(packagePath, $"{RootPath}/{{organization}}/Packages/{{category}}/{{packageId}}", "packageId");
            //package.Version = TrakHoundPath.GetPathParameter(packagePath, $"{RootPath}/{{organization}}/Packages/{{category}}/{{packageId}}/Versions/{{packageVersion}}", "packageVersion");

            //// Set Build Date
            //var buildDate = await _client.Entities.GetTimestampValue(TrakHoundPath.Combine(packagePath, "BuildDate"));
            //if (buildDate != null) package.BuildDate = buildDate.Value;

            //// Set Package Metadata
            //package.Metadata = (await _client.Entities.GetHashValues(TrakHoundPath.Combine(packagePath, "Metadata")))?.FirstOrDefault().Value?.ToDictionary(o => o.Key, o => (object)o.Value);



            //// Set Package Metadata
            //if (!packageVersionEntity.Metadata.IsNullOrEmpty())
            //{
            //    var profileMetadata = new Dictionary<string, object>();

            //    foreach (var entityMetadata in packageVersionEntity.Metadata)
            //    {
            //        if (entityMetadata.Name != "Uuid" && entityMetadata.Name != "Hash" && entityMetadata.Name != "BuildDate")
            //        {
            //            profileMetadata.Add(entityMetadata.Name, entityMetadata.Value);
            //        }
            //    }

            //    package.Metadata = profileMetadata;
            //}

            //var dependenciesEntity = TrakQL.Match("dependencies", packageVersionEntity)?.FirstOrDefault();
            //if (dependenciesEntity != null)
            //{
            //    var dependencyHashes = await _client.Entities.Objects.Hashes.QueryByObjectUuid(dependenciesEntity.Uuid);
            //    if (!dependencyHashes.IsNullOrEmpty())
            //    {
            //        var dependencies = new List<ITrakHoundPackageDependency>();

            //        foreach (var dependencyHash in dependencyHashes)
            //        {
            //            var dependency = new TrakHoundPackageDependency();
            //            dependency.Id = dependencyHash.Key;
            //            dependency.Version = dependencyHash.Value;
            //            dependencies.Add(dependency);
            //        }

            //        package.Dependencies = dependencies;
            //    }
            //}

            //return package;
            return null;
        }

		private async Task<IEnumerable<TrakHoundPackage>> CreatePackages(IEnumerable<string> packagePaths)
		{
            //         var packages = new List<TrakHoundPackage>();

            //         var buildDates = await _client.Entities.GetTimestamps(packagePaths.Select(o => TrakHoundPath.Combine(o, "BuildDate")));
            //         var dBuildDates = buildDates?.ToDictionary(o => o.Object, o => o.Value);

            //         var metadata = await _client.Entities.GetHashes(packagePaths.Select(o => TrakHoundPath.Combine(o, "Metadata")));
            //         var dMetadata = metadata?.ToListDictionary(o => o.Object);

            //         foreach (var packagePath in packagePaths)
            //         {
            //	var package = new TrakHoundPackage();
            //	package.Category = TrakHoundPath.GetPathParameter(packagePath, $"{RootPath}/{{organization}}/Packages/{{category}}", "category");
            //	package.Id = TrakHoundPath.GetPathParameter(packagePath, $"{RootPath}/{{organization}}/Packages/{{category}}/{{packageId}}", "packageId");
            //	package.Version = TrakHoundPath.GetPathParameter(packagePath, $"{RootPath}/{{organization}}/Packages/{{category}}/{{packageId}}/Versions/{{packageVersion}}", "packageVersion");

            //	// Set Build Date
            //	var buildDate = dBuildDates?.GetValueOrDefault(TrakHoundPath.Combine(packagePath, "BuildDate"));
            //	if (buildDate != null) package.BuildDate = buildDate.Value;

            //	// Set Package Metadata
            //	package.Metadata = dMetadata?.Get(TrakHoundPath.Combine(packagePath, "Metadata"))?.ToDictionary(o => o.Key, o => (object)o.Value);

            //             packages.Add(package);
            //         }

            //return packages;

            return null;
		}

        //private async Task<TrakHoundPackage> CreatePackage(ITrakHoundObjectEntityModel packageVersionEntity)
        //{
        //    var package = new TrakHoundPackage();
        //    package.Category = packageVersionEntity.Parent?.Parent?.Name;
        //    package.Id = packageVersionEntity.Parent?.Name;
        //    package.Version = packageVersionEntity.Name;
        //    package.BuildDate = packageVersionEntity.GetMetadata("BuildDate").ToDateTime();

        //    // Set Package Metadata
        //    if (!packageVersionEntity.Metadata.IsNullOrEmpty())
        //    {
        //        var profileMetadata = new Dictionary<string, object>();

        //        foreach (var entityMetadata in packageVersionEntity.Metadata)
        //        {
        //            if (entityMetadata.Name != "Uuid" && entityMetadata.Name != "Hash" && entityMetadata.Name != "BuildDate")
        //            {
        //                profileMetadata.Add(entityMetadata.Name, entityMetadata.Value);
        //            }
        //        }

        //        package.Metadata = profileMetadata;
        //    }

        //    var dependenciesEntity = TrakQL.Match("dependencies", packageVersionEntity)?.FirstOrDefault();
        //    if (dependenciesEntity != null)
        //    {
        //        var dependencyHashes = await _client.Entities.Objects.Hashes.QueryByObjectUuid(dependenciesEntity.Uuid);
        //        if (!dependencyHashes.IsNullOrEmpty())
        //        {
        //            var dependencies = new List<ITrakHoundPackageDependency>();

        //            foreach (var dependencyHash in dependencyHashes)
        //            {
        //                var dependency = new TrakHoundPackageDependency();
        //                dependency.Id = dependencyHash.Key;
        //                dependency.Version = dependencyHash.Value;
        //                dependencies.Add(dependency);
        //            }

        //            package.Dependencies = dependencies;
        //        }
        //    }

        //    return package;
        //}

        public async Task<IEnumerable<TrakHoundPackageJson>> QueryLatest(string organization)
        {
            if (!string.IsNullOrEmpty(organization))
            {
                var query = $"select [uuid] from [{PackageModel.TableName}_latest_view] where [organization] = '{organization}';";

                var uuidResponse = await _client.Services.Tables.ReadMultipleString(query);
                if (uuidResponse.Success)
                {
                    return await Get(organization, uuidResponse.Result);

                    //query = TrakHoundTable.CreateTableVariable("uuids", "varchar(255)", uuidResponse.Result);
                    //query += $"select * from [{PackageModel.TableName}] as [package]";
                    //query += $" inner join @uuids as [uuids] on [package].[uuid] = [uuids].[value]";
                    //query += $" where [organization] = '{organization}';";

                    //query += $"select * from [{PackageMetadataModel.TableName}] as [metadata]";
                    //query += $" inner join @uuids as [uuids] on [metadata].[package_uuid] = [uuids].[value]";
                    //query += $" where [organization] = '{organization}';";

                    //var queryResponse = await _client.Services.Tables.ExecuteQuery(query);
                    //if (queryResponse.Success)
                    //{
                    //    if (queryResponse.Result != null)
                    //    {
                    //        var packageModels = queryResponse.Result[0].ReadMultiple<PackageModel>();
                    //        var metadataModels = queryResponse.Result[1].ReadMultiple<PackageMetadataModel>();

                    //        if (!packageModels.IsNullOrEmpty())
                    //        {
                    //            var packages = new List<TrakHoundPackageJson>();

                    //            foreach (var packageModel in packageModels)
                    //            {
                    //                var package = new TrakHoundPackageJson();
                    //                package.Uuid = packageModel.Uuid;
                    //                package.Id = packageModel.Id;
                    //                package.Version = new System.Version(packageModel.VersionMajor, packageModel.VersionMinor, packageModel.VersionPatch).ToString();
                    //                package.Category = packageModel.Category;
                    //                package.BuildDate = packageModel.BuildDate;
                    //                package.Hash = packageModel.Hash;

                    //                var packageMetadataModels = metadataModels?.Where(o => o.Organization == organization && o.PackageUuid == packageModel.Uuid);
                    //                if (!packageMetadataModels.IsNullOrEmpty())
                    //                {
                    //                    var packageMetadata = new Dictionary<string, object>();
                    //                    foreach (var packageMetadataModel in packageMetadataModels)
                    //                    {
                    //                        packageMetadata.Add(packageMetadataModel.Name, packageMetadataModel.Value);
                    //                    }
                    //                    package.Metadata = packageMetadata;
                    //                }

                    //                packages.Add(package);
                    //            }

                    //            return packages;
                    //        }
                    //    }
                    //    else
                    //    {
                    //        return null;
                    //    }
                    //}
                    //else
                    //{

                    //}
                }
            }

            return null;
        }

        public async Task<IEnumerable<TrakHoundPackageJson>> QueryLatest(string organization, string packageQuery)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageQuery))
            {
                var query = $"select [uuid] from [{PackageModel.TableName}_latest_view] where [organization] = '{organization}' and [id] like '{packageQuery}%';";

                var uuidResponse = await _client.Services.Tables.ReadMultipleString(query);
                if (uuidResponse.Success)
                {
                    return await Get(organization, uuidResponse.Result);

                    //query = TrakHoundTable.CreateTableVariable("uuids", "varchar(255)", uuidResponse.Result);
                    //query += $"select * from [{PackageModel.TableName}] as [package]";
                    //query += $" inner join @uuids as [uuids] on [package].[uuid] = [uuids].[value]";
                    //query += $" where [organization] = '{organization}';";

                    //query += $"select * from [{PackageMetadataModel.TableName}] as [metadata]";
                    //query += $" inner join @uuids as [uuids] on [metadata].[package_uuid] = [uuids].[value]";
                    //query += $" where [organization] = '{organization}';";

                    //var queryResponse = await _client.Services.Tables.ExecuteQuery(query);
                    //if (queryResponse.Success)
                    //{
                    //    if (queryResponse.Result != null)
                    //    {
                    //        var packageModels = queryResponse.Result[0].ReadMultiple<PackageModel>();
                    //        var metadataModels = queryResponse.Result[1].ReadMultiple<PackageMetadataModel>();

                    //        if (!packageModels.IsNullOrEmpty())
                    //        {
                    //            var packages = new List<TrakHoundPackageJson>();

                    //            foreach (var packageModel in packageModels)
                    //            {
                    //                var package = new TrakHoundPackageJson();
                    //                package.Uuid = packageModel.Uuid;
                    //                package.Id = packageModel.Id;
                    //                package.Version = new System.Version(packageModel.VersionMajor, packageModel.VersionMinor, packageModel.VersionPatch).ToString();
                    //                package.Category = packageModel.Category;
                    //                package.BuildDate = packageModel.BuildDate;
                    //                package.Hash = packageModel.Hash;

                    //                var packageMetadataModels = metadataModels?.Where(o => o.Organization == organization && o.PackageUuid == packageModel.Uuid);
                    //                if (!packageMetadataModels.IsNullOrEmpty())
                    //                {
                    //                    var packageMetadata = new Dictionary<string, object>();
                    //                    foreach (var packageMetadataModel in packageMetadataModels)
                    //                    {
                    //                        packageMetadata.Add(packageMetadataModel.Name, packageMetadataModel.Value);
                    //                    }
                    //                    package.Metadata = packageMetadata;
                    //                }

                    //                packages.Add(package);
                    //            }

                    //            return packages;
                    //        }
                    //    }
                    //    else
                    //    {
                    //        return null;
                    //    }
                    //}
                    //else
                    //{

                    //}
                }
            }

            return null;
        }

        public async Task<IEnumerable<TrakHoundPackageJson>> QueryLatestByCategory(string organization, string category)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(category))
            {
                var query = $"select [uuid] from [{PackageModel.TableName}_latest_view] where [organization] = '{organization}' and [category] = '{category}';";

                var uuidResponse = await _client.Services.Tables.ReadMultipleString(query);
                if (uuidResponse.Success)
                {
                    return await Get(organization, uuidResponse.Result);

                    //query = TrakHoundTable.CreateTableVariable("uuids", "varchar(255)", uuidResponse.Result);
                    //query += $"select * from [{PackageModel.TableName}] as [package]";
                    //query += $" inner join @uuids as [uuids] on [package].[uuid] = [uuids].[value]";
                    //query += $" where [organization] = '{organization}';";

                    //query += $"select * from [{PackageMetadataModel.TableName}] as [metadata]";
                    //query += $" inner join @uuids as [uuids] on [metadata].[package_uuid] = [uuids].[value]";
                    //query += $" where [organization] = '{organization}';";

                    //var queryResponse = await _client.Services.Tables.ExecuteQuery(query);
                    //if (queryResponse.Success)
                    //{
                    //    if (queryResponse.Result != null)
                    //    {
                    //        var packageModels = queryResponse.Result[0].ReadMultiple<PackageModel>();
                    //        var metadataModels = queryResponse.Result[1].ReadMultiple<PackageMetadataModel>();

                    //        if (!packageModels.IsNullOrEmpty())
                    //        {
                    //            var packages = new List<TrakHoundPackageJson>();

                    //            foreach (var packageModel in packageModels)
                    //            {
                    //                var package = new TrakHoundPackageJson();
                    //                package.Uuid = packageModel.Uuid;
                    //                package.Id = packageModel.Id;
                    //                package.Version = new System.Version(packageModel.VersionMajor, packageModel.VersionMinor, packageModel.VersionPatch).ToString();
                    //                package.Category = packageModel.Category;
                    //                package.BuildDate = packageModel.BuildDate;
                    //                package.Hash = packageModel.Hash;

                    //                var packageMetadataModels = metadataModels?.Where(o => o.Organization == organization && o.PackageUuid == packageModel.Uuid);
                    //                if (!packageMetadataModels.IsNullOrEmpty())
                    //                {
                    //                    var packageMetadata = new Dictionary<string, object>();
                    //                    foreach (var packageMetadataModel in packageMetadataModels)
                    //                    {
                    //                        packageMetadata.Add(packageMetadataModel.Name, packageMetadataModel.Value);
                    //                    }
                    //                    package.Metadata = packageMetadata;
                    //                }

                    //                packages.Add(package);
                    //            }

                    //            return packages;
                    //        }                            
                    //    }
                    //    else
                    //    {
                    //        return null;
                    //    }
                    //}
                    //else
                    //{

                    //}
                }
            }

            return null;
        }

        public async Task<IEnumerable<TrakHoundPackageJson>> QueryLatestByCategory(string organization, string category, string packageQuery)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(packageQuery))
            {
                var query = $"select [uuid] from [{PackageModel.TableName}_latest_view] where [organization] = '{organization}' and [category] = '{category}' and [id] like '{packageQuery}%';";

                var uuidResponse = await _client.Services.Tables.ReadMultipleString(query);
                if (uuidResponse.Success)
                {
                    return await Get(organization, uuidResponse.Result);

                    //query = TrakHoundTable.CreateTableVariable("uuids", "varchar(255)", uuidResponse.Result);
                    //query += $"select * from [{PackageModel.TableName}] as [package]";
                    //query += $" inner join @uuids as [uuids] on [package].[uuid] = [uuids].[value]";
                    //query += $" where [organization] = '{organization}';";

                    //query += $"select * from [{PackageMetadataModel.TableName}] as [metadata]";
                    //query += $" inner join @uuids as [uuids] on [metadata].[package_uuid] = [uuids].[value]";
                    //query += $" where [organization] = '{organization}';";

                    //var queryResponse = await _client.Services.Tables.ExecuteQuery(query);
                    //if (queryResponse.Success)
                    //{
                    //    if (queryResponse.Result != null)
                    //    {
                    //        var packageModels = queryResponse.Result[0].ReadMultiple<PackageModel>();
                    //        var metadataModels = queryResponse.Result[1].ReadMultiple<PackageMetadataModel>();

                    //        if (!packageModels.IsNullOrEmpty())
                    //        {
                    //            var packages = new List<TrakHoundPackageJson>();

                    //            foreach (var packageModel in packageModels)
                    //            {
                    //                var package = new TrakHoundPackageJson();
                    //                package.Uuid = packageModel.Uuid;
                    //                package.Id = packageModel.Id;
                    //                package.Version = new System.Version(packageModel.VersionMajor, packageModel.VersionMinor, packageModel.VersionPatch).ToString();
                    //                package.Category = packageModel.Category;
                    //                package.BuildDate = packageModel.BuildDate;
                    //                package.Hash = packageModel.Hash;

                    //                var packageMetadataModels = metadataModels?.Where(o => o.Organization == organization && o.PackageUuid == packageModel.Uuid);
                    //                if (!packageMetadataModels.IsNullOrEmpty())
                    //                {
                    //                    var packageMetadata = new Dictionary<string, object>();
                    //                    foreach (var packageMetadataModel in packageMetadataModels)
                    //                    {
                    //                        packageMetadata.Add(packageMetadataModel.Name, packageMetadataModel.Value);
                    //                    }
                    //                    package.Metadata = packageMetadata;
                    //                }

                    //                packages.Add(package);
                    //            }

                    //            return packages;
                    //        }
                    //    }
                    //    else
                    //    {
                    //        return null;
                    //    }
                    //}
                    //else
                    //{

                    //}
                }
            }

            return null;
        }


        public async Task<bool> AddPackage(string organization, TrakHoundPackage package)
        {
            if (package != null && !string.IsNullOrEmpty(package.Category) && !string.IsNullOrEmpty(package.Id) && !string.IsNullOrEmpty(package.Version))
            {
                var packageVersion = package.Version.ToVersion();
                if (packageVersion != null)
                {
                    var packageModel = new PackageModel();
                    packageModel.Organization = organization;
                    packageModel.Uuid = package.Uuid;
                    packageModel.Category = package.Category;
                    packageModel.Id = package.Id;
                    packageModel.VersionMajor = packageVersion.Major;
                    packageModel.VersionMinor = packageVersion.Minor;
                    packageModel.VersionPatch = packageVersion.Build;
                    packageModel.BuildDate = package.BuildDate;
                    packageModel.Hash = package.Hash;

                    var queries = new List<string>();
                    queries.Add(TrakHoundTable.GenerateCreate<PackageModel>());
                    queries.Add(TrakHoundTable.GenerateInsert(packageModel));

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

                        queries.Add(TrakHoundTable.GenerateCreate<PackageMetadataModel>());
                        queries.AddRange(TrakHoundTable.GenerateInserts(packageMetadatas));
                    }

                    var query = string.Join("\r\n", queries);

                    var writeResponse = await _client.Services.Tables.ExecuteNonQuery(query);
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


        //public async Task<byte[]> ReadPackageBytes(string organization, string packageUuid)
        //{
        //    if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageUuid))
        //    {
        //        //var package = await Get(organization, packageUuid);
        //        //if (package != null)
        //        //{
        //        //    var path = $"{RootPath}/{organization}/Packages/{package.Category}/{package.Id}/Versions/{package.Version}/Package";

        //        //    return await _client.Entities.GetBlobBytes(path);
        //        //}
        //    }

        //    return null;
        //}

        public async Task<Stream> ReadPackageStream(string organization, string packageUuid)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageUuid))
            {
                var key = $"{organization}:{packageUuid}".ToSHA256Hash();

                var blobResponse = await _client.Services.Blobs.Download(_blobCollection, key);
                if (blobResponse.Success)
                {
                    return blobResponse.Result;
                }
            }

            return null;
        }

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

        //public async Task<byte[]> DownloadArchiveBytes(string organization, IEnumerable<TrakHoundPackageQuery> packageQueries)
        //{
        //    if (!string.IsNullOrEmpty(organization) && !packageQueries.IsNullOrEmpty())
        //    {
        //        //var tempDir = TrakHoundTemp.CreateDirectory();
        //        //var found = false;

        //        //foreach (var packageQuery in packageQueries)
        //        //{
        //        //    var package = await Get(organization, packageQuery.Id, packageQuery.Installed);
        //        //    if (package != null)
        //        //    {
        //        //        var packageBytes = await ReadPackageBytes(organization, package.Uuid);
        //        //        if (packageBytes != null)
        //        //        {
        //        //            var packagePath = Path.Combine(tempDir, package.Uuid);
        //        //            await File.WriteAllBytesAsync(packagePath, packageBytes);
        //        //            found = true;
        //        //        }
        //        //    }
        //        //}

        //        //if (found)
        //        //{
        //        //    var archivedBytes = Files.Zip(tempDir);

        //        //    Directory.Delete(tempDir, true);

        //        //    return archivedBytes;
        //        //}
        //    }

        //    return null;
        //}

        //public async Task<Stream> DownloadArchiveStream(string organization, IEnumerable<TrakHoundPackageQuery> packageQueries)
        //{
        //    var bytes = await DownloadArchiveBytes(organization, packageQueries);
        //    if (bytes != null)
        //    {
        //        try
        //        {
        //            return new MemoryStream(bytes);
        //        }
        //        catch { }
        //    }

        //    return null;
        //}





        //public async Task<bool> AddPackage(string organization, TrakHoundPackage package)
        //{
        //    if (package != null && !string.IsNullOrEmpty(package.Category) && !string.IsNullOrEmpty(package.Id) && !string.IsNullOrEmpty(package.Version))
        //    {
        //        var packagePath = $"{RootPath}/{organization}/Packages/{package.Category}/{package.Id}";
        //        var packageVersionPath = $"{packagePath}/Versions/{package.Version}";


        //        var collection = new TrakHoundEntityTransaction();
        //        collection.Add(new TrakHoundObjectEntry(packageVersionPath));
        //        collection.Add(new TrakHoundStringEntry(TrakHoundPath.Combine(packageVersionPath, "Uuid"), package.Uuid));
        //        collection.Add(new TrakHoundStringEntry(TrakHoundPath.Combine(packageVersionPath, "Hash"), package.Hash));
        //        collection.Add(new TrakHoundTimestampEntry(TrakHoundPath.Combine(packageVersionPath, "BuildDate"), package.BuildDate));

        //        // Set Metadata
        //        if (!package.Metadata.IsNullOrEmpty())
        //        {
        //            var metadata = new Dictionary<string, string>();
        //            foreach (var packageMetadata in package.Metadata)
        //            {
        //                metadata[packageMetadata.Key] = packageMetadata.Value?.ToString();
        //            }
        //            collection.Add(new TrakHoundHashEntry(TrakHoundPath.Combine(packageVersionPath, "Metadata"), metadata));
        //        }


        //        // Set Latest Version
        //        collection.Add(new TrakHoundStringEntry(TrakHoundPath.Combine(packagePath, "Latest"), package.Version));

        //        //collection.Add(CreatePackageEntry(organization, package));
        //        //collection.Add(CreatePackageDependencyEntries(organization, package));

        //        var indexes = new List<EntityIndexRequest>();
        //        indexes.Add(new EntityIndexRequest($"/Management/Organizations/{organization}/Packages/**/Uuid", package.Uuid, TrakHoundObjectPath.GetUuid(packageVersionPath), 0));

        //        if (await _client.Entities.Publish(collection) && await _client.System.Entities.Objects.UpdateIndex(indexes))
        //        {
        //            return true;
        //        }
        //    }

        //    return false;
        //}

        //private static TrakHoundObjectEntry CreatePackageEntry(string organization, TrakHoundPackage package)
        //{
        //    var entry = new TrakHoundObjectEntry();
        //    TrakHoundObjectEntry($"{RootPath}/{organization}/Packages/{package.Category}/{package.Id}/{package.Version}"));

        //    var metadata = new Dictionary<string, string>();
        //    metadata["Uuid"] = package.Uuid;
        //    metadata["Hash"] = package.Hash;
        //    metadata["BuildDate"] = package.BuildDate.ToISO8601String();

        //    if (!package.Metadata.IsNullOrEmpty())
        //    {
        //        foreach (var packageMetadata in package.Metadata)
        //        {
        //            metadata[packageMetadata.Key] = packageMetadata.Value?.ToString();
        //        }
        //    }
        //    entry.Metadata = metadata;

        //    return entry;
        //}

        //private static TrakHoundObjectEntry CreatePackageEntry(string organization, TrakHoundPackage package)
        //{
        //    var entry = new TrakHoundObjectEntry();
        //    entry.Path = $"{RootPath}/{organization}/Packages/{package.Category}/{package.Id}/{package.Version}";

        //    var metadata = new Dictionary<string, string>();
        //    metadata["Uuid"] = package.Uuid;
        //    metadata["Hash"] = package.Hash;
        //    metadata["BuildDate"] = package.BuildDate.ToISO8601String();

        //    if (!package.Metadata.IsNullOrEmpty())
        //    {
        //        foreach (var packageMetadata in package.Metadata)
        //        {
        //            metadata[packageMetadata.Key] = packageMetadata.Value?.ToString();
        //        }
        //    }
        //    entry.Metadata = metadata;

        //    return entry;
        //}

        //private static TrakHoundEntryCollection CreatePackageDependencyEntries(string organization, TrakHoundPackage package)
        //{
        //    var collection = new TrakHoundEntryCollection();

        //    if (!package.Dependencies.IsNullOrEmpty())
        //    {
        //        foreach (var dependency in package.Dependencies)
        //        {
        //            var path = $"{RootPath}/{organization}/packages/{package.Category}/{package.Id}/{package.Version}/dependencies";

        //            collection.Add(new TrakHoundHashEntry(path, dependency.Id, dependency.Version));
        //        }
        //    }

        //    return collection;
        //}

        public async Task<bool> UploadPackage(string organization, TrakHoundPackage package, Stream packageContent)
        {
            if (!string.IsNullOrEmpty(organization) && package != null && package.Uuid != null && packageContent != null)
            {
                var key = $"{organization}:{package.Uuid}".ToSHA256Hash();

                var blobResponse = await _client.Services.Blobs.Upload(_blobCollection, key, packageContent);
                return blobResponse.Success;
            }

            return false;
        }


        public async Task<bool> DeletePackage(string organization, string packageId)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageId))
            {
                //var path = $"{RootPath}/{organization}/Packages/*/{packageId}";

                //return await _client.Entities.DeleteObject(path);
            }

            return false;
        }

        public async Task<bool> DeletePackageVersion(string organization, string packageUuid)
        {
            if (!string.IsNullOrEmpty(organization) && !string.IsNullOrEmpty(packageUuid))
            {
                //var path = $"{RootPath}/{organization}/packages/*/{packageUuid}";

                //return await _client.Entities.DeleteObject(path);
            }

            return false;
        }
    }
}
