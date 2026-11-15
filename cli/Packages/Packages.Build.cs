// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using TrakHound.Packages;

namespace TrakHound.CLI.Packages
{
    internal static partial class Packages
    {
        [Command("build", $"Build (and optionally Publish) the DotNet Package projects listed in a Build File ({TrakHoundBuildFile.Filename})")]
        [CommandHelp(null, "th packages build ./trakhound.build.json --only Http,Mqtt")]
        public static async Task<bool> Build(
            [CommandParameter($"The path to the Build File (or a directory containing a {TrakHoundBuildFile.Filename} file). If not specified, the current working directory is used")]
            string path = null,
            [CommandOption("Only build the Projects whose path contains one of these values (comma separated)")]
            string only = null,
            [CommandOption("Only build the Project with this directory name or path (an exact match). Can be repeated")]
            string[] project = null,
            [CommandOption("Stop at the first Project that fails")]
            bool failFast = false,
            [CommandOption("List the Projects and their settings without building")]
            bool dryRun = false,
            [CommandOption("A Prerelease Label to add to each Project's Version (ex. dev.{timestamp}), replacing the Publish Target's \"versionSuffix\". Every Package in the build gets the same {timestamp}")]
            string versionSuffix = null,
            [CommandOption($"The path to the private key to sign the Packages with. If not specified, the {SigningKeyVariable} environment variable is used if set")]
            string signKey = null,
            [CommandOption(AllowDirtyDescription)]
            bool allowDirty = false,
            [CommandOption(TargetDescription + " (for each Project that publishes)", "-t")]
            string target = null,
            [CommandOption(SkipExistingDescription + " (for each Project that publishes)")]
            bool skipExisting = false
            )
        {
            return await BuildProjects(path, only, project, failFast, dryRun, versionSuffix, signKey, allowDirty, target, skipExisting);
        }

