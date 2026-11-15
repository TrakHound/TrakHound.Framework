// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Text.Json;
using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Configurations;
using TrakHound.Functions;
using TrakHound.Instances;
using TrakHound.Packages;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Workers;

namespace TrakHound.CLI.Dev
{
    /// <summary>
    /// Runs a Package project in a local Instance: builds it, installs it with a Configuration, and reinstalls it each time its files change.
    /// Works on the Instance's directories (the Instance picks up Package and Configuration changes while it runs). Each build gets its own
    /// Version (the Instance only loads new code for a new Version), the Configuration is set to that exact Version, and the previous dev
    /// Version is then uninstalled (which unloads it)
    /// </summary>
    [CommandGroup("dev", "Run a Package project in a local Instance and reinstall it each time its files change")]
    internal static class Dev
    {
        private const string InstanceDescription = "The root directory of the Instance (with its 'packages' and 'config' directories). If not specified, the TRAKHOUND_DEV_INSTANCE environment variable is used";
        private const string ProfileDescription = "The Configuration Profile of the Instance";
        private const string InstanceEnvironmentVariable = "TRAKHOUND_DEV_INSTANCE";
        private const string DebugConfigurationFilename = "trakhound.debug.json";

        // The Instance's Configuration watcher waits 2 seconds after a change, so the previous Version is removed after the
        // new Configuration has been loaded (removing it first would stop the module until the Configuration is reloaded)
        private static readonly TimeSpan _removePreviousDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _watchDelay = TimeSpan.FromMilliseconds(750);

        private static readonly string[] _watchExtensions = new[] { ".cs", ".razor", ".cshtml", ".css", ".js", ".json", ".csproj", ".resx", ".html", ".svg", ".png" };
        private static readonly string[] _ignoredDirectories = new[] { "bin", "obj", "out", ".vs", ".git", "volume", "node_modules" };

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };


        [Command("run", "Build the project, install it in a local Instance with a Configuration, and reinstall it each time its files change (Ctrl+C stops and removes it)")]
        [CommandHelp(null, "th dev run ./Contoso.Hello --instance C:\\TrakHound\\Instance")]
        public static async Task<bool> Run(
            [CommandParameter("The path to the DotNet project directory (or .csproj file). If not specified, the current working directory is used")]
            string path = null,
            [CommandOption(InstanceDescription, "-i")]
            string instance = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default,
            [CommandOption("The Route of an Api or App. If not specified, the Package's '.defaultRoute' metadata or its ID (lowercase) is used")]
            string route = null,
            [CommandOption("The DotNet build configuration")]
            string projectConfiguration = "Debug",
            [CommandOption("Build and install once, without watching (the Configuration and Package are kept)")]
            bool noWatch = false,
            [CommandOption("Keep the Configuration and Package in the Instance when stopping")]
            bool keep = false
            )
        {
            var session = DevSession.Open(path, instance, profile, route, projectConfiguration);
            if (session == null) return false;

            session.PrintStart();

            var installed = await session.BuildAndInstall();
            if (noWatch) return installed;

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                stop.Cancel();
            };

            using (var watcher = new ProjectWatcher(session.ProjectDirectory))
            {
                Console.WriteLine();
                Console.WriteLine("Watching for changes. Press Ctrl+C to stop..");

                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        await watcher.WaitForChange(stop.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    Console.WriteLine();
                    Console.WriteLine($"Change Detected : {watcher.LastChange}");

                    // Changes made by the build itself (it moves the Project file aside) are ignored
                    watcher.Paused = true;
                    await session.BuildAndInstall();
                    watcher.Paused = false;
                }
            }

            Console.WriteLine();
            if (keep)
            {
                Console.WriteLine($"Stopped. The Configuration ({session.ConfigurationId}) and Version ({session.CurrentVersion}) are kept in the Instance (th dev clean removes them)");
            }
            else
            {
                Console.WriteLine("Stopping..");
                session.Clean();
            }

