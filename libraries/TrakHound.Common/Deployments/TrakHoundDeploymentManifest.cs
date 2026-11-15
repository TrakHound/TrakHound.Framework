// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;

namespace TrakHound.Deployments
{
    /// <summary>
    /// Lists the Configuration files and Packages contained in a Deployment
    /// </summary>
    public class TrakHoundDeploymentManifest
    {
        private const string _configDirectory = "config";
        private const string _packagesDirectory = "packages";


        /// <summary>
        /// The Configuration files in the Deployment
        /// </summary>
        [JsonPropertyName("configurations")]
        public IEnumerable<TrakHoundDeploymentConfigurationManifestItem> Configurations { get; set; }

        /// <summary>
        /// The Packages (each Package Version) in the Deployment
        /// </summary>
        [JsonPropertyName("packages")]
        public IEnumerable<TrakHoundDeploymentPackageManifestItem> Packages { get; set; }

        /// <summary>
        /// The Configuration Profiles the deployment contains (only these are changed when installed). Null in deployments created by earlier versions (all Profiles).
        /// </summary>
        [JsonPropertyName("profiles")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IEnumerable<string> Profiles { get; set; }

        /// <summary>
        /// The Configuration Categories the deployment contains (only these are changed in each Profile when installed). Null for all Categories.
        /// </summary>
        [JsonPropertyName("categories")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IEnumerable<string> Categories { get; set; }

        /// <summary>
        /// The Volumes (IDs) the deployment contains files for. Null if it contains no Volumes.
        /// </summary>
        [JsonPropertyName("volumes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IEnumerable<string> Volumes { get; set; }

        /// <summary>
        /// The Package Variable Collections (IDs) the deployment contains. Null if it contains no Variables.
        /// </summary>
        [JsonPropertyName("variables")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IEnumerable<string> Variables { get; set; }



        /// <summary>
        /// Creates a Manifest from the "config" and "packages" directories in the source path (empty lists if the path doesn't contain them)
        /// </summary>
        public static TrakHoundDeploymentManifest Create(string sourcePath)
        {
            var manifestConfigurations = new List<TrakHoundDeploymentConfigurationManifestItem>();
            var manifestPackages = new List<TrakHoundDeploymentPackageManifestItem>();

            if (!string.IsNullOrEmpty(sourcePath))
            {
                try
                {
                    // Add Configuration Files
                    var configDir = Path.Combine(sourcePath, _configDirectory);
                    if (Directory.Exists(configDir))
                    {
                        var profileDirectories = Directory.GetDirectories(configDir);
                        if (!profileDirectories.IsNullOrEmpty())
                        {
                            foreach (var profileDirectory in profileDirectories)
                            {
                                var files = Directory.GetFiles(profileDirectory, "*.*", SearchOption.AllDirectories);
                                foreach (var file in files)
                                {
                                    var fileInfo = new FileInfo(file);

                                    // Add to Manifest
                                    var manifestItem = new TrakHoundDeploymentConfigurationManifestItem();
                                    manifestItem.Category = Path.GetFileName(Path.GetDirectoryName(file));
                                    manifestItem.Id = Path.GetFileNameWithoutExtension(file);
                                    manifestItem.LastUpdated = fileInfo.LastWriteTime.ToUnixTime();
                                    manifestConfigurations.Add(manifestItem);
                                }
                            }
                        }
                    }

                    // Add Package Files
                    var packagesDir = Path.Combine(sourcePath, _packagesDirectory);
                    if (Directory.Exists(packagesDir))
                    {
                        var categoryDirectories = Directory.GetDirectories(packagesDir);
                        foreach (var categoryDirectory in categoryDirectories)
                        {
                            var packageDirectories = Directory.GetDirectories(categoryDirectory);
                            foreach (var packageDirectory in packageDirectories)
                            {
                                var packageVersionDirectories = Directory.GetDirectories(packageDirectory);
                                foreach (var packageVersionDirectory in packageVersionDirectories)
                                {
                                    // Add to Manifest
                                    var manifestItem = new TrakHoundDeploymentPackageManifestItem();
                                    manifestItem.Category = Path.GetFileName(categoryDirectory);
                                    manifestItem.Id = Path.GetFileName(packageDirectory);
                                    manifestItem.Version = Path.GetFileName(packageVersionDirectory);
                                    manifestPackages.Add(manifestItem);
                                }
                            }
                        }
                    }
                }
                catch
                {

                }
            }

            var manifest = new TrakHoundDeploymentManifest();
            manifest.Configurations = manifestConfigurations;
            manifest.Packages = manifestPackages;
            return manifest;
        }



        /// <summary>
        /// Creates a Manifest of the Configurations that differ between the two Manifests (added, newer, or removed). Packages are not compared.
        /// Returns null if either Manifest is null
        /// </summary>
        public static TrakHoundDeploymentManifest Create(TrakHoundDeploymentManifest baseManifest, TrakHoundDeploymentManifest compareManifest)
        {
            if (baseManifest != null && compareManifest != null)
            {
                var outputConfigurations = new List<TrakHoundDeploymentConfigurationManifestItem>();

                if (!baseManifest.Configurations.IsNullOrEmpty())
                {
                    if (!compareManifest.Configurations.IsNullOrEmpty())
                    {
                        foreach (var item in compareManifest.Configurations)
                        {
                            var baseItem = baseManifest.Configurations.FirstOrDefault(o => o.Category == item.Category && o.Id == item.Id);
                            if (baseItem != null)
                            {
                                if (item.LastUpdated > baseItem.LastUpdated)
                                {
                                    var outputItem = new TrakHoundDeploymentConfigurationManifestItem();
                                    //outputItem.ChangeType = TrakHoundDeploymentChangeType.Modified;
                                    outputItem.Category = item.Category;
                                    outputItem.Id = item.Id;
                                    outputItem.LastUpdated = item.LastUpdated;
                                    outputConfigurations.Add(outputItem);
                                }
                            }
                            else
                            {
                                var outputItem = new TrakHoundDeploymentConfigurationManifestItem();
                                //outputItem.ChangeType = TrakHoundDeploymentChangeType.Added;
                                outputItem.Category = item.Category;
                                outputItem.Id = item.Id;
                                outputItem.LastUpdated = item.LastUpdated;
                                outputConfigurations.Add(outputItem);
                            }
                        }

                        // Detect Deleted Configurations
                        foreach (var item in baseManifest.Configurations)
                        {
                            if (!compareManifest.Configurations.Any(o => o.Category == item.Category && o.Id == item.Id))
                            {
                                var outputItem = new TrakHoundDeploymentConfigurationManifestItem();
                                //outputItem.ChangeType = TrakHoundDeploymentChangeType.Removed;
                                outputItem.Category = item.Category;
                                outputItem.Id = item.Id;
                                outputItem.LastUpdated = item.LastUpdated;
                                outputConfigurations.Add(outputItem);
                            }
                        }
                    }
                    else
                    {
                        outputConfigurations.AddRange(baseManifest.Configurations);
                    }
                }
                else if (!compareManifest.Configurations.IsNullOrEmpty())
                {
                    outputConfigurations.AddRange(compareManifest.Configurations);
                }

                var outputManifest = new TrakHoundDeploymentManifest();
                outputManifest.Configurations = outputConfigurations;
                return outputManifest;
            }

            return null;
        }
    }
}
