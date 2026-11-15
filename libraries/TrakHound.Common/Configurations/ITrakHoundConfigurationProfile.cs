// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Configurations
{
    /// <summary>
    /// A named set of Configurations (grouped by Category) that are stored as YAML files in the Profile's directory
    /// </summary>
    public interface ITrakHoundConfigurationProfile : IDisposable
    {
        /// <summary>
        /// All of the Configurations currently loaded in the Profile
        /// </summary>
        IEnumerable<ITrakHoundConfiguration> Configurations { get; }

        /// <summary>
        /// The unique Identifier of the Profile
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The directory the Profile's Configuration files are stored in
        /// </summary>
        string Path { get; }

        /// <summary>
        /// The display Name of the Profile
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// The Version of the Profile
        /// </summary>
        string Version { get; set; }

        /// <summary>
        /// Whether the Profile is enabled
        /// </summary>
        bool Enabled { get; set; }


        /// <summary>
        /// Raised when a Configuration is loaded, added, or changed (including when its file is updated).
        /// Use ConfigurationChanged to tell these apart
        /// </summary>
        event EventHandler<ITrakHoundConfiguration> ConfigurationAdded;

        /// <summary>
        /// Raised when a Configuration is removed (including when its file is deleted)
        /// </summary>
        event EventHandler<ITrakHoundConfiguration> ConfigurationRemoved;

        /// <summary>
        /// Raised when a Configuration is Loaded, Added, Updated, or Removed (see ChangeType). Raised along with ConfigurationAdded and ConfigurationRemoved
        /// </summary>
        event EventHandler<TrakHoundConfigurationChangedArgs> ConfigurationChanged;


        /// <summary>
        /// Gets the loaded Configurations in the Category that are of type TConfiguration (empty if the Category has none)
        /// </summary>
        IEnumerable<TConfiguration> Get<TConfiguration>(string category) where TConfiguration : ITrakHoundConfiguration;

        /// <summary>
        /// Gets the loaded Configuration with the specified Category and Id (default if not found or not of type TConfiguration)
        /// </summary>
        TConfiguration Get<TConfiguration>(string category, string configurationId) where TConfiguration : ITrakHoundConfiguration;

        /// <summary>
        /// Gets the loaded Configuration with the specified Category and Id (null if not found)
        /// </summary>
        object Get(string category, string configurationId);


        /// <summary>
        /// Adds (or replaces, if its Hash has changed) a Configuration, optionally saving it to file with its secret Parameters encrypted
        /// </summary>
        void Add(ITrakHoundConfiguration configuration, bool save = false);

        /// <summary>
        /// Copies a Configuration file into the Category directory (if a file with the same name does not already exist)
        /// </summary>
        void Add(string category, string configurationPath);

        /// <summary>
        /// Removes the Configuration with the specified Category and Id and deletes its file
        /// </summary>
        void Remove(string category, string configurationId);

        /// <summary>
        /// Removes all Configurations in the Category and deletes their files
        /// </summary>
        void Clear(string category);


        /// <summary>
        /// Reads all of the Configuration files in the Category directory (without loading them into the Profile)
        /// </summary>
        IEnumerable<TConfiguration> Read<TConfiguration>(string category) where TConfiguration : ITrakHoundConfiguration;

        /// <summary>
        /// Reads the Configuration file with the specified Category and Id (without loading it into the Profile). Returns default if the file doesn't exist or can't be read
        /// </summary>
        TConfiguration Read<TConfiguration>(string category, string configurationId) where TConfiguration : ITrakHoundConfiguration;


        /// <summary>
        /// Saves the loaded Configuration with the specified Category and Id to a YAML file
        /// </summary>
        void Save(string category, string configurationId);


        /// <summary>
        /// Deletes the YAML file for the Configuration with the specified Category and Id
        /// </summary>
        void Delete(string category, string configurationId);


        /// <summary>
        /// Reads and loads all of the Configurations in the Category, then watches the Category directory for changes.
        /// The Category is stored as TConfiguration (Configurations of another type can't be added to it). A Category that is already loaded is not loaded again
        /// </summary>
        void Load<TConfiguration>(string category) where TConfiguration : ITrakHoundConfiguration;
    }
}
