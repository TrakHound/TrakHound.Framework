// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace TrakHound.Packages
{
    public class TrakHoundDotNetPackage
    {
        /// <summary>
        /// Environment variable that names what's building the Package (ex. the URL of a CI run)
        /// </summary>
        private const string BuilderVariable = "TRAKHOUND_BUILDER";

        private static IEnumerable<string> _sourceIgnorePatterns = new string[]
        {
            "[Bb]in\\\\",
            "[Oo]bj\\\\",
            "\\.vs",

            // Repository history and debug settings (which can hold an API key) don't belong in a Package
            "(^|[\\\\/])\\.git([\\\\/]|$)",
            "(^|[\\\\/])trakhound\\.debug\\.json$"
        };


        // Static Web Asset directories that are provided by the TrakHound Instance (csproj ExcludeAssets doesn't exclude these)
        private static readonly string[] _excludedContentDirectories = new string[]
        {
            "TrakHound.Blazor",
            "Radzen.Blazor"
        };


        public static byte[] Create(string projectPath, TrakHoundDotNetPackageSettings settings)
        {
            if (!string.IsNullOrEmpty(projectPath))
            {
                try
                {
                    var path = Path.GetFullPath(projectPath, Environment.CurrentDirectory);

                    if (string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        return CreateFromProjectFile(path, settings);
                    }
                    else if (Directory.Exists(path))
                    {
                        var informationPath = Path.Combine(path, TrakHoundPackage.PackageConfigurationFilename);
                        var information = TrakHoundPackage.ReadInformationFromFile(informationPath);
                        if (information != null)
                        {
                            var projectFilePath = FindProjectFile(path, information.Id, out var error);
                            if (projectFilePath != null) return CreateFromProjectFile(projectFilePath, settings);

                            Console.Error.WriteLine(error);
                            return null;
                        }

                        Console.Error.WriteLine($"Package Information File not Found : ({informationPath})");
                    }
                    else
                    {
                        Console.Error.WriteLine($"Project Path not Found : ({path})");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("TrakHoundDotNetPackage.Create() : " + ex.Message);
                }
            }

            return null;
        }

        // The Project file copy a build writes next to the Project ([id]-[version].csproj, ex. "Contoso.Hello-0.0.1.3.csproj")
        private static readonly System.Text.RegularExpressions.Regex _temporaryProjectCopyRegex = new(@"-\d+\.\d+\.\d+[^\\/]*\.csproj$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>
        /// Whether a file is the Project file copy that packing writes next to the Project (ex. "Contoso.Hello-0.0.1.3.csproj"), and removes when it's done
        /// </summary>
        public static bool IsTemporaryProjectCopy(string path) => !string.IsNullOrEmpty(path) && _temporaryProjectCopyRegex.IsMatch(Path.GetFileName(path));

        /// <summary>
        /// The Project file of a Package's directory : [packageId].csproj, or else the directory's only Project file (ex. a project created
        /// from a template as "example-api-01" has example-api-01.csproj, while its Package ID is example_api_01). A Project file that packing
        /// moved aside (to .tmp) when it was interrupted is found by its original name, and restored when it's packed.
        /// Returns null, with the reason in error, when the directory has none, or more than one and none is named after the Package
        /// </summary>
        public static string FindProjectFile(string directory, string packageId, out string error)
        {
            error = null;

            if (!string.IsNullOrEmpty(packageId))
            {
                var named = Path.Combine(directory, $"{packageId}.csproj");
                if (File.Exists(named) || File.Exists(Path.ChangeExtension(named, ".tmp"))) return named;
            }

            var projectFiles = Directory.GetFiles(directory, "*.csproj")
                .Where(o => !IsTemporaryProjectCopy(o))
                .ToList();

            // An interrupted run leaves the original Project as [name].tmp
            if (projectFiles.Count == 0)
            {
                projectFiles = Directory.GetFiles(directory, "*.tmp")
                    .Where(IsProjectFile)
                    .Select(o => Path.ChangeExtension(o, ".csproj"))
                    .ToList();
            }

            if (projectFiles.Count == 1) return projectFiles[0];

            error = projectFiles.Count == 0
                ? $"Project File not Found : ({directory}) has no .csproj file"
                : $"Project File not Found : ({directory}) has more than one .csproj file ({string.Join(", ", projectFiles.Select(Path.GetFileName))}) and none is named {packageId}.csproj. Pass the .csproj file instead of the directory";
            return null;
        }

        private static bool IsProjectFile(string path)
        {
            try
            {
                using var reader = new StreamReader(path);
                var buffer = new char[256];
                var length = reader.Read(buffer, 0, buffer.Length);
                return new string(buffer, 0, length).TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith("<Project", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static byte[] CreateFromProjectFile(string projectFilePath, TrakHoundDotNetPackageSettings settings)
        {
            if (string.IsNullOrEmpty(projectFilePath)) return null;

            Console.WriteLine($"Packing DotNet Project : ({projectFilePath})");

            var projectDir = Path.GetDirectoryName(projectFilePath);
            var tempProjectFilePath = Path.ChangeExtension(projectFilePath, ".tmp");

            // Recover the original Project file if a previous run was interrupted
            if (!File.Exists(projectFilePath) && File.Exists(tempProjectFilePath))
            {
                Console.WriteLine($"Restoring Project File from a previous interrupted run : ({tempProjectFilePath})");
                File.Move(tempProjectFilePath, projectFilePath);
            }

            if (!File.Exists(projectFilePath))
            {
                Console.Error.WriteLine($"Project File not Found : ({projectFilePath})");
                return null;
            }

            if (File.Exists(tempProjectFilePath))
            {
                Console.Error.WriteLine($"Temporary Project File already exists. Remove it and try again : ({tempProjectFilePath})");
                return null;
            }

            var packageConfigurationPath = Path.Combine(projectDir, TrakHoundPackage.PackageConfigurationFilename);
            var packageConfiguration = TrakHoundPackage.ReadInformationFromFile(packageConfigurationPath);
            if (packageConfiguration == null)
            {
                Console.Error.WriteLine($"Package Information File not Found : ({packageConfigurationPath})");
                return null;
            }

            string packageTempDir = null;
            string newProjectTempPath = null;
            var projectMoved = false;

            try
            {
                var packageBuilder = new TrakHoundPackageBuilder(packageConfiguration);

                // Override Assembly/Package Version
                if (!string.IsNullOrEmpty(settings.Version)) packageBuilder.Version = settings.Version;

                // Set TrakHound Version (the TrakHound.Common version the Project references, from a Package or Project Reference)
                var trakHoundReferences = GetTrakHoundReferences(projectDir);

                // Without a restore, the converted Project references whichever TrakHound version is latest, so the signed Package couldn't be rebuilt
                if (trakHoundReferences == null && settings.Signer != null)
                {
                    Restore(projectFilePath, settings);
                    trakHoundReferences = GetTrakHoundReferences(projectDir);
                }

                var trakHoundVersion = trakHoundReferences?.Version;
                if (string.IsNullOrEmpty(trakHoundVersion) && settings.Signer != null)
                {
                    Console.Error.WriteLine($"Error : Unable to find the TrakHound version the Project references, so a signed Package couldn't be rebuilt : ({projectFilePath})");
                    return null;
                }

                if (!string.IsNullOrEmpty(trakHoundVersion))
                {
                    packageBuilder.SetMetadata(TrakHoundPackage.TrakHoundVersionName, trakHoundVersion);
                }

                Console.WriteLine("trakHoundVersion = " + trakHoundVersion);

                // Add Git Branch and Commit SHA
                if (!string.IsNullOrEmpty(settings.GitBranch)) packageBuilder.SetMetadata(TrakHoundPackage.RepositoryBranchName, settings.GitBranch);
                if (!string.IsNullOrEmpty(settings.GitCommit)) packageBuilder.SetMetadata(TrakHoundPackage.RepositoryCommitName, settings.GitCommit);
                if (settings.GitDirty.HasValue) packageBuilder.SetMetadata(TrakHoundPackage.RepositoryDirtyName, settings.GitDirty.Value ? "true" : "false");

                // The compiler embeds what Razor generates in the Symbols, including full paths that the Path Map can't reach
                if (settings.Signer != null && settings.IncludeDebugSymbols && string.Equals(packageBuilder.Category, "app", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Warning : An App with Debug Symbols can't be rebuilt to the same files from a different directory. Use --include-debug-symbols false so that th packages rebuild can check it");
                }

                // What a rebuild needs to match this one
                packageBuilder.SetMetadata(TrakHoundPackage.BuildConfigurationName, settings.Configuration);
                packageBuilder.SetMetadata(TrakHoundPackage.DotnetSdkVersionName, GetSdkVersion(projectDir, settings));
                packageBuilder.SetMetadata(TrakHoundPackage.BuilderName, Environment.GetEnvironmentVariable(BuilderVariable));

                if (!string.IsNullOrEmpty(settings.GitCommit) && string.IsNullOrEmpty(packageConfiguration.GetMetadata(TrakHoundPackage.RepositoryName)))
                {
                    Console.WriteLine($"Warning : The commit was recorded, but '{TrakHoundPackage.RepositoryName}' isn't set in {TrakHoundPackage.PackageConfigurationFilename}, so the Package can't be traced to a repository");
                }

                // Publish directly to the package's 'dist' directory
                packageTempDir = TrakHoundTemp.CreateDirectory();
                var distDir = Path.Combine(packageTempDir, TrakHoundPackage.DistributableDirectory);

                newProjectTempPath = Path.Combine(projectDir, $"{packageBuilder.Id}-{packageBuilder.Version}.csproj");

                // Move the original Project file out of the way (restored in 'finally')
                File.Move(projectFilePath, tempProjectFilePath);
                projectMoved = true;

                // Copy and Convert Project file to add category specific configuration
                ConvertProjectFile(trakHoundReferences, packageBuilder.Category, packageBuilder.Id, tempProjectFilePath, newProjectTempPath);

                // The converted Project builds in its own temporary 'obj' and 'bin' directories (see AddOutputProperties) and excludes
                // the Project's 'obj' and 'bin' directories, so they are left as they are (and Visual Studio is not affected).
                // 'dotnet publish' restores the converted Project itself
                var package = packageBuilder.Build();

                if (!PublishProject(package, settings, newProjectTempPath, distDir))
                {
                    Console.Error.WriteLine("Publish Error");
                    return null;
                }

                // Set the TrakHound Version that was actually built against (ex. when the Project had not been restored,
                // the converted Project references the latest TrakHound Package), read from the converted Project's restore
                var resolvedTrakHoundVersion = GetResolvedTrakHoundVersion(newProjectTempPath);
                if (!string.IsNullOrEmpty(resolvedTrakHoundVersion) && resolvedTrakHoundVersion != trakHoundVersion)
                {
                    packageBuilder.SetMetadata(TrakHoundPackage.TrakHoundVersionName, resolvedTrakHoundVersion);
                    package = packageBuilder.Build();

                    Console.WriteLine("trakHoundVersion (resolved) = " + resolvedTrakHoundVersion);
                }

                // Exclude Static Web Assets that are provided by the Instance
                foreach (var excludedContentDirectory in _excludedContentDirectories)
                {
                    var contentDir = Path.Combine(distDir, "wwwroot", "_content", excludedContentDirectory);
                    if (Directory.Exists(contentDir)) Directory.Delete(contentDir, true);
                }

                // Font Awesome Pro is licensed and only supplied by official Instance builds, so it's never in a Package
                var fontAwesomeDir = Path.Combine(distDir, "wwwroot", "_content", "TrakHound.Blazor.Hosting", "css", "font-awesome");
                if (Directory.Exists(fontAwesomeDir)) Directory.Delete(fontAwesomeDir, true);

                CopyReadMeToRoot(distDir, packageTempDir);

                if (settings.IncludeSource)
                {
                    // Copy Source files and compress to "src.zip"
                    var srcZipTemp = TrakHoundTemp.CreateDirectory();
                    TrakHoundFiles.Copy(projectDir, srcZipTemp, _sourceIgnorePatterns);

                    // The Project file is moved aside while building, so put the original back in place of the converted one (which has temporary paths)
                    File.Delete(Path.Combine(srcZipTemp, Path.GetFileName(newProjectTempPath)));
                    File.Move(Path.Combine(srcZipTemp, Path.GetFileName(tempProjectFilePath)), Path.Combine(srcZipTemp, Path.GetFileName(projectFilePath)));

                    TrakHoundFiles.Zip(srcZipTemp, Path.Combine(packageTempDir, $"{TrakHoundPackage.SourceDirectory}.zip"));
                    Directory.Delete(srcZipTemp, true);
                }

                WriteSbom(newProjectTempPath, package, Path.Combine(packageTempDir, TrakHoundPackage.SbomFilename));

                return package.Package(packageTempDir, settings.Signer);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("TrakHoundDotNetPackage.CreateFromProjectFile() : " + projectFilePath + " : " + ex.Message + " : " + ex.StackTrace);
                return null;
            }
            finally
            {
                try
                {
                    if (newProjectTempPath != null && File.Exists(newProjectTempPath)) File.Delete(newProjectTempPath);
                    if (packageTempDir != null && Directory.Exists(packageTempDir)) Directory.Delete(packageTempDir, true);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("Error removing temporary files : " + ex.Message);
                }

                // Always put the original Project file back
                if (projectMoved) File.Move(tempProjectFilePath, projectFilePath);
            }
        }

        /// <summary>
        /// Restore a Project's NuGet Packages. Returns true if successful
        /// </summary>
        internal static bool Restore(string projectFilePath, TrakHoundDotNetPackageSettings settings)
        {
            return RunCommand(null, GetDotnetExecutable(settings), $"restore \"{projectFilePath}\"");
        }

        /// <summary>
        /// Get the version of the DotNet SDK used in a directory (global.json can select it). Returns null if it can't be read
        /// </summary>
        private static string GetSdkVersion(string directory, TrakHoundDotNetPackageSettings settings)
        {
            try
            {
                var startInfo = new ProcessStartInfo(GetDotnetExecutable(settings), "--version");
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.WorkingDirectory = directory;

                using (var process = Process.Start(startInfo))
                {
                    process.ErrorDataReceived += (s, e) => { };
                    process.BeginErrorReadLine();

                    var version = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    return process.ExitCode == 0 ? version : null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Copy the Readme in the 'dist' directory (Readme.md or Readme.txt, any case) to the root of the Package as Readme.md or Readme.txt.
        /// The Instance reads it from 'dist', and package servers (ex. packages.trakhound.com) read it from the root without extracting 'dist'
        /// </summary>
        private static void CopyReadMeToRoot(string distDir, string packageDir)
        {
            if (!Directory.Exists(distDir)) return;

            foreach (var extension in TrakHoundPackage.ReadMeFileExtensions)
            {
                var filename = TrakHoundPackage.ReadMeFilename + extension;
                var readMePath = Directory.GetFiles(distDir, "*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(o => string.Equals(Path.GetFileName(o), filename, StringComparison.OrdinalIgnoreCase));

                if (readMePath != null)
                {
                    File.Copy(readMePath, Path.Combine(packageDir, filename), true);
                    return;
                }
            }
        }

        /// <summary>
        /// Write the NuGet Packages a converted Project was built with as a CycloneDX SBOM. It has no timestamp, so that a rebuild of the same commit gives the same file
        /// </summary>
        private static void WriteSbom(string convertedProjectFilePath, TrakHoundPackage package, string outputPath)
        {
            try
            {
                var assetsPath = GetConvertedAssetsPath(convertedProjectFilePath);
                var assets = assetsPath != null && File.Exists(assetsPath) ? Json.Read<ProjectAssetsFile>(assetsPath) : null;
                if (assets?.Libraries == null)
                {
                    Console.WriteLine("Warning : Unable to read the Project's NuGet Packages, so the Package has no SBOM");
                    return;
                }

                var components = assets.Libraries
                    .Where(o => o.Value?.Type == "package" && o.Key.Contains('/'))
                    .OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(o =>
                    {
                        var parts = o.Key.Split('/');
                        return new
                        {
                            type = "library",
                            name = parts[0],
                            version = parts[1],
                            purl = $"pkg:nuget/{parts[0]}@{parts[1]}",
                            hashes = !string.IsNullOrEmpty(o.Value.Sha512) ? new[] { new { alg = "SHA-512", content = Convert.ToHexStringLower(Convert.FromBase64String(o.Value.Sha512)) } } : null
                        };
                    });

                var sbom = new
                {
                    bomFormat = "CycloneDX",
                    specVersion = "1.5",
                    version = 1,
                    metadata = new { component = new { type = "library", name = package.Id, version = package.Version } },
                    components
                };

                File.WriteAllBytes(outputPath, JsonSerializer.SerializeToUtf8Bytes(sbom, new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning : Unable to write the SBOM : {ex.Message}");
            }
        }

        private static string GetDotnetExecutable(TrakHoundDotNetPackageSettings settings)
        {
            if (settings != null && !string.IsNullOrEmpty(settings.DotnetSdkLocation)) return Path.Combine(settings.DotnetSdkLocation, "dotnet");
            return "dotnet";
        }

        private static bool PublishProject(TrakHoundPackage packageConfiguration, TrakHoundDotNetPackageSettings settings, string projectFilePath, string outputPath)
        {
            if (packageConfiguration != null && settings != null && !string.IsNullOrEmpty(settings.Configuration) && !string.IsNullOrEmpty(projectFilePath))
            {
                var arguments = new List<string>();
                arguments.Add("publish");
                arguments.Add($"\"{projectFilePath}\"");
                arguments.Add($"-c:{settings.Configuration}");

                if (!settings.IncludeDebugSymbols)
                {
                    arguments.Add("/p:DebugType=None");
                    arguments.Add("/p:DebugSymbols=false");
                }

                arguments.Add($"-o:\"{outputPath}\"");

                // The source and temporary directories differ on every build, and would otherwise end up in the Assembly and its Symbols.
                // Symbols also record the identity of referenced Assemblies, so this is passed to the whole build rather than set in the Project.
                // %2C is a comma that MSBuild doesn't treat as the end of the value
                var pathMap = $"{TrakHound.CLI.GitCommands.GetRootDirectory(Path.GetDirectoryName(projectFilePath))}=/_/%2C{GetTempDirectory(projectFilePath)}=/_build/";
                arguments.Add("/p:Deterministic=true");

                // Generated sources (ex. from Razor) hold full paths that the map doesn't reach, and they would be embedded in the Symbols
                arguments.Add("/p:EmbedUntrackedSources=false");
                arguments.Add($"/p:PathMap=\"{pathMap}\"");

                return RunCommand(null, GetDotnetExecutable(settings), string.Join(' ', arguments));
            }

            return false;
        }

        private static void ConvertProjectFile(TrakHoundReferences trakhoundReferences, string packageCategory, string packageId, string inputProjectFilePath, string outputProjectFilePath)
        {
            if (!string.IsNullOrEmpty(packageCategory) && !string.IsNullOrEmpty(inputProjectFilePath) && !string.IsNullOrEmpty(outputProjectFilePath))
            {
                switch (packageCategory.ToLower())
                {
                    case "api": ConvertApiProjectFile(trakhoundReferences, packageId, inputProjectFilePath, outputProjectFilePath); break;
                    case "app": ConvertAppProjectFile(trakhoundReferences, packageId, inputProjectFilePath, outputProjectFilePath); break;
                    case "function": ConvertFunctionProjectFile(trakhoundReferences, packageId, inputProjectFilePath, outputProjectFilePath); break;
                    case "worker": ConvertWorkerProjectFile(trakhoundReferences, packageId, inputProjectFilePath, outputProjectFilePath); break;
                    case "service": ConvertServiceProjectFile(trakhoundReferences, packageId, inputProjectFilePath, outputProjectFilePath); break;
                    case "identity": ConvertIdentityProjectFile(trakhoundReferences, packageId, inputProjectFilePath, outputProjectFilePath); break;
                }
            }
        }

        private static void ConvertApiProjectFile(TrakHoundReferences trakhoundReferences, string packageId, string inputProjectFilePath, string outputProjectFilePath)
        {
            if (!string.IsNullOrEmpty(inputProjectFilePath) && !string.IsNullOrEmpty(outputProjectFilePath))
            {
                var xml = new XmlDocument();
                xml.Load(inputProjectFilePath);

                var projectNode = xml.SelectSingleNode("/Project");
                if (projectNode != null)
                {
                    ClearProjectSdk(xml, projectNode);
                    RemoveImportSdk(xml);
                    RemoveDebugFiles(xml);
                    RemoveDebugProperties(xml);
                    RemoveDebugReferences(xml);
                    RemoveTrakHoundReferences(xml);

                    AddImportSdkProps(xml, projectNode);
                    AddImportSdkTargets(xml, projectNode);

                    RemoveDebugOutputProperties(xml);
                    AddOutputProperties(xml, projectNode, packageId);

                    AddApiProperties(xml, projectNode);
                    AddApiReferences(xml, projectNode, trakhoundReferences);

                    IgnoreFile(xml, projectNode, "Program.cs", "Compile");

                    xml.Save(outputProjectFilePath);
                }
            }
        }

        private static void ConvertAppProjectFile(TrakHoundReferences trakhoundReferences, string packageId, string inputProjectFilePath, string outputProjectFilePath)
        {
            if (!string.IsNullOrEmpty(inputProjectFilePath) && !string.IsNullOrEmpty(outputProjectFilePath))
            {
                var xml = new XmlDocument();
                xml.Load(inputProjectFilePath);

                var projectNode = xml.SelectSingleNode("/Project");
                if (projectNode != null)
                {
                    ClearProjectSdk(xml, projectNode);
                    RemoveImportSdk(xml);
                    RemoveDebugFiles(xml);
                    RemoveDebugProperties(xml);
                    RemoveDebugReferences(xml);
                    RemoveTrakHoundReferences(xml);

                    AddImportSdkRazorProps(xml, projectNode);
                    AddImportSdkRazorTargets(xml, projectNode);

                    RemoveDebugOutputProperties(xml);
                    AddOutputProperties(xml, projectNode, packageId);

                    AddAppProperties(xml, projectNode);
                    AddAppItems(xml, projectNode);
                    AddAppReferences(xml, projectNode, trakhoundReferences);

                    IgnoreFile(xml, projectNode, "Program.cs", "Compile");
                    IgnoreFile(xml, projectNode, "Debug.razor", "Content");
                    IgnoreFile(xml, projectNode, "DebugRoutes.razor", "Content");

                    // The Razor SDK includes .json files as Content, which would put the debug settings (and any API key) in the package
                    IgnoreFile(xml, projectNode, "trakhound.debug.json", "Content");

                    xml.Save(outputProjectFilePath);
                }
            }
        }

        private static void ConvertFunctionProjectFile(TrakHoundReferences trakhoundReferences, string packageId, string inputProjectFilePath, string outputProjectFilePath)
        {
            if (!string.IsNullOrEmpty(inputProjectFilePath) && !string.IsNullOrEmpty(outputProjectFilePath))
            {
                var xml = new XmlDocument();
                xml.Load(inputProjectFilePath);

                var projectNode = xml.SelectSingleNode("/Project");
                if (projectNode != null)
                {
                    ClearProjectSdk(xml, projectNode);
                    RemoveImportSdk(xml);
                    RemoveDebugFiles(xml);
                    RemoveDebugProperties(xml);
                    RemoveDebugReferences(xml);
                    RemoveTrakHoundReferences(xml);

                    AddImportSdkProps(xml, projectNode);
                    AddImportSdkTargets(xml, projectNode);

                    RemoveDebugOutputProperties(xml);
                    AddOutputProperties(xml, projectNode, packageId);

                    AddFunctionProperties(xml, projectNode);
                    AddFunctionReferences(xml, projectNode, trakhoundReferences);

                    IgnoreFile(xml, projectNode, "Program.cs", "Compile");

                    xml.Save(outputProjectFilePath);
                }
            }
        }

        private static void ConvertServiceProjectFile(TrakHoundReferences trakhoundReferences, string packageId, string inputProjectFilePath, string outputProjectFilePath)
        {
            if (!string.IsNullOrEmpty(inputProjectFilePath) && !string.IsNullOrEmpty(outputProjectFilePath))
            {
                var xml = new XmlDocument();
                xml.Load(inputProjectFilePath);

                var projectNode = xml.SelectSingleNode("/Project");
                if (projectNode != null)
                {
                    ClearProjectSdk(xml, projectNode);
                    RemoveImportSdk(xml);
                    RemoveDebugFiles(xml);
                    RemoveDebugProperties(xml);
                    RemoveDebugReferences(xml);
                    RemoveTrakHoundReferences(xml);

                    AddImportSdkProps(xml, projectNode);
                    AddImportSdkTargets(xml, projectNode);

                    RemoveDebugOutputProperties(xml);
                    AddOutputProperties(xml, projectNode, packageId);

                    AddServiceProperties(xml, projectNode);
                    AddServiceReferences(xml, projectNode, trakhoundReferences);

                    IgnoreFile(xml, projectNode, "Program.cs", "Compile");

                    xml.Save(outputProjectFilePath);
                }
            }
        }

        private static void ConvertWorkerProjectFile(TrakHoundReferences trakhoundReferences, string packageId, string inputProjectFilePath, string outputProjectFilePath)
        {
            if (!string.IsNullOrEmpty(inputProjectFilePath) && !string.IsNullOrEmpty(outputProjectFilePath))
            {
                var xml = new XmlDocument();
                xml.Load(inputProjectFilePath);

                var projectNode = xml.SelectSingleNode("/Project");
                if (projectNode != null)
                {
                    ClearProjectSdk(xml, projectNode);
                    RemoveImportSdk(xml);
                    RemoveDebugFiles(xml);
                    RemoveDebugProperties(xml);
                    RemoveDebugReferences(xml);
                    RemoveTrakHoundReferences(xml);

                    AddImportSdkProps(xml, projectNode);
                    AddImportSdkTargets(xml, projectNode);

                    RemoveDebugOutputProperties(xml);
                    AddOutputProperties(xml, projectNode, packageId);

                    AddWorkerProperties(xml, projectNode);
                    AddWorkerReferences(xml, projectNode, trakhoundReferences);

                    IgnoreFile(xml, projectNode, "Program.cs", "Compile");

                    xml.Save(outputProjectFilePath);
                }
            }
        }

        private static void ConvertIdentityProjectFile(TrakHoundReferences trakhoundReferences, string packageId, string inputProjectFilePath, string outputProjectFilePath)
        {
            if (!string.IsNullOrEmpty(inputProjectFilePath) && !string.IsNullOrEmpty(outputProjectFilePath))
            {
                var xml = new XmlDocument();
                xml.Load(inputProjectFilePath);

                var projectNode = xml.SelectSingleNode("/Project");
                if (projectNode != null)
                {
                    ClearProjectSdk(xml, projectNode);
                    RemoveImportSdk(xml);
                    RemoveDebugFiles(xml);
                    RemoveDebugProperties(xml);
                    RemoveDebugReferences(xml);
                    RemoveTrakHoundReferences(xml);

                    AddImportSdkProps(xml, projectNode);
                    AddImportSdkTargets(xml, projectNode);

                    RemoveDebugOutputProperties(xml);
                    AddOutputProperties(xml, projectNode, packageId);

                    AddIdentityProperties(xml, projectNode);
                    AddIdentityReferences(xml, projectNode, trakhoundReferences);

                    IgnoreFile(xml, projectNode, "Program.cs", "Compile");

                    xml.Save(outputProjectFilePath);
                }
            }
        }


        private static void ClearProjectSdk(XmlDocument xml, XmlNode projectNode)
        {
            projectNode.Attributes.RemoveNamedItem("Sdk");
        }

        private static void RemoveImportSdk(XmlDocument xml)
        {
            // Remove Web SDK props (Web Api Project)
            var nodes = xml.SelectNodes("//Import[@Project='Sdk.props']");
            if (nodes != null)
            {
                foreach (XmlNode x in nodes) x.ParentNode.RemoveChild(x);
            }

            // Remove Web SDK props (Web Api Project)
            nodes = xml.SelectNodes("//Import[@Project='Sdk.targets']");
            if (nodes != null)
            {
                foreach (XmlNode x in nodes) x.ParentNode.RemoveChild(x);
            }
        }

        private static void RemoveDebugFiles(XmlDocument xml)
        {
            var nodes = xml.SelectNodes("//ItemGroup[@Label='files-trakhound-debug']");
            if (nodes != null)
            {
                foreach (XmlNode x in nodes) x.ParentNode.RemoveChild(x);
            }
        }

        private static void RemoveDebugProperties(XmlDocument xml)
        {
            RemoveProperty(xml, "OutputType");
        }

        private static void RemoveDebugReferences(XmlDocument xml)
        {
            var nodes = xml.SelectNodes("//ItemGroup[@Label='references-trakhound-debug']");
            if (nodes != null)
            {
                foreach (XmlNode x in nodes) x.ParentNode.RemoveChild(x);
            }
        }

        private static void RemoveTrakHoundReferences(XmlDocument xml)
        {
            RemoveReference(xml, "TrakHound.Blazor");
            RemoveReference(xml, "TrakHound.Common");
            RemoveReference(xml, "TrakHound.Clients");
            RemoveReference(xml, "TrakHound.Debug.Apps");
            RemoveReference(xml, "TrakHound.Debug.AspNetCore");
            RemoveReference(xml, "TrakHound.Debug");
            RemoveReference(xml, "TrakHound.Drivers");
        }


        private static void AddImportSdkProps(XmlDocument xml, XmlNode projectNode)
        {
            var importNode = AddImport(xml, projectNode, "Sdk.props", "Microsoft.NET.Sdk");
            projectNode.InsertBefore(importNode, projectNode.FirstChild);
        }

        private static void AddImportSdkTargets(XmlDocument xml, XmlNode projectNode)
        {
            var importNode = AddImport(xml, projectNode, "Sdk.targets", "Microsoft.NET.Sdk");
            projectNode.InsertAfter(importNode, projectNode.LastChild);
        }

        private static void AddImportSdkRazorProps(XmlDocument xml, XmlNode projectNode)
        {
            var importNode = AddImport(xml, projectNode, "Sdk.props", "Microsoft.NET.Sdk.Razor");
            projectNode.InsertBefore(importNode, projectNode.FirstChild);
        }

        private static void AddImportSdkRazorTargets(XmlDocument xml, XmlNode projectNode)
        {
            var importNode = AddImport(xml, projectNode, "Sdk.targets", "Microsoft.NET.Sdk.Razor");
            projectNode.InsertAfter(importNode, projectNode.LastChild);
        }


        private static void AddOutputProperties(XmlDocument xml, XmlNode projectNode, string packageId)
        {
            var tempDir = TrakHoundTemp.CreateDirectory();
            var tempBin = Path.Combine(tempDir, "bin");
            var tempObj = Path.Combine(tempDir, "obj");

            var propertyGroupNode = AddPropertyGroup(xml, projectNode, "trakhound-package-output-properties");

            var rootNamespace = GetProperty(xml, "RootNamespace");
            if (string.IsNullOrEmpty(rootNamespace))
            {
                AddProperty(xml, propertyGroupNode, "RootNamespace", packageId);
            }

            AddProperty(xml, propertyGroupNode, "OutputPath", tempBin);
            AddProperty(xml, propertyGroupNode, "BaseOutputPath", tempBin);
            AddProperty(xml, propertyGroupNode, "MSBuildProjectExtensionsPath", tempObj);
            AddProperty(xml, propertyGroupNode, "BaseIntermediateOutputPath", tempObj);
            AddProperty(xml, propertyGroupNode, "IntermediateOutputPath", tempObj);

            // The Project's own 'obj' and 'bin' directories are no longer the intermediate/output paths, so exclude them
            // from the default items (ex. obj/Debug/*.AssemblyAttributes.cs would be compiled twice)
            AddProperty(xml, propertyGroupNode, "DefaultItemExcludes", "$(DefaultItemExcludes);obj/**;bin/**");

            projectNode.InsertBefore(propertyGroupNode, projectNode.FirstChild);
        }

        private static void RemoveDebugOutputProperties(XmlDocument xml)
        {
            var nodes = xml.SelectNodes("//PropertyGroup[@Label='trakhound-debug-output-properties']");
            if (nodes != null)
            {
                foreach (XmlNode x in nodes)
                {
                    if (x != null && x.ParentNode != null)
                    {
                        var parentNode = x.ParentNode;
                        parentNode.RemoveChild(x);
                        if (!parentNode.HasChildNodes) parentNode.ParentNode.RemoveChild(parentNode);
                    }
                }
            }
        }



        private static void AddApiProperties(XmlDocument xml, XmlNode projectNode)
        {
            var propertyGroupNode = AddPropertyGroup(xml, projectNode, "trakhound-package-properties");

            AddProperty(xml, propertyGroupNode, "OutputType", "Library");
            AddProperty(xml, propertyGroupNode, "EnableDynamicLoading", "true");
            AddProperty(xml, propertyGroupNode, "GenerateAssemblyInfo", "false");

            projectNode.InsertBefore(propertyGroupNode, projectNode.LastChild);
        }

        private static void AddApiReferences(XmlDocument xml, XmlNode projectNode, TrakHoundReferences trakhoundReferences)
        {
            var itemGroupNode = AddItemGroup(xml, projectNode, "trakhound-package-references");

            AddTrakHoundReference(xml, itemGroupNode, "TrakHound.Common", trakhoundReferences);

            projectNode.InsertBefore(itemGroupNode, projectNode.LastChild);
        }


        private static void AddAppProperties(XmlDocument xml, XmlNode projectNode)
        {
            var propertyGroupNode = AddPropertyGroup(xml, projectNode, "trakhound-package-properties");

            AddProperty(xml, propertyGroupNode, "EnableDynamicLoading", "true");
            AddProperty(xml, propertyGroupNode, "GenerateAssemblyInfo", "false");

            projectNode.InsertBefore(propertyGroupNode, projectNode.LastChild);
        }

        private static void AddAppItems(XmlDocument xml, XmlNode projectNode)
        {
            var itemGroupNode = AddItemGroup(xml, projectNode, "trakhound-package-items");

            AddItem(xml, itemGroupNode, "FrameworkReference", "Microsoft.AspNetCore.App");
            AddItem(xml, itemGroupNode, "SupportedPlatform", "browser");

            projectNode.InsertBefore(itemGroupNode, projectNode.LastChild);
        }

        private static void AddAppReferences(XmlDocument xml, XmlNode projectNode, TrakHoundReferences trakhoundReferences)
        {
            var itemGroupNode = AddItemGroup(xml, projectNode, "trakhound-package-references");

            AddTrakHoundReference(xml, itemGroupNode, "TrakHound.Blazor", trakhoundReferences);

            projectNode.InsertBefore(itemGroupNode, projectNode.LastChild);
        }


        private static void AddFunctionProperties(XmlDocument xml, XmlNode projectNode)
        {
            var propertyGroupNode = AddPropertyGroup(xml, projectNode, "trakhound-package-properties");

            AddProperty(xml, propertyGroupNode, "OutputType", "Library");
            AddProperty(xml, propertyGroupNode, "EnableDynamicLoading", "true");
            AddProperty(xml, propertyGroupNode, "GenerateAssemblyInfo", "false");

            projectNode.InsertBefore(propertyGroupNode, projectNode.LastChild);
        }

        private static void AddFunctionReferences(XmlDocument xml, XmlNode projectNode, TrakHoundReferences trakhoundReferences)
        {
            var itemGroupNode = AddItemGroup(xml, projectNode, "trakhound-package-references");

            AddTrakHoundReference(xml, itemGroupNode, "TrakHound.Common", trakhoundReferences);

            projectNode.InsertBefore(itemGroupNode, projectNode.LastChild);
        }


        private static void AddWorkerProperties(XmlDocument xml, XmlNode projectNode)
        {
            var propertyGroupNode = AddPropertyGroup(xml, projectNode, "trakhound-package-properties");

            AddProperty(xml, propertyGroupNode, "OutputType", "Library");
            AddProperty(xml, propertyGroupNode, "EnableDynamicLoading", "true");
            AddProperty(xml, propertyGroupNode, "GenerateAssemblyInfo", "false");

            projectNode.InsertBefore(propertyGroupNode, projectNode.LastChild);
        }

        private static void AddWorkerReferences(XmlDocument xml, XmlNode projectNode, TrakHoundReferences trakhoundReferences)
        {
            var itemGroupNode = AddItemGroup(xml, projectNode, "trakhound-package-references");

            AddTrakHoundReference(xml, itemGroupNode, "TrakHound.Common", trakhoundReferences);

            projectNode.InsertBefore(itemGroupNode, projectNode.LastChild);
        }


        private static void AddServiceProperties(XmlDocument xml, XmlNode projectNode)
        {
            var propertyGroupNode = AddPropertyGroup(xml, projectNode, "trakhound-package-properties");

            AddProperty(xml, propertyGroupNode, "OutputType", "Library");
            AddProperty(xml, propertyGroupNode, "EnableDynamicLoading", "true");
            AddProperty(xml, propertyGroupNode, "GenerateAssemblyInfo", "false");

            projectNode.InsertBefore(propertyGroupNode, projectNode.LastChild);
        }

        private static void AddServiceReferences(XmlDocument xml, XmlNode projectNode, TrakHoundReferences trakhoundReferences)
        {
            var itemGroupNode = AddItemGroup(xml, projectNode, "trakhound-package-references");

            AddTrakHoundReference(xml, itemGroupNode, "TrakHound.Common", trakhoundReferences);

            projectNode.InsertBefore(itemGroupNode, projectNode.LastChild);
        }


        private static void AddIdentityProperties(XmlDocument xml, XmlNode projectNode)
        {
            var propertyGroupNode = AddPropertyGroup(xml, projectNode, "trakhound-package-properties");

            AddProperty(xml, propertyGroupNode, "OutputType", "Library");
            AddProperty(xml, propertyGroupNode, "EnableDynamicLoading", "true");
            AddProperty(xml, propertyGroupNode, "GenerateAssemblyInfo", "false");

            projectNode.InsertBefore(propertyGroupNode, projectNode.LastChild);
        }

        private static void AddIdentityReferences(XmlDocument xml, XmlNode projectNode, TrakHoundReferences trakhoundReferences)
        {
            var itemGroupNode = AddItemGroup(xml, projectNode, "trakhound-package-references");

            AddTrakHoundReference(xml, itemGroupNode, "TrakHound.Common", trakhoundReferences);

            projectNode.InsertBefore(itemGroupNode, projectNode.LastChild);
        }


        private static XmlNode AddImport(XmlDocument xml, XmlNode projectNode, string project, string sdk)
        {
            var importNode = xml.CreateNode(XmlNodeType.Element, "Import", null);

            var projectAttribute = xml.CreateAttribute("Project");
            projectAttribute.Value = project;
            importNode.Attributes.Append(projectAttribute);

            var sdkAttribute = xml.CreateAttribute("Sdk");
            sdkAttribute.Value = sdk;
            importNode.Attributes.Append(sdkAttribute);

            return importNode;
        }

        private static XmlNode AddPropertyGroup(XmlDocument xml, XmlNode projectNode, string label = null, Dictionary<string, string> attributes = null)
        {
            var groupNode = xml.CreateNode(XmlNodeType.Element, "PropertyGroup", null);

            if (!string.IsNullOrEmpty(label))
            {
                var xmlAttribute = xml.CreateAttribute("Label");
                xmlAttribute.Value = label;
                groupNode.Attributes.Append(xmlAttribute);
            }

            if (!attributes.IsNullOrEmpty())
            {
                foreach (var attribute in attributes)
                {
                    var xmlAttribute = xml.CreateAttribute(attribute.Key);
                    xmlAttribute.Value = attribute.Value;
                    groupNode.Attributes.Append(xmlAttribute);
                }
            }

            return groupNode;
        }

        private static void AddProperty(XmlDocument xml, XmlNode propertyGroupNode, string propertyName, string propertyValue)
        {
            var node = xml.CreateNode(XmlNodeType.Element, propertyName, null);
            node.InnerText = propertyValue;
            propertyGroupNode.AppendChild(node);
        }

        private static string GetProperty(XmlDocument xml, string propertyName)
        {
            var node = xml.SelectSingleNode($"//PropertyGroup/{propertyName}");
            if (node != null)
            {
                return node.InnerText;
            }

            return null;
        }

        private static void RemoveProperty(XmlDocument xml, string propertyName)
        {
            var nodes = xml.SelectNodes($"//PropertyGroup/{propertyName}");
            if (nodes != null)
            {
                foreach (XmlNode x in nodes)
                {
                    if (x != null && x.ParentNode != null)
                    {
                        var parentNode = x.ParentNode;
                        parentNode.RemoveChild(x);
                        if (!parentNode.HasChildNodes) parentNode.ParentNode.RemoveChild(parentNode);
                    }
                }
            }
        }


        private static XmlNode AddItemGroup(XmlDocument xml, XmlNode projectNode, string label = null, Dictionary<string, string> attributes = null)
        {
            var groupNode = xml.CreateNode(XmlNodeType.Element, "ItemGroup", null);

            if (!string.IsNullOrEmpty(label))
            {
                var xmlAttribute = xml.CreateAttribute("Label");
                xmlAttribute.Value = label;
                groupNode.Attributes.Append(xmlAttribute);
            }

            if (!attributes.IsNullOrEmpty())
            {
                foreach (var attribute in attributes)
                {
                    var xmlAttribute = xml.CreateAttribute(attribute.Key);
                    xmlAttribute.Value = attribute.Value;
                    groupNode.Attributes.Append(xmlAttribute);
                }
            }

            return groupNode;
        }

        private static void AddItem(XmlDocument xml, XmlNode itemGroupNode, string itemName, string include)
        {
            var node = xml.CreateNode(XmlNodeType.Element, itemName, null);

            var includeAttribute = xml.CreateAttribute("Include");
            includeAttribute.Value = include;
            node.Attributes.Append(includeAttribute);

            itemGroupNode.AppendChild(node);
        }


        private static void AddPackageReference(XmlDocument xml, XmlNode itemGroupNode, string packageId, string version = "*", bool privateAssets = false)
        {
            var referenceNode = xml.CreateNode(XmlNodeType.Element, "PackageReference", null);

            var includeAttribute = xml.CreateAttribute("Include");
            includeAttribute.Value = packageId;
            referenceNode.Attributes.Append(includeAttribute);

            var versionAttribute = xml.CreateAttribute("Version");
            versionAttribute.Value = !string.IsNullOrEmpty(version) ? version : "*";
            referenceNode.Attributes.Append(versionAttribute);

            var privateNode = xml.CreateNode(XmlNodeType.Element, "Private", null);
            privateNode.InnerText = "false";
            referenceNode.AppendChild(privateNode);

            var excludeAssetsNode = xml.CreateNode(XmlNodeType.Element, "ExcludeAssets", null);
            excludeAssetsNode.InnerText = "runtime";
            referenceNode.AppendChild(excludeAssetsNode);

            itemGroupNode.AppendChild(referenceNode);
        }

        /// <summary>
        /// Add a compile-only reference to a TrakHound library (the Instance provides it at runtime). If the original Project referenced the
        /// TrakHound libraries as Projects (ex. within the TrakHound repository), the same Projects are referenced so that the Package is built
        /// against them. Otherwise a Package Reference to the referenced TrakHound version is added
        /// </summary>
        private static void AddTrakHoundReference(XmlDocument xml, XmlNode itemGroupNode, string packageId, TrakHoundReferences trakhoundReferences)
        {
            if (trakhoundReferences != null && trakhoundReferences.ProjectPaths.ContainsKey(packageId))
            {
                // Reference every TrakHound library Project directly so that none are copied to the output (including transitive ones)
                foreach (var projectPath in trakhoundReferences.ProjectPaths.Values)
                {
                    AddProjectReference(xml, itemGroupNode, projectPath);
                }

                // Exclude the runtime assets of the Packages that the TrakHound libraries reference (as a TrakHound Package Reference
                // with ExcludeAssets=runtime does), unless the Project references the Package itself
                foreach (var dependency in trakhoundReferences.ProjectPackageDependencies)
                {
                    if (xml.SelectSingleNode($"//PackageReference[@Include='{dependency.Key}']") == null)
                    {
                        AddPackageReference(xml, itemGroupNode, dependency.Key, dependency.Value, true);
                    }
                }
            }
            else
            {
                AddPackageReference(xml, itemGroupNode, packageId, trakhoundReferences?.Version, true);
            }
        }

        private static void AddProjectReference(XmlDocument xml, XmlNode itemGroupNode, string projectPath)
        {
            var referenceNode = xml.CreateNode(XmlNodeType.Element, "ProjectReference", null);

            var includeAttribute = xml.CreateAttribute("Include");
            includeAttribute.Value = projectPath;
            referenceNode.Attributes.Append(includeAttribute);

            var privateNode = xml.CreateNode(XmlNodeType.Element, "Private", null);
            privateNode.InnerText = "false";
            referenceNode.AppendChild(privateNode);

            itemGroupNode.AppendChild(referenceNode);
        }

        private static void RemoveReference(XmlDocument xml, string packageName)
        {
            // Remove Package References
            var nodes = xml.SelectNodes($"//PackageReference[@Include='{packageName}']");
            if (nodes != null)
            {
                foreach (XmlNode x in nodes)
                {
                    if (x != null && x.ParentNode != null)
                    {
                        var parentNode = x.ParentNode;
                        parentNode.RemoveChild(x);
                        if (!parentNode.HasChildNodes) parentNode.ParentNode.RemoveChild(parentNode);
                    }
                }
            }

            // Remove Project References
            nodes = xml.SelectNodes($"//ProjectReference[contains(@Include, '\\{packageName}.csproj')]");
            if (nodes != null)
            {
                foreach (XmlNode x in nodes)
                {
                    if (x != null && x.ParentNode != null)
                    {
                        var parentNode = x.ParentNode;
                        parentNode.RemoveChild(x);
                        if (!parentNode.HasChildNodes) parentNode.ParentNode.RemoveChild(parentNode);
                    }
                }
            }
        }


        private static void IgnoreFile(XmlDocument xml, XmlNode projectNode, string filename, string buildType)
        {
            var itemGroupNode = xml.CreateNode(XmlNodeType.Element, "ItemGroup", null);

            var itemGroupLabelAttribute = xml.CreateAttribute("Label");
            itemGroupLabelAttribute.Value = "trakhound-package-ignore-files";
            itemGroupNode.Attributes.Append(itemGroupLabelAttribute);


            var compileNode = xml.CreateNode(XmlNodeType.Element, buildType, null);

            var removeAttribute = xml.CreateAttribute("Remove");
            removeAttribute.Value = filename;
            compileNode.Attributes.Append(removeAttribute);

            itemGroupNode.AppendChild(compileNode);


            var noneNode = xml.CreateNode(XmlNodeType.Element, "None", null);

            var includeAttribute = xml.CreateAttribute("Include");
            includeAttribute.Value = filename;
            noneNode.Attributes.Append(includeAttribute);

            itemGroupNode.AppendChild(noneNode);


            projectNode.InsertBefore(itemGroupNode, projectNode.LastChild);
        }


        private static bool RunCommand(string workingDirectory, string executable, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo(executable, arguments);
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                if (!string.IsNullOrEmpty(workingDirectory)) startInfo.WorkingDirectory = workingDirectory;

                // Read stdout and stderr asynchronously (reading one to the end first can deadlock if the other fills its buffer)
                var outputBuilder = new StringBuilder();
                DataReceivedEventHandler onData = (s, e) =>
                {
                    if (e.Data != null) lock (outputBuilder) outputBuilder.AppendLine(e.Data);
                };

                using (var process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.OutputDataReceived += onData;
                    process.ErrorDataReceived += onData;

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        Console.Error.WriteLine($"Command Failed (Exit Code {process.ExitCode}) : {executable} {arguments}");
                        lock (outputBuilder) Console.Error.WriteLine(outputBuilder.ToString());
                    }

                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("TrakHoundDotNetPackage.RunCommand() : Command = " + executable + " " + arguments + " : WorkingDirectory = " + workingDirectory + " : " + ex.Message);
            }

            return false;
        }


        class ProjectAssetsFile
        {
            [JsonPropertyName("targets")]
            public Dictionary<string, Dictionary<string, ProjectAssetsFileTarget>> Targets { get; set; }

            [JsonPropertyName("libraries")]
            public Dictionary<string, ProjectAssetsFileLibrary> Libraries { get; set; }
        }

        class ProjectAssetsFileTarget
        {
            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("dependencies")]
            public Dictionary<string, string> Dependencies { get; set; }
        }

        class ProjectAssetsFileLibrary
        {
            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("path")]
            public string Path { get; set; }

            [JsonPropertyName("sha512")]
            public string Sha512 { get; set; }
        }

        /// <summary>
        /// The TrakHound libraries that a Project references (read from its project.assets.json)
        /// </summary>
        class TrakHoundReferences
        {
            /// <summary>
            /// The TrakHound.Common version (from either a Package or a Project Reference)
            /// </summary>
            public string Version { get; set; }

            /// <summary>
            /// The TrakHound libraries that are referenced as Projects (Package ID => full path of the Project file)
            /// </summary>
            public Dictionary<string, string> ProjectPaths { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// The Packages that the TrakHound library Projects reference (Package ID => Version). The Instance provides them at runtime
            /// </summary>
            public Dictionary<string, string> ProjectPackageDependencies { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // The TrakHound libraries that are provided by the Instance (and only referenced to compile a Package)
        private static readonly HashSet<string> _trakhoundLibraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TrakHound.Common",
            "TrakHound.Blazor",
            "TrakHound.Clients",
            "TrakHound.Drivers"
        };


        /// <summary>
        /// Get the TrakHound libraries that a Project references (read from its 'obj/project.assets.json'). Returns null if the Project has not been restored
        /// </summary>
        private static TrakHoundReferences GetTrakHoundReferences(string projectDirectory)
        {
            if (!string.IsNullOrEmpty(projectDirectory))
            {
                return ReadTrakHoundReferences(Path.Combine(projectDirectory, "obj", "project.assets.json"), projectDirectory);
            }

            return null;
        }

        /// <summary>
        /// Get the TrakHound.Common version that was restored for a converted Project (read from the project.assets.json
        /// in the temporary directory set by <see cref="AddOutputProperties"/>). Returns null if it can't be read
        /// </summary>
        private static string GetResolvedTrakHoundVersion(string convertedProjectFilePath)
        {
            var assetsPath = GetConvertedAssetsPath(convertedProjectFilePath);

            return assetsPath != null ? ReadTrakHoundReferences(assetsPath, Path.GetDirectoryName(convertedProjectFilePath))?.Version : null;
        }

        /// <summary>
        /// Get the temporary directory set by <see cref="AddOutputProperties"/> for a converted Project
        /// </summary>
        private static string GetTempDirectory(string convertedProjectFilePath)
        {
            return Path.GetDirectoryName(GetConvertedAssetsPath(convertedProjectFilePath)?.Replace("project.assets.json", "").TrimEnd(Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Get the path of a converted Project's project.assets.json (in the temporary directory set by <see cref="AddOutputProperties"/>). Returns null if it can't be read
        /// </summary>
        private static string GetConvertedAssetsPath(string convertedProjectFilePath)
        {
            if (!string.IsNullOrEmpty(convertedProjectFilePath) && File.Exists(convertedProjectFilePath))
            {
                try
                {
                    var xml = new XmlDocument();
                    xml.Load(convertedProjectFilePath);

                    var node = xml.SelectSingleNode("//PropertyGroup[@Label='trakhound-package-output-properties']/MSBuildProjectExtensionsPath");
                    if (node != null && !string.IsNullOrEmpty(node.InnerText))
                    {
                        return Path.Combine(node.InnerText, "project.assets.json");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            return null;
        }

        /// <param name="assetsPath">Path to the project.assets.json file</param>
        /// <param name="projectDirectory">Directory of the Project (Project Reference paths are relative to it)</param>
        private static TrakHoundReferences ReadTrakHoundReferences(string assetsPath, string projectDirectory)
        {
            if (!string.IsNullOrEmpty(assetsPath) && File.Exists(assetsPath))
            {
                try
                {
                    var assets = Json.Read<ProjectAssetsFile>(assetsPath);
                    if (assets?.Libraries != null)
                    {
                        var references = new TrakHoundReferences();

                        // Libraries are listed as "{Package ID}/{Version}" and include transitive references
                        foreach (var library in assets.Libraries)
                        {
                            var keyParts = library.Key.Split('/');
                            if (keyParts.Length < 2 || library.Value == null) continue;

                            var packageName = keyParts[0];
                            var packageVersion = keyParts[1];

                            if (packageName == "TrakHound.Common") references.Version = packageVersion;

                            if (library.Value.Type == "project" && _trakhoundLibraries.Contains(packageName) && !string.IsNullOrEmpty(library.Value.Path))
                            {
                                references.ProjectPaths[packageName] = Path.GetFullPath(Path.Combine(projectDirectory, library.Value.Path));
                            }
                        }

                        // Packages referenced by the TrakHound library Projects (ex. YamlDotNet)
                        if (assets.Targets != null && references.ProjectPaths.Count > 0)
                        {
                            foreach (var target in assets.Targets.Values)
                            {
                                foreach (var item in target)
                                {
                                    var packageName = item.Key.Split('/')[0];
                                    if (item.Value?.Type != "project" || !references.ProjectPaths.ContainsKey(packageName) || item.Value.Dependencies == null) continue;

                                    foreach (var dependency in item.Value.Dependencies)
                                    {
                                        if (!_trakhoundLibraries.Contains(dependency.Key))
                                        {
                                            references.ProjectPackageDependencies[dependency.Key] = dependency.Value;
                                        }
                                    }
                                }
                            }
                        }

                        return references;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            return null;
        }
    }
}
