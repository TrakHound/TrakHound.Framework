// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using TrakHound.Packages;

namespace TrakHound.Workers
{
    /// <summary>
    /// Creates the Information for a loaded Worker from its Configuration, Type and Package. Kept apart from TrakHoundWorkerInformation so the Information class only describes the data that the System API returns
    /// </summary>
    public static class TrakHoundWorkerInformationFactory
    {
        /// <summary>
        /// Creates Worker Information from the Configuration and Package (the Id and Status are not set). Returns null if the Configuration, Type, or Package is null
        /// </summary>
        public static TrakHoundWorkerInformation Create(ITrakHoundWorkerConfiguration configuration, Type moduleType, TrakHoundPackage package, string volumeId, string packageReadme = null)
        {
            if (configuration != null && moduleType != null && package != null)
            {
                var information = new TrakHoundWorkerInformation();
                information.Name = package.Id;
                information.Description = !string.IsNullOrEmpty(configuration.Description) ? configuration.Description : package?.Metadata?.GetValueOrDefault("description")?.ToString();
                information.ConfigurationId = configuration.Id;
                information.PackageId = configuration.PackageId;
                information.VolumeId = volumeId;
                information.GroupId = configuration.GroupId;

                if (package != null)
                {
                    information.PackageVersion = package.Version;
                    information.PackageBuildDate = package.BuildDate;
                    information.PackageUuid = package.Uuid;
                    information.PackageHash = package.Hash;
                    information.PackageIcon = package.GetMetadata(TrakHoundPackage.ImageName);

                    information.TrakHoundVersion = package.GetMetadata(TrakHoundPackage.TrakHoundVersionName);

                    information.Repository = package.GetMetadata(TrakHoundPackage.RepositoryName);
                    information.RepositoryBranch = package.GetMetadata(TrakHoundPackage.RepositoryBranchName);
                    information.RepositoryDirectory = package.GetMetadata(TrakHoundPackage.RepositoryDirectoryName);
                    information.RepositoryCommit = package.GetMetadata(TrakHoundPackage.RepositoryCommitName);
                }

                information.PackageReadMe = packageReadme;

                return information;
            }

            return null;
        }
    }
}
