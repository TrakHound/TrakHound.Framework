// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TrakHound.Extensions;
using TrakHound.Logging;
using TrakHound.Packages;

namespace TrakHound.Modules
{
    /// <summary>
    /// Loads Modules (Types that implement TModule) from installed Packages and unloads them when the Package is removed
    /// </summary>
    public class TrakHoundModuleManager<TModule> : ITrakHoundModuleManager, IDisposable
    {
        /// <summary>
        /// The directory (relative to the application's base directory) that Package files are copied to before being loaded
        /// </summary>
        public const string _directory = "_modules";
        private const string _defaultFileExtension = ".dll";
        private const int _maxLoaderExceptions = 5;
        private static readonly TimeSpan _failureLogInterval = TimeSpan.FromMinutes(10);

        private static Func<string, string> _defaultFilterFunction = (packageId) =>
        {
            return packageId + _defaultFileExtension;
        };

        private readonly TrakHoundModuleContext _context;
        private readonly bool _loadByDependencies;
        private readonly IEnumerable<string> _packageCategories;
        private readonly Func<string, string> _filterFunction;
        private readonly TrakHoundPackageManager _packageManager;
        private readonly Dictionary<string, TrakHoundModule> _modules = new Dictionary<string, TrakHoundModule>(); // ModuleKey => Module
        private readonly ConcurrentDictionary<string, object> _loadLocks = new ConcurrentDictionary<string, object>(); // ModuleKey => Lock
        private readonly Dictionary<string, DateTime> _reportedFailures = new Dictionary<string, DateTime>(); // Failure => Last Logged
        private readonly Dictionary<string, TrakHoundModuleInformation> _failures = new Dictionary<string, TrakHoundModuleInformation>(); // ModuleKey => Latest Failure
        private readonly object _lock = new object();


        /// <summary>
        /// The Package Categories that Modules are loaded from
        /// </summary>
        public IEnumerable<string> PackageCategories => _packageCategories;

        /// <summary>
        /// Maps the Module key ("{PackageId}-{PackageVersion}") to the filename of the Assembly to load (default appends ".dll"). Only used when loading by dependencies
        /// </summary>
        public Func<string, string> FilterFunction => _filterFunction;

        /// <summary>
        /// The Modules that are currently loaded
        /// </summary>
        public IEnumerable<ITrakHoundModule> Modules
        {
            get
            {
                lock (_lock) return _modules.Values.ToList();
            }
        }

        /// <summary>
        /// All of the Types from the Modules that are currently loaded
        /// </summary>
        public IEnumerable<Type> ModuleTypes
        {
            get
            {
                lock (_lock) return _modules.Values.SelectMany(o => o.ModuleTypes).ToList();
            }
        }

        /// <summary>
        /// Invoked when a Module is loaded
        /// </summary>
        public EventHandler<ITrakHoundModule> ModuleAdded { get; set; }

        /// <summary>
        /// Invoked when a Module is unloaded because its Package was removed
        /// </summary>
        public EventHandler<ITrakHoundModule> ModuleRemoved { get; set; }

        /// <summary>
        /// Invoked when a Module fails to load or unload
        /// </summary>
        public EventHandler<Exception> ModuleLoadError { get; set; }

        /// <summary>
        /// The Logger used to write Module load and unload messages (optional)
        /// </summary>
        public ITrakHoundLogger Logger { get; set; }


        /// <summary>
        /// Create a new Module Manager that loads from the specified Package Category. When loadByDependencies is false, every DLL in the Package is scanned for Types
        /// </summary>
        public TrakHoundModuleManager(
            TrakHoundPackageManager packageManager,
            string packageCategory,
            Func<string, string> filterFunction = null,
            TrakHoundModuleContext context = null,
            bool loadByDependencies = true
            )
            : this(packageManager, !string.IsNullOrEmpty(packageCategory) ? new string[] { packageCategory } : null, filterFunction, context, loadByDependencies) { }

        /// <summary>
        /// Create a new Module Manager that loads from the specified Package Categories. When loadByDependencies is false, every DLL in the Package is scanned for Types
        /// </summary>
        public TrakHoundModuleManager(
            TrakHoundPackageManager packageManager,
            IEnumerable<string> packageCategories,
            Func<string, string> filterFunction = null,
            TrakHoundModuleContext context = null,
            bool loadByDependencies = true
            )
        {
            _packageManager = packageManager;
            _packageManager.PackageRemoved += PackageRemoved;

            _packageCategories = packageCategories;
            _filterFunction = filterFunction ?? _defaultFilterFunction;
            _context = context ?? new TrakHoundModuleContext();
            _loadByDependencies = loadByDependencies;
        }

