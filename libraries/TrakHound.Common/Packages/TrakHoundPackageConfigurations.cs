// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Configurations;
using TrakHound.Functions;
using TrakHound.Workers;

namespace TrakHound.Packages
{
    /// <summary>
    /// The Configuration a Package gets when it is installed (from the Admin interface, or th packages install --configure),
    /// using its ".defaultName", ".defaultRoute" and ".defaultVolumeId" Metadata
    /// </summary>
    public static class TrakHoundPackageConfigurations
    {
        public const string DefaultNameMetadataKey = ".defaultName";
        public const string DefaultRouteMetadataKey = ".defaultRoute";
        public const string DefaultVolumeIdMetadataKey = ".defaultVolumeId";


        /// <summary>
        /// Create the default Configuration for the Package (with a new ID). Returns null for Categories that don't get one (ex. service, identity)
        /// </summary>
        public static ITrakHoundConfiguration CreateDefault(TrakHoundPackage package)
        {
            if (package == null || string.IsNullOrEmpty(package.Id)) return null;

            var defaultRoute = package.GetMetadata(DefaultRouteMetadataKey);
            var defaultVolumeId = package.GetMetadata(DefaultVolumeIdMetadataKey);

            switch (package.Category)
            {
                case "app":
                    return new TrakHoundAppConfiguration
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = package.GetMetadata(DefaultNameMetadataKey),
                        PackageId = package.Id,
                        PackageVersion = "*",
                        Route = defaultRoute,
                        VolumeId = defaultVolumeId
                    };

                case "api":
                    return new TrakHoundApiConfiguration
                    {
                        Id = Guid.NewGuid().ToString(),
                        PackageId = package.Id,
                        PackageVersion = "*",
                        Route = defaultRoute,
                        VolumeId = defaultVolumeId
                    };

                case "function":
                    return new TrakHoundFunctionConfiguration
                    {
                        Id = Guid.NewGuid().ToString(),
                        PackageId = package.Id,
                        PackageVersion = "*",
                        FunctionId = package.Id,
                        VolumeId = defaultVolumeId
                    };

                case "worker":
                    return new TrakHoundWorkerConfiguration
                    {
                        Id = Guid.NewGuid().ToString(),
                        PackageId = package.Id,
                        PackageVersion = "*",
                        VolumeId = defaultVolumeId
                    };
            }

            return null;
        }

        /// <summary>
        /// A copy of the Configuration using the Package's ".defaultVolumeId", for an existing Configuration without a Volume ID that runs this Package version.
        /// Returns null if there is nothing to change. A copy is returned because the Configuration Profile detects changes by comparing against the Configuration it holds
        /// </summary>
        public static ITrakHoundConfiguration WithDefaultVolumeId(ITrakHoundConfiguration configuration, TrakHoundPackage package)
        {
            if (configuration == null || package == null) return null;

            var defaultVolumeId = package.GetMetadata(DefaultVolumeIdMetadataKey);
            if (string.IsNullOrWhiteSpace(defaultVolumeId)) return null;

            switch (configuration)
            {
                case TrakHoundAppConfiguration app when Applies(app.VolumeId, app.PackageId, app.PackageVersion, package):
                    return new TrakHoundAppConfiguration
                    {
                        Id = app.Id,
                        Path = app.Path,
                        Name = app.Name,
                        Description = app.Description,
                        Route = app.Route,
                        VolumeId = defaultVolumeId,
                        RouterId = app.RouterId,
                        GroupId = app.GroupId,
                        PackageId = app.PackageId,
                        PackageVersion = app.PackageVersion,
                        Parameters = app.Parameters
                    };

                case TrakHoundApiConfiguration api when Applies(api.VolumeId, api.PackageId, api.PackageVersion, package):
                    return new TrakHoundApiConfiguration(api) { VolumeId = defaultVolumeId };

                case TrakHoundFunctionConfiguration function when Applies(function.VolumeId, function.PackageId, function.PackageVersion, package):
                    return new TrakHoundFunctionConfiguration(function) { VolumeId = defaultVolumeId };

                case TrakHoundWorkerConfiguration worker when Applies(worker.VolumeId, worker.PackageId, worker.PackageVersion, package):
                    return new TrakHoundWorkerConfiguration(worker) { VolumeId = defaultVolumeId };
            }

            return null;
        }

        private static bool Applies(string volumeId, string packageId, string packageVersion, TrakHoundPackage package)
        {
            if (!string.IsNullOrWhiteSpace(volumeId) || packageId != package.Id) return false;

            // A Configuration pinned to another version isn't running this Package
            return string.IsNullOrEmpty(packageVersion) || packageVersion.Contains(TrakHoundPackage.WilcardVersion) || packageVersion == package.Version;
        }

        /// <summary>
        /// The Package ID of an App, Api, Function or Worker Configuration (null for other Configurations)
        /// </summary>
        public static string GetPackageId(ITrakHoundConfiguration configuration)
        {
            switch (configuration)
            {
                case TrakHoundAppConfiguration app: return app.PackageId;
                case TrakHoundApiConfiguration api: return api.PackageId;
                case TrakHoundFunctionConfiguration function: return function.PackageId;
                case TrakHoundWorkerConfiguration worker: return worker.PackageId;
            }

            return null;
        }

        /// <summary>
        /// Puts an App, Api, Function or Worker Configuration in the Group (ignored for other Configurations)
        /// </summary>
        public static void SetGroupId(ITrakHoundConfiguration configuration, string groupId)
        {
            switch (configuration)
            {
                case TrakHoundAppConfiguration app: app.GroupId = groupId; break;
                case TrakHoundApiConfiguration api: api.GroupId = groupId; break;
                case TrakHoundFunctionConfiguration function: function.GroupId = groupId; break;
                case TrakHoundWorkerConfiguration worker: worker.GroupId = groupId; break;
            }
        }
    }
}
