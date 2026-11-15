// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Functions;
using TrakHound.Packages;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Workers;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Configurations
{
    /// <summary>
    /// A Configuration Profile stored in a directory of YAML files (one subdirectory per Category). Configuration files are watched for changes,
    /// and Configuration Packages installed by the Package Manager are copied into the Profile
    /// </summary>
    public class TrakHoundConfigurationProfile : ITrakHoundConfigurationProfile
    {
        /// <summary>
        /// The Id of the default Configuration Profile
        /// </summary>
        public const string Default = "default";
        private const string _packageCategory = "configuration";
        private const string _fileExtension = ".yaml";
        private const string _tempFileExtension = ".tmp";

        private static readonly StringComparison _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private readonly string _id;
        private readonly string _path;
        private readonly Dictionary<(string Category, string Id), ITrakHoundConfiguration> _configurations = new Dictionary<(string, string), ITrakHoundConfiguration>();
        private readonly Dictionary<string, IDisposable> _watchers = new Dictionary<string, IDisposable>();
        private readonly Dictionary<string, Type> _categoryTypes = new Dictionary<string, Type>(); // Category => the Type it was Loaded as
        private readonly TrakHoundPackageManager _packageManager;
        private readonly object _lock = new object();


        /// <inheritdoc/>
        [YamlIgnore]
        public IEnumerable<ITrakHoundConfiguration> Configurations
        {
            get
            {
                lock (_lock) return _configurations.Values.ToList();
            }
        }

        /// <inheritdoc/>
        [YamlIgnore]
        public string Id => _id;

        /// <inheritdoc/>
        [YamlIgnore]
        public string Path => _path;

        /// <inheritdoc/>
        public string Name { get; set; }

        /// <inheritdoc/>
        public string Version { get; set; }

        /// <inheritdoc/>
        public bool Enabled { get; set; }


        /// <inheritdoc/>
        public event EventHandler<ITrakHoundConfiguration> ConfigurationAdded;

        /// <inheritdoc/>
        public event EventHandler<ITrakHoundConfiguration> ConfigurationRemoved;

        /// <inheritdoc/>
        public event EventHandler<TrakHoundConfigurationChangedArgs> ConfigurationChanged;

        /// <summary>
        /// Raised on the caller's thread as soon as this Profile writes or deletes a Configuration file (with the path of the file)
        /// </summary>
        public event EventHandler<string> ConfigurationFileChanged;

        /// <summary>
        /// Raised when a Configuration file can't be read, written, or deleted (ex. invalid YAML)
        /// </summary>
        public event EventHandler<string> ErrorReceived;


        /// <summary>
        /// Creates a Profile stored in the specified path (or the default directory for the Id if no path is given).
        /// If a Package Manager is specified, Configuration Packages it adds are copied into the Profile
        /// </summary>
        /// <param name="initializeSecrets">
        /// Use the key in the parent directory for encrypted Parameters. The key is global, so a Profile that isn't the running one
        /// (ex. a Deployment Workspace) passes false to keep the running Profile's key
        /// </param>
        public TrakHoundConfigurationProfile(string id, TrakHoundPackageManager packageManager = null, string path = null, bool initializeSecrets = true)
        {
            _id = id;
            _path = !string.IsNullOrEmpty(path) ? path : GetDirectory(id);

            // The key for encrypted Parameters is shared by the Profiles (in the parent directory)
            if (initializeSecrets) TrakHoundSecrets.Initialize(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)));

            if (packageManager != null)
            {
                _packageManager = packageManager;
                _packageManager.PackageAdded += PackageAdded;
            }
        }

        /// <summary>
        /// Unsubscribes from the Package Manager and disposes the Configuration file watchers
        /// </summary>
        public void Dispose()
        {
            if (_packageManager != null)
            {
                _packageManager.PackageAdded -= PackageAdded;
            }

            List<IDisposable> watchers;
            lock (_lock)
            {
                watchers = _watchers.Values.ToList();
                _watchers.Clear();
            }

            foreach (var watcher in watchers)
            {
                if (watcher != null) watcher.Dispose();
            }
        }


        private void PackageAdded(object sender, TrakHoundPackage package)
        {
            if (package != null && package.Category == _packageCategory)
            {
                try
                {
                    var packageDist = System.IO.Path.Combine(package.Location, TrakHoundPackage.DistributableDirectory);
                    var configurationCategoryDirectories = Directory.GetDirectories(packageDist);
                    if (!configurationCategoryDirectories.IsNullOrEmpty())
                    {
                        foreach (var configurationCategoryDirectory in configurationCategoryDirectories)
                        {
                            var configurationFiles = Directory.GetFiles(configurationCategoryDirectory);
                            if (!configurationFiles.IsNullOrEmpty())
                            {
                                var configurationCategory = System.IO.Path.GetFileName(configurationCategoryDirectory);

                                foreach (var configurationFile in configurationFiles)
                                {
                                    Add(configurationCategory, configurationFile);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnError($"Error copying Configurations from Package ({package.Id} v{package.Version}) : {ex.Message}");
                }
            }
        }


        /// <inheritdoc/>
        public IEnumerable<TConfiguration> Get<TConfiguration>(string category) where TConfiguration : ITrakHoundConfiguration
        {
            if (!string.IsNullOrEmpty(category))
            {
                lock (_lock) return _configurations.Values.Where(o => o.Category == category).OfType<TConfiguration>().ToList();
            }

            return Enumerable.Empty<TConfiguration>();
        }

        /// <inheritdoc/>
        public TConfiguration Get<TConfiguration>(string category, string configurationId) where TConfiguration : ITrakHoundConfiguration
        {
            if (Get(category, configurationId) is TConfiguration configuration) return configuration;

            return default;
        }

        /// <inheritdoc/>
        public object Get(string category, string configurationId)
        {
            if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(configurationId))
            {
                lock (_lock) return _configurations.GetValueOrDefault((category, configurationId));
            }

            return null;
        }


        /// <inheritdoc/>
        public void Add(ITrakHoundConfiguration configuration, bool save = false)
        {
            if (configuration != null && !string.IsNullOrEmpty(configuration.Category) && !string.IsNullOrEmpty(configuration.Id))
            {
                // A Configuration that isn't the Category's Type wouldn't be found by Get (and would be read back as the Category's Type)
                Type categoryType;
                lock (_lock) categoryType = _categoryTypes.GetValueOrDefault(configuration.Category);
                if (categoryType != null && !categoryType.IsInstanceOfType(configuration))
                {
                    OnError($"Configuration ({configuration.Category} : {configuration.Id}) is a {configuration.GetType().Name} and was not added (the Category is {categoryType.Name})");
                    return;
                }

                // Secrets are saved encrypted (and kept encrypted in memory so the saved file matches)
                if (save) TrakHoundSecrets.EncryptParameters(GetParameters(configuration));

                var key = (configuration.Category, configuration.Id);
                var add = false;

                ITrakHoundConfiguration existingConfiguration;
                lock (_lock)
                {
                    existingConfiguration = _configurations.GetValueOrDefault(key);
                    if (existingConfiguration != null)
                    {
                        // A Configuration built in code (ex. by an edit in the UI) is saved back to the file it was read from
                        if (string.IsNullOrEmpty(configuration.Path)) configuration.Path = existingConfiguration.Path;

                        // The same Configuration read from a different file (ex. the file was renamed)
                        if (existingConfiguration.Hash == configuration.Hash && !string.IsNullOrEmpty(configuration.Path))
                        {
                            existingConfiguration.Path = configuration.Path;
                        }
                    }

                    if (existingConfiguration == null || existingConfiguration.Hash != configuration.Hash)
                    {
                        _configurations[key] = configuration;
                        add = true;
                    }
                }

                if (existingConfiguration != null && !IsSamePath(existingConfiguration.Path, configuration.Path) && File.Exists(existingConfiguration.Path))
                {
                    OnError($"Duplicate Configuration ({configuration.Category} : {configuration.Id}) in \"{existingConfiguration.Path}\" and \"{configuration.Path}\"");
                }

                if (add)
                {
                    if (save) Save(configuration.Category, configuration.Id);

                    OnConfigurationAdded(configuration, existingConfiguration == null ? TrakHoundConfigurationChangeType.Added : TrakHoundConfigurationChangeType.Updated);
                }
            }
        }

        /// <inheritdoc/>
        public void Add(string category, string configurationPath)
        {
            if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(configurationPath))
            {
                try
                {
                    var categoryDirectory = System.IO.Path.Combine(_path, category);
                    if (!Directory.Exists(categoryDirectory)) Directory.CreateDirectory(categoryDirectory);

                    var configurationId = System.IO.Path.GetFileName(configurationPath);
                    var savePath = System.IO.Path.Combine(categoryDirectory, configurationId);

                    if (!File.Exists(savePath))
                    {
                        File.Copy(configurationPath, savePath);
                    }
                }
                catch (Exception ex)
                {
                    OnError($"Error copying Configuration file \"{configurationPath}\" : {ex.Message}");
                }
            }
        }

        /// <inheritdoc/>
        public void Remove(string category, string configurationId)
        {
            if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(configurationId))
            {
                ITrakHoundConfiguration configuration;
                lock (_lock)
                {
                    configuration = _configurations.GetValueOrDefault((category, configurationId));
                    if (configuration != null) _configurations.Remove((category, configurationId));
                }

                if (configuration != null)
                {
                    DeleteFile(GetFilePath(configuration));

                    OnConfigurationRemoved(configuration);
                }
            }
        }

        /// <inheritdoc/>
        public void Clear(string category)
        {
            if (!string.IsNullOrEmpty(category))
            {
                List<ITrakHoundConfiguration> configurations;
                lock (_lock)
                {
                    configurations = _configurations.Values.Where(o => o.Category == category).ToList();
                    foreach (var configuration in configurations) _configurations.Remove((configuration.Category, configuration.Id));
                }

                foreach (var configuration in configurations)
                {
                    DeleteFile(GetFilePath(configuration));

                    OnConfigurationRemoved(configuration);
                }
            }
        }


        /// <summary>
        /// Reads all of the Configuration files in the Category directory (without loading them into the Profile).
        /// Plain text secret Parameters found in the files are encrypted and the files are updated
        /// </summary>
        public IEnumerable<TConfiguration> Read<TConfiguration>(string category) where TConfiguration : ITrakHoundConfiguration
        {
            var configurations = new List<TConfiguration>();

            if (!string.IsNullOrEmpty(category))
            {
                var categoryDirectory = System.IO.Path.Combine(_path, category);
                if (Directory.Exists(categoryDirectory))
                {
                    var configurationPaths = Directory.GetFiles(categoryDirectory);
                    if (!configurationPaths.IsNullOrEmpty())
                    {
                        foreach (var configurationPath in configurationPaths)
                        {
                            var configuration = ReadFile<TConfiguration>(configurationPath);
                            if (configuration != null)
                            {
                                configurations.Add(configuration);
                            }
                        }
                    }
                }
            }

            return configurations;
        }

        /// <summary>
        /// Reads the file the loaded Configuration was read from (or "{Path}/{category}/{configurationId}.yaml" if it isn't loaded)
        /// without loading it into the Profile. Returns default if the file doesn't exist or can't be read
        /// </summary>
        public TConfiguration Read<TConfiguration>(string category, string configurationId) where TConfiguration : ITrakHoundConfiguration
        {
            if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(configurationId))
            {
                var configurationPath = Get(category, configurationId) is ITrakHoundConfiguration configuration
                    ? GetFilePath(configuration)
                    : GetDefaultFilePath(category, configurationId);

                return ReadFile<TConfiguration>(configurationPath);
            }

            return default;
        }

        /// <summary>
        /// Saves the loaded Configuration with the specified Category and Id to the file it was read from
        /// (or "{Path}/{category}/{configurationId}.yaml" if it wasn't read from a file in the Category directory)
        /// </summary>
        public void Save(string category, string configurationId)
        {
            if (Get(category, configurationId) is ITrakHoundConfiguration configuration)
            {
                var configurationPath = GetFilePath(configuration);

                try
                {
                    var categoryDirectory = System.IO.Path.GetDirectoryName(configurationPath);
                    if (!Directory.Exists(categoryDirectory)) Directory.CreateDirectory(categoryDirectory);

                    var serializer = new SerializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    var yaml = serializer.Serialize(configuration);

                    // Written to a temporary file and then moved over the file so that it is never read partially written
                    var tempPath = configurationPath + _tempFileExtension;
                    File.WriteAllText(tempPath, yaml);
                    File.Move(tempPath, configurationPath, true);

                    configuration.Path = configurationPath;

                    ConfigurationFileChanged?.Invoke(this, configurationPath);
                }
                catch (Exception ex)
                {
                    OnError($"Error saving Configuration ({category} : {configurationId}) to \"{configurationPath}\" : {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Deletes the file the loaded Configuration was read from (or "{Path}/{category}/{configurationId}.yaml" if it isn't loaded)
        /// </summary>
        public void Delete(string category, string configurationId)
        {
            if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(configurationId))
            {
                if (Get(category, configurationId) is ITrakHoundConfiguration configuration)
                {
                    DeleteFile(GetFilePath(configuration));
                }
                else
                {
                    DeleteFile(GetDefaultFilePath(category, configurationId));
                }
            }
        }


        /// <summary>
        /// The file the Configuration was read from, if it is in the Category directory. Otherwise "{Path}/{category}/{Id}.yaml"
        /// </summary>
        private string GetFilePath(ITrakHoundConfiguration configuration)
        {
            var categoryDirectory = System.IO.Path.GetFullPath(System.IO.Path.Combine(_path, configuration.Category));

            if (!string.IsNullOrEmpty(configuration.Path))
            {
                try
                {
                    var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(configuration.Path));
                    if (string.Equals(directory, categoryDirectory, _pathComparison)) return configuration.Path;
                }
                catch { }
            }

            return GetDefaultFilePath(configuration.Category, configuration.Id);
        }

        private string GetDefaultFilePath(string category, string configurationId)
        {
            return System.IO.Path.Combine(_path, category, configurationId + _fileExtension);
        }

        private static bool IsSamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b);

            try
            {
                return string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), _pathComparison);
            }
            catch
            {
                return string.Equals(a, b, _pathComparison);
            }
        }

        private TConfiguration ReadFile<TConfiguration>(string configurationPath) where TConfiguration : ITrakHoundConfiguration
        {
            if (!string.IsNullOrEmpty(configurationPath))
            {
                // A temporary file written by Save (or left behind if a Save was interrupted)
                if (configurationPath.EndsWith(_tempFileExtension, StringComparison.OrdinalIgnoreCase)) return default;

                if (File.Exists(configurationPath))
                {
                    return ReadYaml<TConfiguration>(configurationPath);
                }
            }

            return default;
        }

        private void DeleteFile(string configurationPath)
        {
            if (!string.IsNullOrEmpty(configurationPath))
            {
                try
                {
                    File.Delete(configurationPath);

                    ConfigurationFileChanged?.Invoke(this, configurationPath);
                }
                catch (Exception ex)
                {
                    OnError($"Error deleting Configuration file \"{configurationPath}\" : {ex.Message}");
                }
            }
        }


        private TConfiguration ReadYaml<TConfiguration>(string configurationPath) where TConfiguration : ITrakHoundConfiguration
        {
            if (!string.IsNullOrEmpty(configurationPath))
            {
                try
                {
                    var text = File.ReadAllText(configurationPath);
                    if (!string.IsNullOrEmpty(text))
                    {
                        // Encrypt plain text secrets (ex. a password entered in the file) and update the file
                        var (encryptedText, encryptedCount) = TrakHoundSecrets.EncryptYaml(text);
                        if (encryptedCount > 0)
                        {
                            try
                            {
                                // Written in place (a Changed event for the watcher)
                                File.WriteAllText(configurationPath, encryptedText);
                                text = encryptedText;
                            }
                            catch (Exception ex)
                            {
                                OnError($"Error encrypting secrets in Configuration file \"{configurationPath}\" : {ex.Message}");
                            }
                        }

                        var deserializer = new DeserializerBuilder()
                            .WithNamingConvention(CamelCaseNamingConvention.Instance)
                            .IgnoreUnmatchedProperties()
                            .Build();

                        var configuration = deserializer.Deserialize<TConfiguration>(text);
                        if (configuration != null)
                        {
                            configuration.Path = configurationPath;

                            if (string.IsNullOrEmpty(configuration.Id))
                            {
                                OnError($"Configuration file \"{configurationPath}\" has no Id and was not loaded");
                            }
                        }

                        return configuration;
                    }
                }
                catch (Exception ex)
                {
                    OnError($"Error reading Configuration file \"{configurationPath}\" : {ex.Message}");
                }
            }

            return default;
        }

        private static Dictionary<string, object> GetParameters(ITrakHoundConfiguration configuration)
        {
            switch (configuration)
            {
                case ITrakHoundApiConfiguration o: return o.Parameters;
                case ITrakHoundAppConfiguration o: return o.Parameters;
                case ITrakHoundFunctionConfiguration o: return o.Parameters;
                case ITrakHoundIdentityProviderConfiguration o: return o.Parameters;
                case ITrakHoundServiceConfiguration o: return o.Parameters;
                case ITrakHoundWorkerConfiguration o: return o.Parameters;
                default: return null;
            }
        }

        private void OnError(string message)
        {
            if (ErrorReceived != null) ErrorReceived(this, message);
        }

        private void OnConfigurationAdded(ITrakHoundConfiguration configuration, TrakHoundConfigurationChangeType changeType)
        {
            if (ConfigurationAdded != null) ConfigurationAdded(this, configuration);
            if (ConfigurationChanged != null) ConfigurationChanged(this, new TrakHoundConfigurationChangedArgs(changeType, configuration));
        }

        private void OnConfigurationRemoved(ITrakHoundConfiguration configuration)
        {
            if (ConfigurationRemoved != null) ConfigurationRemoved(this, configuration);
            if (ConfigurationChanged != null) ConfigurationChanged(this, new TrakHoundConfigurationChangedArgs(TrakHoundConfigurationChangeType.Removed, configuration));
        }


        /// <inheritdoc/>
        /// <remarks>The watcher of a Category that is already loaded keeps its Configurations up to date</remarks>
        public void Load<TConfiguration>(string category) where TConfiguration : ITrakHoundConfiguration
        {
            if (!string.IsNullOrEmpty(category))
            {
                var categoryDirectory = System.IO.Path.Combine(_path, category);

                Type categoryType;
                lock (_lock)
                {
                    categoryType = _categoryTypes.GetValueOrDefault(category);
                    if (categoryType == null)
                    {
                        // Reserved so that a concurrent Load of the same Category returns
                        _categoryTypes.Add(category, typeof(TConfiguration));
                        _watchers.Add(categoryDirectory, null);
                    }
                }

                if (categoryType != null)
                {
                    if (categoryType != typeof(TConfiguration))
                    {
                        OnError($"Configuration Category ({category}) is already Loaded as {categoryType.Name} and was not Loaded as {typeof(TConfiguration).Name}");
                    }

                    return;
                }

                var configurations = Read<TConfiguration>(category);
                if (!configurations.IsNullOrEmpty())
                {
                    foreach (var configuration in configurations)
                    {
                        if (!string.IsNullOrEmpty(configuration.Id))
                        {
                            var key = (configuration.Category, configuration.Id);

                            ITrakHoundConfiguration existingConfiguration;
                            lock (_lock)
                            {
                                existingConfiguration = _configurations.GetValueOrDefault(key);
                                _configurations[key] = configuration;
                            }

                            if (existingConfiguration != null && !IsSamePath(existingConfiguration.Path, configuration.Path))
                            {
                                OnError($"Duplicate Configuration ({configuration.Category} : {configuration.Id}) in \"{existingConfiguration.Path}\" and \"{configuration.Path}\"");
                            }

                            OnConfigurationAdded(configuration, TrakHoundConfigurationChangeType.Loaded);
                        }
                    }
                }

                var watcher = new ConfigurationFileWatcher<TConfiguration>(category, categoryDirectory, ReadFile<TConfiguration>);
                watcher.ConfigurationUpdated += ConfigurationFileUpdated;
                watcher.ConfigurationRemoved += ConfigurationFileRemoved;
                watcher.ErrorReceived += (s, message) => OnError($"Configuration file watcher ({category}) : {message}");
                watcher.Start();

                lock (_lock) _watchers[categoryDirectory] = watcher;
            }
        }

        private void ConfigurationFileUpdated<TConfiguration>(object sender, TConfiguration configuration) where TConfiguration : ITrakHoundConfiguration
        {
            if (configuration != null && !string.IsNullOrEmpty(configuration.Category) && !string.IsNullOrEmpty(configuration.Id))
            {
                // The watcher already waits for a series of changes to a file to finish
                Add(configuration);
            }
        }

        private void ConfigurationFileRemoved(object sender, ConfigurationFileRemovedArgs args)
        {
            // The file was deleted outside of the Profile, so only unload the Configuration that was read from it
            // (the file's name may not match the Id in its content, and a file with the same Id may have replaced it)
            ITrakHoundConfiguration configuration;
            lock (_lock)
            {
                configuration = _configurations.Values.FirstOrDefault(o => o.Category == args.Category && IsSamePath(o.Path, args.Path));
                if (configuration != null) _configurations.Remove((configuration.Category, configuration.Id));
            }

            if (configuration != null) OnConfigurationRemoved(configuration);
        }

        /// <summary>
        /// Reads and loads all of the Configurations in each of the Categories, then watches the Category directories for changes
        /// </summary>
        public void Load<TConfiguration>(IEnumerable<string> categories) where TConfiguration : ITrakHoundConfiguration
        {
            if (!categories.IsNullOrEmpty())
            {
                foreach (var category in categories)
                {
                    Load<TConfiguration>(category);
                }
            }
        }


        /// <summary>
        /// Gets the default directory for the Profile ("config/{profileId}" in the application's base directory). Returns null if the Id is empty
        /// </summary>
        public static string GetDirectory(string profileId)
        {
            if (!string.IsNullOrEmpty(profileId))
            {
                return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TrakHoundConfigurations.Directory, profileId);
            }

            return null;
        }
    }
}