        /// <summary>
        /// Unsubscribes from the Package Manager events
        /// </summary>
        public void Dispose()
        {
            _packageManager.PackageRemoved -= PackageRemoved;
        }


        /// <summary>
        /// Gets the Module for the specified Package, loading it if needed. The Version can contain wildcards (see <see cref="TrakHoundPackage.SelectVersion"/>). Returns null if the Package is not found or fails to load
        /// </summary>
        public ITrakHoundModule Get(string packageId, string packageVersion)
        {
            if (!string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(packageVersion))
            {
                // Exact versions can be returned from the cache without asking the Package Manager
                if (!packageVersion.Contains('*'))
                {
                    var module = GetCachedModule(GetModuleKey(packageId, packageVersion));
                    if (module != null) return module;
                }

                // Resolve the Version from the installed Packages (so removed or newly added Packages are always reflected)
                var package = _packageManager.Resolve(packageId, packageVersion);
                if (package != null)
                {
                    return GetOrLoadModule(package, packageVersion);
                }
                else
                {
                    var installedVersions = _packageManager.GetVersions(packageId);
                    var installed = !installedVersions.IsNullOrEmpty() ? $"Installed Versions = {string.Join(", ", installedVersions.OrderBy(o => o, VersionComparer.Default))}" : "Not Installed";

                    LogFailure(GetModuleKey(packageId, packageVersion), TrakHoundLogLevel.Warning, $"Package Not Found : {packageId} v{packageVersion} : {installed}");
                    RecordFailure(GetModuleKey(packageId, packageVersion), null, packageId, packageVersion, $"Package Not Found : {installed}");
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the Module Types for the specified Package, loading it if needed (empty if not found)
        /// </summary>
        public IEnumerable<Type> GetTypes(string packageId, string packageVersion)
        {
            return Get(packageId, packageVersion)?.ModuleTypes ?? Enumerable.Empty<Type>();
        }


        private TrakHoundModule GetCachedModule(string moduleKey)
        {
            lock (_lock) return _modules.GetValueOrDefault(moduleKey);
        }

        private TrakHoundModule GetOrLoadModule(TrakHoundPackage package, string requestedVersion)
        {
            var moduleKey = GetModuleKey(package.Id, package.Version);

            var module = GetCachedModule(moduleKey);
            if (module != null) return module;

            // Only load each Module once when requested by multiple threads at the same time
            lock (_loadLocks.GetOrAdd(moduleKey, _ => new object()))
            {
                module = GetCachedModule(moduleKey);
                if (module != null) return module;

                LogSkippedPrerelease(package, requestedVersion);

                module = LoadModule(package, moduleKey);
                if (module == null) return null;

                lock (_lock)
                {
                    _modules[moduleKey] = module;
                    _failures.Remove(moduleKey);
                }
            }

            ModuleAdded?.Invoke(this, module);

            return module;
        }


        // A newer Prerelease being passed over looks like a failed install, so say why the older Version is running
        private void LogSkippedPrerelease(TrakHoundPackage package, string requestedVersion)
        {
            if (!requestedVersion.Contains('*') || requestedVersion.EndsWith(TrakHoundPackage.PrereleaseWildcardSuffix)) return;

            var newest = TrakHoundPackage.SelectVersion(_packageManager.GetVersions(package.Id), requestedVersion + TrakHoundPackage.PrereleaseWildcardSuffix);
            if (newest != null && newest != package.Version)
            {
                Logger?.LogInformation($"Newer Prerelease Not Selected : {package.Id} v{newest} : '{requestedVersion}' selects v{package.Version}. Use '{requestedVersion}{TrakHoundPackage.PrereleaseWildcardSuffix}' or the exact Version to select it");
            }
        }


        private void PackageRemoved(object sender, TrakHoundPackageChangedArgs args)
        {
            if (string.IsNullOrEmpty(args.PackageId) || string.IsNullOrEmpty(args.PackageVersion)) return;
            if (!_packageCategories.IsNullOrEmpty() && !_packageCategories.Contains(args.PackageCategory)) return;

            var moduleKey = GetModuleKey(args.PackageId, args.PackageVersion);

            TrakHoundModule removeModule;
            lock (_lock)
            {
                removeModule = _modules.GetValueOrDefault(moduleKey);
                if (removeModule != null) _modules.Remove(moduleKey);
                _failures.Remove(moduleKey);
            }

            if (removeModule != null)
            {
                UnloadModule(args.PackageCategory, args.PackageId, args.PackageVersion);

                ModuleRemoved?.Invoke(this, removeModule);
            }
        }


        private TrakHoundModule LoadModule(TrakHoundPackage package, string moduleKey)
        {
            var stopwatch = Stopwatch.StartNew();
            var warnings = new List<string>();

            try
            {
                var types = ReadPackageModuleTypes(package.Category, package.Id, package.Version, warnings);

                var packageReadMe = TrakHoundPackage.GetReadMe(package.Category, package.Id, package.Version, _packageManager.Path);
                var packageDirectory = TrakHoundPackage.GetPackageVersionDirectory(_packageManager.Path, package.Category, package.Id, package.Version);

                Logger?.LogInformation($"Module Loaded : {package.Id} v{package.Version} : {package.Category} : {types.Count()} {typeof(TModule).Name} Type(s) : {stopwatch.ElapsedMilliseconds}ms");
                Logger?.LogDebug($"Module Types : {package.Id} v{package.Version} : {string.Join(", ", types.Select(o => o.FullName))}");

                return new TrakHoundModule(package, packageDirectory, types, packageReadMe)
                {
                    LoadedTime = DateTime.UtcNow,
                    LoadDuration = stopwatch.Elapsed,
                    Warnings = warnings
                };
            }
            catch (TrakHoundModuleLoadException ex)
            {
                // Expected failure (ex. the Assembly is missing), so the message describes it without a Stack Trace.
                // When scanning every DLL (ex. for optional App Injection Services) a package without Module Types is normal
                var level = _loadByDependencies ? TrakHoundLogLevel.Warning : TrakHoundLogLevel.Debug;
                LogFailure(moduleKey, level, $"Module Not Loaded : {package.Id} v{package.Version} : {package.Category} : {ex.Message}");
                if (_loadByDependencies) RecordFailure(moduleKey, package.Category, package.Id, package.Version, ex.Message);
                ModuleLoadError?.Invoke(this, ex);
            }
            catch (Exception ex)
            {
                if (LogFailure(moduleKey, TrakHoundLogLevel.Error, $"Module Load Error : {package.Id} v{package.Version} : {package.Category} : {ex.GetType().Name} : {ex.Message}"))
                {
                    Logger?.LogDebug($"Module Load Error : {package.Id} v{package.Version} : {ex}");
                }

                RecordFailure(moduleKey, package.Category, package.Id, package.Version, $"{ex.GetType().Name} : {ex.Message}");

                ModuleLoadError?.Invoke(this, ex);
            }

            return null;
        }

        private void UnloadModule(string packageCategory, string packageId, string packageVersion)
        {
            try
            {
                // Collectible, so the Assemblies are released once nothing references their Types
                _context.RemoveLoadContext(CreateLoadContextId(packageCategory, packageId, packageVersion));

                Logger?.LogInformation($"Module Unloaded : {packageId} v{packageVersion} : {packageCategory}");
            }
            catch (Exception ex)
            {
                Logger?.LogError($"Module Unload Error : {packageId} v{packageVersion} : {packageCategory} : {ex.GetType().Name} : {ex.Message}");
                ModuleLoadError?.Invoke(this, ex);
            }
        }

        /// <summary>
        /// Log the same Failure at most once per interval, since failed Modules are requested repeatedly (ex. Services retrying)
        /// </summary>
        private bool LogFailure(string moduleKey, TrakHoundLogLevel level, string message)
        {
            if (Logger == null) return false;

            var failureKey = $"{moduleKey}|{message}";
            var now = DateTime.UtcNow;

            lock (_lock)
            {
                if (_reportedFailures.TryGetValue(failureKey, out var lastLogged) && now - lastLogged < _failureLogInterval) return false;
                _reportedFailures[failureKey] = now;
            }

            Logger.Log(level, message);
            return true;
        }


        /// <summary>
        /// Keep the latest Failure for each Module (independent of logging) so it can be shown in Diagnostics
        /// </summary>
        private void RecordFailure(string moduleKey, string packageCategory, string packageId, string packageVersion, string error)
        {
            lock (_lock)
            {
                var failure = _failures.GetValueOrDefault(moduleKey);
                if (failure == null)
                {
                    failure = new TrakHoundModuleInformation
                    {
                        PackageCategory = packageCategory,
                        PackageId = packageId,
                        PackageVersion = packageVersion
                    };
                    _failures[moduleKey] = failure;
                }

                failure.Error = error;
                failure.ErrorTime = DateTime.UtcNow.ToUnixTime();
                failure.ErrorCount++;
            }
        }

        /// <inheritdoc/>
        public IEnumerable<TrakHoundModuleInformation> GetInformation()
        {
            var moduleType = typeof(TModule).Name;
            var results = new List<TrakHoundModuleInformation>();

            lock (_lock)
            {
                foreach (var module in _modules.Values)
                {
                    results.Add(new TrakHoundModuleInformation
                    {
                        ModuleType = moduleType,
                        ManagerCategories = _packageCategories?.ToList(),
                        PackageCategory = module.Package?.Category,
                        PackageId = module.PackageId,
                        PackageVersion = module.PackageVersion,
                        PackageDirectory = module.PackageDirectory,
                        IsLoaded = true,
                        Types = module.ModuleTypes?.Select(o => o.FullName).OrderBy(o => o).ToList(),
                        LoadedTime = module.LoadedTime.ToUnixTime(),
                        LoadDuration = (long)module.LoadDuration.TotalMilliseconds,
                        Warnings = module.Warnings?.ToList()
                    });
                }

                foreach (var failure in _failures.Values)
                {
                    results.Add(new TrakHoundModuleInformation
                    {
                        ModuleType = moduleType,
                        ManagerCategories = _packageCategories?.ToList(),
                        PackageCategory = failure.PackageCategory,
                        PackageId = failure.PackageId,
                        PackageVersion = failure.PackageVersion,
                        IsLoaded = false,
                        Error = failure.Error,
                        ErrorTime = failure.ErrorTime,
                        ErrorCount = failure.ErrorCount
                    });
                }
            }

            return results;
        }


        private IEnumerable<Type> ReadPackageModuleTypes(string packageCategory, string packageId, string packageVersion, List<string> warnings)
        {
            var packageDirectory = TrakHoundPackage.GetPackageVersionDistributableDirectory(_packageManager.Path, packageCategory, packageId, packageVersion);
            if (packageDirectory == null || !Directory.Exists(packageDirectory))
            {
                throw new TrakHoundModuleLoadException($"Package Distributable Directory Not Found : {packageDirectory}");
            }

            var key = $"{packageId}-{packageVersion}";

            var modulePath = CreateModuleDirectory(key, packageDirectory);
            var loadContextId = CreateLoadContextId(packageCategory, packageId, packageVersion);
            var assemblyFilename = _filterFunction(key);
            var assemblyPath = Path.Combine(modulePath, assemblyFilename);
            var loadContext = _context.AddLoadContext(loadContextId, assemblyPath);

            var types = new List<Type>();
            if (_loadByDependencies)
            {
                if (!File.Exists(assemblyPath))
                {
                    throw new TrakHoundModuleLoadException($"Assembly Not Found : Expected {assemblyFilename} in {packageDirectory}");
                }

                types.AddRange(GetTypesFromFile(loadContext, assemblyPath, packageId, packageVersion, warnings));
            }
            else
            {
                foreach (var dllFile in Directory.GetFiles(modulePath, "*.dll"))
                {
                    types.AddRange(GetTypesFromFile(loadContext, dllFile, packageId, packageVersion, warnings));
                }
            }

            if (types.Count < 1)
            {
                var searched = _loadByDependencies ? assemblyFilename : "*.dll";
                throw new TrakHoundModuleLoadException($"No {typeof(TModule).Name} Types Found : Searched {searched} in {packageDirectory}");
            }

            return types;
        }


        private static string GetModuleKey(string packageId, string packageVersion)
        {
            return $"{packageId}:{packageVersion}";
        }


        private IEnumerable<Type> GetTypesFromFile(TrakHoundModuleLoadContext context, string path, string packageId, string packageVersion, List<string> warnings)
        {
            if (context == null || string.IsNullOrEmpty(path) || !File.Exists(path)) return Enumerable.Empty<Type>();

            Assembly assembly;
            try
            {
                // Reuse the Assembly if it is already loaded (ex. as a dependency of another file in the package)
                var assemblyName = AssemblyName.GetAssemblyName(path);
                assembly = context.Assemblies.FirstOrDefault(o => AssemblyName.ReferenceMatchesDefinition(o.GetName(), assemblyName))
                    ?? context.LoadFromAssemblyPath(path);
            }
            catch (BadImageFormatException)
            {
                // Native DLL (not a .NET Assembly)
                Logger?.LogDebug($"Skipped Native DLL : {packageId} v{packageVersion} : {Path.GetFileName(path)}");
                return Enumerable.Empty<Type>();
            }

            Type[] assemblyTypes;
            try
            {
                assemblyTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Use the Types that did load (ex. a dependency of an unrelated Type is missing)
                assemblyTypes = ex.Types.Where(o => o != null).ToArray();

                var loaderErrors = ex.LoaderExceptions.Where(o => o != null).Select(o => o.Message).Distinct().ToList();
                var shownErrors = string.Join(" | ", loaderErrors.Take(_maxLoaderExceptions));
                if (loaderErrors.Count > _maxLoaderExceptions) shownErrors += $" | (+{loaderErrors.Count - _maxLoaderExceptions} more)";

                LogFailure(GetModuleKey(packageId, packageVersion), TrakHoundLogLevel.Warning, $"Some Types Failed to Load : {packageId} v{packageVersion} : {Path.GetFileName(path)} : {ex.Types.Count(o => o == null)} Type(s) Skipped : {shownErrors}");
                warnings?.Add($"Some Types Failed to Load : {Path.GetFileName(path)} : {ex.Types.Count(o => o == null)} Type(s) Skipped : {shownErrors}");
            }

            return assemblyTypes.Where(o => !o.IsAbstract && !o.IsInterface && typeof(TModule).IsAssignableFrom(o)).ToList();
        }


        private static string CreateLoadContextId(string packageCategory, string packageId, string packageVersion)
        {
            return $"{packageCategory}:{packageId}:{packageVersion}".ToMD5Hash();
        }

        private string CreateModuleDirectory(string key, string sourceDirectory)
        {
            // Include a Stamp of the source files so a reinstalled Package (with the same Version) gets a fresh copy
            // instead of reusing the old files (which can't be overwritten while they are loaded)
            var sourceFiles = GetFiles(sourceDirectory);
            var modulePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _directory, $"{key}-{CreateStamp(sourceFiles, true)}");
            if (Directory.Exists(modulePath)) return modulePath;

            // Copy to a temp directory first and then move into place, so a partial copy is never used
            var tempPath = $"{modulePath}.tmp-{Guid.NewGuid():N}";
            Directory.CreateDirectory(tempPath);
            TrakHoundFiles.Copy(sourceDirectory, tempPath);

            if (CreateStamp(GetFiles(tempPath), false) != CreateStamp(sourceFiles, false))
            {
                TryDeleteDirectory(tempPath);
                throw new IOException($"Failed to copy Module files from {sourceDirectory} to {modulePath}");
            }

            try
            {
                Directory.Move(tempPath, modulePath);
                Logger?.LogDebug($"Module Files Copied : {sourceDirectory} => {modulePath}");
            }
            catch (IOException) when (Directory.Exists(modulePath))
            {
                // Another process copied the same files first
                TryDeleteDirectory(tempPath);
            }

            return modulePath;
        }

        private static IEnumerable<(string RelativePath, FileInfo File)> GetFiles(string directory)
        {
            return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Select(o => (Path.GetRelativePath(directory, o), new FileInfo(o)))
                .OrderBy(o => o.Item1, StringComparer.Ordinal)
                .ToList();
        }

        private static string CreateStamp(IEnumerable<(string RelativePath, FileInfo File)> files, bool includeTimestamps)
        {
            var builder = new StringBuilder();
            foreach (var file in files)
            {
                builder.Append(file.RelativePath).Append('|').Append(file.File.Length);
                if (includeTimestamps) builder.Append('|').Append(file.File.LastWriteTimeUtc.Ticks);
                builder.Append(';');
            }

            return builder.ToString().ToMD5Hash()?.Substring(0, 8);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch { }
        }
    }
}