            return true;
        }

        [Command("clean", "Remove the Configuration and the dev Versions that th dev run installed for the project")]
        [CommandHelp(null, "th dev clean ./Contoso.Hello --instance C:\\TrakHound\\Instance")]
        public static bool Clean(
            [CommandParameter("The path to the DotNet project directory (or .csproj file). If not specified, the current working directory is used")]
            string path = null,
            [CommandOption(InstanceDescription, "-i")]
            string instance = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            var session = DevSession.Open(path, instance, profile, null, null);
            if (session == null) return false;

            session.Clean();
            return true;
        }


        private class DevSession
        {
            private readonly TrakHoundPackage _information;
            private readonly string _projectConfiguration;
            private readonly string _route;
            private readonly DevState _state;
            private readonly string _statePath;
            private int _buildCount;


            public string ProjectDirectory { get; private set; }

            public string InstanceDirectory { get; private set; }

            public string PackagesDirectory { get; private set; }

            public string ProfileDirectory { get; private set; }

            public string Profile { get; private set; }

            public string ConfigurationId => $"dev-{_information.Id}";

            public string CurrentVersion => _state.CurrentVersion;


            private DevSession(TrakHoundPackage information, string projectConfiguration, string route, string statePath)
            {
                _information = information;
                _projectConfiguration = projectConfiguration;
                // The same default Route the Instance uses for a new Configuration (".defaultRoute" in the Package metadata), or the Package ID
                if (string.IsNullOrEmpty(route)) route = information.GetMetadata(".defaultRoute")?.ToString();
                if (string.IsNullOrEmpty(route)) route = information.Id.ToLowerInvariant();
                _route = route.Trim('/');
                _statePath = statePath;
                _state = DevState.Read(statePath);
            }

            public static DevSession Open(string path, string instance, string profile, string route, string projectConfiguration)
            {
                var projectPath = CliPath.Resolve(path);
                var projectDirectory = File.Exists(projectPath) ? Path.GetDirectoryName(projectPath) : projectPath;

                var informationPath = Path.Combine(projectDirectory, TrakHoundPackage.PackageConfigurationFilename);
                var information = TrakHoundPackage.ReadInformationFromFile(informationPath);
                if (information == null)
                {
                    Console.Error.WriteLine($"Error : Package Information File not Found : ({informationPath})");
                    return null;
                }

                if (GetConfigurationType(information.Category) == null)
                {
                    Console.Error.WriteLine($"Error : th dev doesn't support the '{information.Category}' Package Category (expected api, app, function, identity, service or worker)");
                    return null;
                }

                if (string.IsNullOrEmpty(instance)) instance = Environment.GetEnvironmentVariable(InstanceEnvironmentVariable);
                if (string.IsNullOrEmpty(instance))
                {
                    Console.Error.WriteLine($"Error : No Instance directory. Set --instance or the {InstanceEnvironmentVariable} environment variable");
                    return null;
                }

                var instanceDirectory = CliPath.Resolve(instance);
                if (!Directory.Exists(instanceDirectory))
                {
                    Console.Error.WriteLine($"Error : Instance directory not Found : ({instanceDirectory})");
                    return null;
                }

                if (string.IsNullOrEmpty(profile)) profile = TrakHoundConfigurationProfile.Default;

                var session = new DevSession(information, projectConfiguration, route, Path.Combine(projectDirectory, "obj", "trakhound-dev.json"));
                session.ProjectDirectory = projectDirectory;
                session.InstanceDirectory = instanceDirectory;
                session.PackagesDirectory = Packages.Packages.GetPackagesPath(instanceDirectory);
                session.Profile = profile;
                session.ProfileDirectory = Path.Combine(instanceDirectory, TrakHoundConfigurations.Directory, profile);
                return session;
            }


            public void PrintStart()
            {
                Console.WriteLine();
                Console.WriteLine(string.Format("{0,-14} : {1}", "Package", $"{_information.Id} ({_information.Category})"));
                Console.WriteLine(string.Format("{0,-14} : {1}", "Project", ProjectDirectory));
                Console.WriteLine(string.Format("{0,-14} : {1}", "Instance", InstanceDirectory));
                Console.WriteLine(string.Format("{0,-14} : {1}", "Profile", Profile));
                Console.WriteLine(string.Format("{0,-14} : {1}", "Configuration", $"{_information.Category} : {ConfigurationId}"));

                var boot = ReadRunningBoot(ProfileDirectory);
                if (boot != null)
                {
                    Console.WriteLine(string.Format("{0,-14} : {1}", "Instance PID", $"{boot.Value.ProcessId} (attach a debugger to this process)"));
                }
                else
                {
                    Console.WriteLine(string.Format("{0,-14} : {1}", "Instance PID", "Not Found (start the Instance with this directory and profile; the package is still installed)"));
                }
            }

            public async Task<bool> BuildAndInstall()
            {
                _buildCount++;
                var stopwatch = Stopwatch.StartNew();
                var version = GetNextVersion();

                Console.WriteLine();
                Console.WriteLine($"Building {_information.Id} {version} ({_projectConfiguration})..");

                var settings = new TrakHoundDotNetPackageSettings();
                settings.DotnetSdkLocation = TrakHoundCliConfiguration.Instance.DotnetSdkLocation;
                settings.Version = version;
                settings.Configuration = _projectConfiguration;
                settings.IncludeDebugSymbols = true;
                settings.IncludeSource = false;

                // The build is synchronous (dotnet publish), so it runs off the calling thread to keep Ctrl+C responsive
                var packageBytes = await Task.Run(() => TrakHoundDotNetPackage.Create(ProjectDirectory, settings));
                if (packageBytes == null)
                {
                    Console.Error.WriteLine(!string.IsNullOrEmpty(_state.CurrentVersion)
                        ? $"Build Failed : the Instance keeps running {_information.Id} {_state.CurrentVersion}"
                        : "Build Failed");
                    return false;
                }

                if (!TrakHoundPackage.Install(packageBytes, PackagesDirectory))
                {
                    Console.Error.WriteLine($"Error : Unable to install the Package in ({PackagesDirectory})");
                    return false;
                }

                var previousVersion = _state.CurrentVersion;
                _state.CurrentVersion = version;
                if (!_state.Versions.Contains(version)) _state.Versions.Add(version);
                _state.Write(_statePath);

                Packages.Packages.UpdatePackagesFile(InstanceDirectory, PackagesDirectory);

                if (!SaveConfiguration(version)) return false;

                Console.WriteLine($"Installed {_information.Id} {version} in {stopwatch.Elapsed.TotalSeconds:0.0}s");
                PrintUsage();

                // Uninstall the previous dev Versions once the Instance has loaded the new Configuration
                if (!string.IsNullOrEmpty(previousVersion))
                {
                    await Task.Delay(_removePreviousDelay);
                    RemoveVersions(_state.Versions.Where(o => o != version).ToList());
                }

                return true;
            }

            public void Clean()
            {
                RemoveConfiguration();
                RemoveVersions(_state.Versions.ToList());

                _state.CurrentVersion = null;
                _state.Write(_statePath);
            }


            /// <summary>
            /// The Package Version with a 4th (Revision) number for each build. The Instance only loads new code for a new Version, and Versions
            /// must be numeric (System.Version), so a "-dev" suffix can't be used
            /// </summary>
            private string GetNextVersion()
            {
                var baseVersion = _information.Version.ToVersion() ?? new Version(0, 0, 1);
                var major = Math.Max(baseVersion.Major, 0);
                var minor = Math.Max(baseVersion.Minor, 0);
                var build = Math.Max(baseVersion.Build, 0);

                // Continue after the highest Revision installed for this base Version (ex. after an earlier session)
                var installed = TrakHoundPackage.GetInstalledVersions(_information.Category, _information.Id, PackagesDirectory)?
                    .Select(o => o.Version.ToVersion())
                    .Where(o => o != null && o.Major == major && o.Minor == minor && Math.Max(o.Build, 0) == build)
                    .Select(o => Math.Max(o.Revision, 0)) ?? Enumerable.Empty<int>();

                var revision = Math.Max(_state.Revision, installed.DefaultIfEmpty(0).Max()) + 1;
                _state.Revision = revision;

                return new Version(major, minor, build, revision).ToString();
            }

            private bool SaveConfiguration(string version)
            {
                var configuration = CreateConfiguration(version);

                using (var configurationProfile = new TrakHoundConfigurationProfile(Profile, path: ProfileDirectory))
                {
                    var errors = new List<string>();
                    configurationProfile.ErrorReceived += (sender, message) => errors.Add(message);

                    configurationProfile.Add(configuration, true);
                    if (!errors.IsNullOrEmpty())
                    {
                        foreach (var error in errors) Console.Error.WriteLine($"Error : {error}");
                        return false;
                    }

                    if (_buildCount == 1) Console.WriteLine($"Configuration Saved : ({configuration.Path})");
                    return true;
                }
            }

            private ITrakHoundConfiguration CreateConfiguration(string version)
            {
                var parameters = ReadParameters();
                var name = $"{_information.Id} (th dev)";

                switch (_information.Category.ToLowerInvariant())
                {
                    case TrakHoundApiConfiguration.ConfigurationCategory:
                        return new TrakHoundApiConfiguration { Id = ConfigurationId, Name = name, PackageId = _information.Id, PackageVersion = version, Route = _route, Parameters = parameters };

                    case TrakHoundAppConfiguration.ConfigurationCategory:
                        return new TrakHoundAppConfiguration { Id = ConfigurationId, Name = name, PackageId = _information.Id, PackageVersion = version, Route = _route, Parameters = parameters };

                    case TrakHoundFunctionConfiguration.ConfigurationCategory:
                        return new TrakHoundFunctionConfiguration { Id = ConfigurationId, Name = name, PackageId = _information.Id, PackageVersion = version, FunctionId = _information.Id, Parameters = parameters };

                    case TrakHoundWorkerConfiguration.ConfigurationCategory:
                        return new TrakHoundWorkerConfiguration { Id = ConfigurationId, Name = name, PackageId = _information.Id, PackageVersion = version, Parameters = parameters };

                    case TrakHoundServiceConfiguration.ConfigurationCategory:
                        return new TrakHoundServiceConfiguration { Id = ConfigurationId, Name = name, PackageId = _information.Id, PackageVersion = version, Parameters = parameters };

                    case TrakHoundIdentityProviderConfiguration.ConfigurationCategory:
                        return new TrakHoundIdentityProviderConfiguration { Id = ConfigurationId, PackageId = _information.Id, PackageVersion = version, Parameters = parameters };
                }

                return null;
            }

            /// <summary>
            /// The "parameters" in the project's trakhound.debug.json (the same Parameters the standalone debug host uses)
            /// </summary>
            private Dictionary<string, object> ReadParameters()
            {
                var parameters = new Dictionary<string, object>();

                var debugPath = Path.Combine(ProjectDirectory, DebugConfigurationFilename);
                if (File.Exists(debugPath))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(debugPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                        if (document.RootElement.TryGetProperty("parameters", out var element) && element.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var property in element.EnumerateObject())
                            {
                                var value = ConvertValue(property.Value);
                                if (value != null) parameters[property.Name] = value;
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Console.Error.WriteLine($"Warning : Unable to read the Parameters in ({debugPath}) : {ex.Message}");
                    }
                }

                return parameters;
            }

            private void RemoveConfiguration()
            {
                if (!Directory.Exists(ProfileDirectory)) return;

                var category = _information.Category.ToLowerInvariant();
                var configurationPath = Path.Combine(ProfileDirectory, category, $"{ConfigurationId}.yaml");
                if (File.Exists(configurationPath))
                {
                    File.Delete(configurationPath);
                    Console.WriteLine($"Configuration Removed : ({configurationPath})");
                }
            }

            private void RemoveVersions(List<string> versions)
            {
                if (versions.IsNullOrEmpty()) return;

                var removed = false;
                foreach (var version in versions)
                {
                    var versionDirectory = TrakHoundPackage.GetPackageVersionDirectory(PackagesDirectory, _information.Category, _information.Id, version);
                    if (Directory.Exists(versionDirectory))
                    {
                        if (TrakHoundPackage.RemovePackageVersion(versionDirectory))
                        {
                            Console.WriteLine($"Uninstalled {_information.Id} {version}");
                            removed = true;
                        }
                        else
                        {
                            // Kept in the list to be removed later (ex. a file was in use)
                            continue;
                        }
                    }

                    _state.Versions.Remove(version);
                }

                _state.Write(_statePath);
                if (removed) Packages.Packages.UpdatePackagesFile(InstanceDirectory, PackagesDirectory);
            }

            private void PrintUsage()
            {
                if (_buildCount > 1) return;

                var instanceConfiguration = TrakHoundInstanceConfiguration.Read(Path.Combine(ProfileDirectory, TrakHoundInstanceConfiguration.Filename));
                var port = instanceConfiguration?.HttpPort > 0 ? instanceConfiguration.HttpPort : 8472;
                var basePath = instanceConfiguration?.BasePath?.Trim('/');
                var baseUrl = $"http://localhost:{port}" + (!string.IsNullOrEmpty(basePath) ? $"/{basePath}" : "");

                switch (_information.Category.ToLowerInvariant())
                {
                    case TrakHoundApiConfiguration.ConfigurationCategory:
                    case TrakHoundAppConfiguration.ConfigurationCategory:
                        Console.WriteLine($"Open : {baseUrl}/{_route}");
                        break;

                    case TrakHoundFunctionConfiguration.ConfigurationCategory:
                        Console.WriteLine($"Run : Admin interface > Explorer > Functions > {_information.Id} ({baseUrl})");
                        break;

                    case TrakHoundWorkerConfiguration.ConfigurationCategory:
                        Console.WriteLine($"Status : Admin interface > Explorer > Workers ({baseUrl})");
                        break;

                    case TrakHoundServiceConfiguration.ConfigurationCategory:
                        Console.WriteLine($"Status : Admin interface > Explorer > Services ({baseUrl}). Add a Router that uses it to call it through Client.Services");
                        break;

                    case TrakHoundIdentityProviderConfiguration.ConfigurationCategory:
                        Console.WriteLine($"Test : Admin interface > Security > Providers > Test Sign In ({baseUrl})");
                        break;
                }
            }


            private static Type GetConfigurationType(string category)
            {
                switch (category?.ToLowerInvariant())
                {
                    case TrakHoundApiConfiguration.ConfigurationCategory: return typeof(TrakHoundApiConfiguration);
                    case TrakHoundAppConfiguration.ConfigurationCategory: return typeof(TrakHoundAppConfiguration);
                    case TrakHoundFunctionConfiguration.ConfigurationCategory: return typeof(TrakHoundFunctionConfiguration);
                    case TrakHoundWorkerConfiguration.ConfigurationCategory: return typeof(TrakHoundWorkerConfiguration);
                    case TrakHoundServiceConfiguration.ConfigurationCategory: return typeof(TrakHoundServiceConfiguration);
                    case TrakHoundIdentityProviderConfiguration.ConfigurationCategory: return typeof(TrakHoundIdentityProviderConfiguration);
                    default: return null;
                }
            }

            private static object ConvertValue(JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.String: return element.GetString();
                    case JsonValueKind.Number: return element.TryGetInt64(out var integer) ? integer : element.GetDouble();
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False: return false;
                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined: return null;
                    default: return element.GetRawText();
                }
            }

            /// <summary>
            /// The running Boot in the Instance's Boot History (boots.json), if the Instance records one and its process is still running
            /// </summary>
            private static (int ProcessId, DateTime? Started)? ReadRunningBoot(string profileDirectory)
            {
                try
                {
                    var bootsPath = Path.Combine(profileDirectory, "boots.json");
                    if (!File.Exists(bootsPath)) return null;

                    using var document = JsonDocument.Parse(File.ReadAllText(bootsPath));
                    if (document.RootElement.ValueKind != JsonValueKind.Array) return null;

                    foreach (var boot in document.RootElement.EnumerateArray().Reverse())
                    {
                        if (boot.TryGetProperty("ended", out var ended) && ended.ValueKind != JsonValueKind.Null) continue;
                        if (!boot.TryGetProperty("processId", out var processIdElement) || !processIdElement.TryGetInt32(out var processId)) continue;

                        try
                        {
                            using var process = Process.GetProcessById(processId);
                            if (process.HasExited) continue;
                        }
                        catch (ArgumentException)
                        {
                            continue; // Not running (the Instance ended without recording it)
                        }

                        DateTime? started = boot.TryGetProperty("started", out var startedElement) && startedElement.TryGetDateTime(out var startedValue) ? startedValue : null;
                        return (processId, started);
                    }
                }
                catch { }

                return null;
            }
        }


        /// <summary>
        /// The dev Versions installed for a project, kept in its 'obj' directory so a later session (or th dev clean) can remove them
        /// </summary>
        private class DevState
        {
            public int Revision { get; set; }

            public string CurrentVersion { get; set; }

            public List<string> Versions { get; set; } = new List<string>();


            public static DevState Read(string path)
            {
                try
                {
                    if (File.Exists(path)) return JsonSerializer.Deserialize<DevState>(File.ReadAllText(path), _jsonOptions) ?? new DevState();
                }
                catch { }

                return new DevState();
            }

            public void Write(string path)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
        }


        /// <summary>
        /// Watches a project directory for changes to its source files (build output and debug host storage are ignored)
        /// </summary>
        private class ProjectWatcher : IDisposable
        {
            private readonly FileSystemWatcher _watcher;
            private readonly SemaphoreSlim _changed = new SemaphoreSlim(0);
            private readonly string _directory;


            public bool Paused { get; set; }

            public string LastChange { get; private set; }


            public ProjectWatcher(string directory)
            {
                _directory = directory;

                _watcher = new FileSystemWatcher(directory);
                _watcher.IncludeSubdirectories = true;
                _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite;
                _watcher.Created += OnChanged;
                _watcher.Changed += OnChanged;
                _watcher.Deleted += OnChanged;
                _watcher.Renamed += OnChanged;
                _watcher.EnableRaisingEvents = true;
            }

            public void Dispose()
            {
                _watcher.Dispose();
                _changed.Dispose();
            }

            public async Task WaitForChange(CancellationToken cancellationToken)
            {
                await _changed.WaitAsync(cancellationToken);

                // Wait until changes stop (ex. a Save All), then take every change as one
                while (await _changed.WaitAsync(_watchDelay, cancellationToken)) { }
            }

            private void OnChanged(object sender, FileSystemEventArgs e)
            {
                if (Paused || !IsWatched(e.FullPath)) return;

                LastChange = Path.GetRelativePath(_directory, e.FullPath);
                _changed.Release();
            }

            private bool IsWatched(string path)
            {
                var relativePath = Path.GetRelativePath(_directory, path);
                var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Take(segments.Length - 1).Any(o => _ignoredDirectories.Contains(o, StringComparer.OrdinalIgnoreCase))) return false;

                var extension = Path.GetExtension(path);
                if (!_watchExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return false;

                // The Project file copies the build writes (ex. "Contoso.Hello-0.0.1.3.csproj")
                var filename = Path.GetFileName(path);
                if (filename.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && filename.Contains('-')) return false;

                return true;
            }
        }
    }
}