        private static async Task<bool> BuildProjects(string path, string only, string[] projectNames, bool failFast, bool dryRun, string versionSuffix, string signKey, bool allowDirty, string target, bool skipExisting = false)
        {
            // Checked before anything is built (each Project would otherwise fail with the same error)
            if (!IsValidVersionSuffix(versionSuffix)) return false;

            var buildFilePath = CliPath.Resolve(path);
            if (Directory.Exists(buildFilePath)) buildFilePath = Path.Combine(buildFilePath, TrakHoundBuildFile.Filename);

            if (!File.Exists(buildFilePath))
            {
                Console.Error.WriteLine($"Error : Build File not Found : ({buildFilePath})");
                return false;
            }

            TrakHoundBuildFile buildFile;
            try
            {
                buildFile = TrakHoundBuildFile.Read(buildFilePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error : Invalid Build File ({buildFilePath}) : {ex.Message}");
                return false;
            }

            if (buildFile?.Projects.IsNullOrEmpty() != false)
            {
                Console.Error.WriteLine($"Error : No Projects found in Build File ({buildFilePath})");
                return false;
            }

            // Relative paths in the Build File are resolved against the Build File directory
            var baseDirectory = Path.GetDirectoryName(buildFilePath);

            var projects = new List<TrakHoundBuildProject>();
            for (var i = 0; i < buildFile.Projects.Count; i++)
            {
                var project = buildFile.Projects[i]?.WithDefaults(buildFile.Defaults);
                if (string.IsNullOrEmpty(project?.Path))
                {
                    Console.Error.WriteLine($"Error : Project #{i + 1} in the Build File has no 'path'");
                    return false;
                }

                project.Path = Path.GetFullPath(project.Path, baseDirectory);
                if (!string.IsNullOrEmpty(project.Output)) project.Output = Path.GetFullPath(project.Output, baseDirectory);

                projects.Add(project);
            }

            // Each Project directory can only be listed once (packaging temporarily renames the .csproj file)
            var duplicates = projects
                .GroupBy(o => GetProjectDirectory(o.Path), StringComparer.OrdinalIgnoreCase)
                .Where(o => o.Count() > 1)
                .Select(o => o.Key)
                .ToList();
            if (duplicates.Count > 0)
            {
                foreach (var duplicate in duplicates) Console.Error.WriteLine($"Error : Project directory listed more than once : ({duplicate})");
                return false;
            }

            if (!string.IsNullOrEmpty(only))
            {
                var filters = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                projects = projects.Where(o => filters.Any(f => o.Path.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();

                if (projects.Count == 0)
                {
                    Console.Error.WriteLine($"Error : No Projects match --only ({only})");
                    return false;
                }
            }

            if (!projectNames.IsNullOrEmpty())
            {
                // Unlike --only, a name must match a whole Project (ex. 'TrakHound.Packages.Api' doesn't also select 'TrakHound.Packages.Api.Client')
                foreach (var name in projectNames)
                {
                    if (!projects.Any(o => IsProject(o, name, baseDirectory)))
                    {
                        Console.Error.WriteLine($"Error : No Project matches --project ({name})");
                        return false;
                    }
                }

                projects = projects.Where(o => projectNames.Any(name => IsProject(o, name, baseDirectory))).ToList();
            }

            Console.WriteLine($"Build File Read : ({buildFilePath}) : {projects.Count} Project(s)");

            // A Package is built and published after the Packages in the Build File that it depends on, so a Dependency is never missing from a Destination
            var packages = new Dictionary<TrakHoundBuildProject, TrakHoundPackage>();
            foreach (var project in projects)
            {
                // A missing Information File is reported when the Project is built
                packages[project] = TrakHoundPackage.ReadInformationFromFile(Path.Combine(GetProjectDirectory(project.Path), TrakHoundPackage.PackageConfigurationFilename));
            }

            var packageIds = packages.Values.Where(o => !string.IsNullOrEmpty(o?.Id)).Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // The Dependencies that are in the Build File (the others are expected to be published already)
            List<string> GetDependencyIds(TrakHoundBuildProject project)
            {
                var package = packages[project];
                if (package?.Dependencies == null) return new List<string>();

                return package.Dependencies
                    .Where(o => !string.IsNullOrEmpty(o?.Id) && packageIds.Contains(o.Id) && !string.Equals(o.Id, package.Id, StringComparison.OrdinalIgnoreCase))
                    .Select(o => o.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            projects = TrakHoundPackageDependencyOrder.Sort(projects, o => packages[o]?.Id, GetDependencyIds, out var cyclic);
            foreach (var project in cyclic)
            {
                Console.Error.WriteLine($"Warning : ({packages[project]?.Id}) is part of a Dependency cycle ({string.Join(", ", GetDependencyIds(project))}) : It is built before a Package that it depends on");
            }

            if (dryRun)
            {
                foreach (var project in projects)
                {
                    Console.WriteLine();
                    Console.WriteLine(project.Path);

                    var dependencyIds = GetDependencyIds(project);
                    if (dependencyIds.Count > 0) Console.WriteLine(string.Format("    {0,-22} : {1}", "Builds After", string.Join(", ", dependencyIds)));
                    Console.WriteLine(string.Format("    {0,-22} : {1}", "Configuration", project.ProjectConfiguration ?? "Release"));
                    Console.WriteLine(string.Format("    {0,-22} : {1}", "Include Debug Symbols", project.IncludeDebugSymbols ?? true));
                    Console.WriteLine(string.Format("    {0,-22} : {1}", "Include Source", project.IncludeSource ?? false));
                    Console.WriteLine(string.Format("    {0,-22} : {1}", "Publish", project.Publish ?? false));
                    Console.WriteLine(string.Format("    {0,-22} : {1}", "Output", project.Output ?? ((project.Publish ?? false) ? "(none)" : Environment.CurrentDirectory)));
                    if (!string.IsNullOrEmpty(project.Version)) Console.WriteLine(string.Format("    {0,-22} : {1}", "Version", project.Version));
                    if (!string.IsNullOrEmpty(versionSuffix)) Console.WriteLine(string.Format("    {0,-22} : {1}", "Version Suffix", versionSuffix));
                    if (!string.IsNullOrEmpty(project.Server)) Console.WriteLine(string.Format("    {0,-22} : {1}", "Server", project.Server));
                    if (!string.IsNullOrEmpty(project.Organization)) Console.WriteLine(string.Format("    {0,-22} : {1}", "Organization", project.Organization));

                    var projectDirectory = GetProjectDirectory(project.Path);

                    PublishSettings publishSettings = null;
                    if (project.Publish ?? false)
                    {
                        publishSettings = ReadPublishSettings(projectDirectory, target, log: false);
                        if (publishSettings?.HasTarget == true)
                        {
                            Console.WriteLine(string.Format("    {0,-22} : {1} ({2})", "Publish Target", publishSettings.TargetName, publishSettings.Path));
                            foreach (var destination in publishSettings.Destinations)
                            {
                                var token = !string.IsNullOrWhiteSpace(destination.ApiTokenVariable)
                                    ? $" (Api Token : {destination.ApiTokenVariable.Trim()}{(PackageClient.GetApiToken(destination) == null ? ", NOT SET" : null)})"
                                    : null;

                                Console.WriteLine(string.Format("    {0,-22}   {1} | {2}{3}", "", destination.ManagementServer, destination.Organization, token));
                            }

                            var flags = new List<string>();
                            if (publishSettings.Target.RequireSigning) flags.Add("requireSigning");
                            if (publishSettings.Target.AllowDirty) flags.Add("allowDirty");
                            if (flags.Count > 0) Console.WriteLine(string.Format("    {0,-22} : {1}", "Target Settings", string.Join(", ", flags)));
                        }
                    }

                    // The Version the Project would be built with (the Publish Target's Suffix only applies when it's published)
                    var informationPath = Path.Combine(projectDirectory, TrakHoundPackage.PackageConfigurationFilename);
                    var packageVersion = GetPackageVersion(projectDirectory, informationPath, project.Version, versionSuffix, publishSettings, log: false);
                    Console.WriteLine(string.Format("    {0,-22} : {1}", "Package Version", packageVersion ?? "(none : see the error above)"));
                }
                return true;
            }

            // Every Project that publishes is checked against its Publish Target before anything is built (a problem would otherwise only fail
            // its own Project, after the Projects before it were built) : its Api Tokens, its Signing Key, and uncommitted changes
            var publishProblems = false;
            foreach (var project in projects.Where(o => o.Publish ?? false))
            {
                var projectDirectory = GetProjectDirectory(project.Path);

                var publishSettings = ReadPublishSettings(projectDirectory, target, log: false);
                if (publishSettings == null || !CheckServerOptions(publishSettings, project.Server, project.Organization) || !CheckApiTokens(publishSettings))
                {
                    publishProblems = true;
                    continue;
                }

                using var signer = GetSigner(signKey, projectDirectory);
                var projectAllowDirty = allowDirty;
                if (!CheckPublishTarget(publishSettings, projectDirectory, signer != null, ref projectAllowDirty)) publishProblems = true;
            }
            if (publishProblems) return false;
            // Every Project is built before any Package is published, so a Project that fails doesn't leave only some of the Packages published
            var results = projects.Select(o => new BuildResult { Project = o }).ToList();
            for (var i = 0; i < results.Count; i++)
            {
                var result = results[i];
                var project = result.Project;

                Console.WriteLine();
                Console.WriteLine("==========");
                Console.WriteLine($"[{i + 1}/{results.Count}] {project.Path}");
                Console.WriteLine("==========");

                var stopwatch = Stopwatch.StartNew();
                try
                {
                    result.Build = await BuildDotNet(
                        path: project.Path,
                        output: project.Output,
                        projectConfiguration: !string.IsNullOrEmpty(project.ProjectConfiguration) ? project.ProjectConfiguration : "Release",
                        includeDebugSymbols: project.IncludeDebugSymbols ?? true,
                        includeSource: project.IncludeSource ?? false,
                        publish: project.Publish ?? false,
                        version: project.Version,
                        versionSuffix: versionSuffix,
                        server: project.Server,
                        organization: project.Organization,
                        signKey: signKey,
                        allowDirty: allowDirty,
                        target: target,
                        skipExisting: skipExisting
                        );
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error : {ex.Message}");
                }
                stopwatch.Stop();

                result.Status = result.Build != null ? BuildStatus.Ok : BuildStatus.Failed;
                result.Duration = stopwatch.Elapsed;

                // The remaining Projects stay Skipped
                if (result.Build == null && failFast) break;
            }

            var publishResults = results.Where(o => o.Project.Publish ?? false).ToList();
            var notBuilt = results.Count(o => o.Build == null);
            if (notBuilt > 0)
            {
                var built = publishResults.Where(o => o.Build != null).ToList();
                foreach (var result in built) result.Status = BuildStatus.NotPublished;

                if (built.Count > 0)
                {
                    Console.WriteLine();
                    Console.Error.WriteLine($"Error : {notBuilt} Project(s) were not built : Nothing Published");
                }
            }
            else
            {
                // The Packages that weren't published, so that the Packages that depend on them aren't published either
                var notPublished = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < publishResults.Count; i++)
                {
                    var result = publishResults[i];
                    var information = result.Build.Information;

                    Console.WriteLine();
                    Console.WriteLine("==========");
                    Console.WriteLine($"Publish [{i + 1}/{publishResults.Count}] {information.Id} : v{information.Version}");
                    Console.WriteLine("==========");

                    var missing = GetDependencyIds(result.Project).Where(notPublished.Contains).ToList();
                    if (missing.Count > 0)
                    {
                        Console.Error.WriteLine($"Error : Not Published : It depends on a Package that was not published ({string.Join(", ", missing)})");
                        result.Status = BuildStatus.NotPublished;
                        notPublished.Add(information.Id);
                        continue;
                    }

                    var stopwatch = Stopwatch.StartNew();
                    var success = false;
                    try
                    {
                        success = await PublishDotNet(result.Build);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error : {ex.Message}");
                    }
                    stopwatch.Stop();

                    result.Duration += stopwatch.Elapsed;
                    if (!success)
                    {
                        result.Status = BuildStatus.Failed;
                        notPublished.Add(information.Id);
                    }
                    else if (result.Build.PublishDestinations?.Destinations.Count < 1)
                    {
                        // Every Destination already has it from this commit (--skip-existing), so the Packages that depend on it can be published
                        result.Status = BuildStatus.AlreadyPublished;
                    }

                    // The Package file is no longer needed
                    result.Build.PackageBytes = null;
                }
            }

            // Print Summary
            Console.WriteLine();
            Console.WriteLine("Build Summary");
            Console.WriteLine("-------------------");
            foreach (var result in results)
            {
                var name = Path.GetFileName(GetProjectDirectory(result.Project.Path));
                Console.WriteLine(string.Format("  {0,-18} {1,-50} {2,8}", result.Status, name, result.Status != BuildStatus.Skipped ? $"{result.Duration.TotalSeconds:0.0}s" : null));
            }

            var failed = results.Count(o => o.Status == BuildStatus.Failed);
            var notPublishedCount = results.Count(o => o.Status == BuildStatus.NotPublished);
            var skippedCount = results.Count(o => o.Status == BuildStatus.Skipped);
            var alreadyPublishedCount = results.Count(o => o.Status == BuildStatus.AlreadyPublished);
            Console.WriteLine();
            Console.WriteLine($"{results.Count(o => o.Status == BuildStatus.Ok)} Succeeded, {failed} Failed"
                + (alreadyPublishedCount > 0 ? $", {alreadyPublishedCount} Already Published" : null)
                + (notPublishedCount > 0 ? $", {notPublishedCount} Not Published" : null)
                + (skippedCount > 0 ? $", {skippedCount} Skipped" : null));

            return results.All(o => o.Status == BuildStatus.Ok || o.Status == BuildStatus.AlreadyPublished);
        }

        private static class BuildStatus
        {
            public const string Ok = "OK";
            public const string Failed = "FAILED";

            /// <summary>
            /// The Package was built, but not uploaded because another Project (or a Package it depends on) failed
            /// </summary>
            public const string NotPublished = "NOT PUBLISHED";

            /// <summary>
            /// The Package was built, but every Destination already has its Version from the same commit, so it wasn't uploaded (with --skip-existing)
            /// </summary>
            public const string AlreadyPublished = "ALREADY PUBLISHED";

            /// <summary>
            /// The Project wasn't built because an earlier Project failed (with --fail-fast)
            /// </summary>
            public const string Skipped = "SKIPPED";
        }

        private class BuildResult
        {
            public TrakHoundBuildProject Project { get; set; }

            public string Status { get; set; } = BuildStatus.Skipped;

            public TimeSpan Duration { get; set; }

            public DotNetBuild Build { get; set; }
        }

        /// <summary>
        /// Returns true if the name is the Project's directory name, or its path (absolute or relative to the Build File directory)
        /// </summary>
        private static bool IsProject(TrakHoundBuildProject project, string name, string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            var projectDirectory = GetProjectDirectory(project.Path);
            if (string.Equals(Path.GetFileName(projectDirectory), name.Trim(), StringComparison.OrdinalIgnoreCase)) return true;

            try
            {
                return string.Equals(GetProjectDirectory(Path.GetFullPath(name.Trim(), baseDirectory)), projectDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // Not a valid path
                return false;
            }
        }

        private static string GetProjectDirectory(string path)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(fullPath) : fullPath;
        }
    }
}
