// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using TrakHound.Configurations;
using TrakHound.Packages;

namespace TrakHound.CLI.Packages
{
    internal static partial class Packages
    {
        private const string _staticAssetEndpointsExtension = ".staticwebassets.endpoints.json";

        private static readonly Regex _commitPattern = new Regex("^[0-9a-fA-F]{7,64}$");
        private static readonly Regex _lastModifiedHeaderPattern = new Regex("\\{\"Name\":\"Last-Modified\",\"Value\":\"[^\"]*\"\\},?");


        [Command("rebuild", "Build a Package's recorded commit again and compare the files, to check that the Package was built from that source")]
        [CommandHelp(null, "th packages rebuild ./MyPackage-1.0.0.thp --repository ../my-repository")]
        public static bool Rebuild(
            [CommandParameter($"The path to the Package file ({TrakHoundPackage.FileExtension}) to check")]
            string file,
            [CommandOption("The repository to clone (a URL or local path). If not specified, the repository recorded in the Package is used", "-r")]
            string repository = null,
            [CommandOption("The directory of the Project within the repository. If not specified, the directory recorded in the Package is used")]
            string project = null,
            [CommandOption("Build the source even if the Package isn't signed by a Trusted Publisher. Building runs code from the repository, so only use this if you trust it")]
            bool allowUntrusted = false,
            [CommandOption("The root directory that holds the Configuration Profile", "-p")]
            string path = null,
            [CommandOption("The Configuration Profile whose Trusted Publishers are used")]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            var filePath = CliPath.Resolve(file);
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"Error : Package File not Found : ({filePath})");
                return false;
            }

            var packageBytes = File.ReadAllBytes(filePath);
            var verification = TrakHoundPackageVerifier.VerifyArchive(packageBytes, TrakHoundTrustedPublishers.Read(Trust.Trust.GetProfilePath(path, profile)));
            var manifest = verification.Manifest;

            if (verification.Status != TrakHoundPackageVerificationStatus.Valid)
            {
                Console.Error.WriteLine($"Error : The Package doesn't match its Manifest, so there's nothing to compare with : {TrakHoundPackageVerificationPolicy.Describe(verification)}");
                return false;
            }

            // The Package names the repository, and a build runs that repository's code
            if (verification.SignatureStatus != TrakHoundPackageSignatureStatus.Trusted && !allowUntrusted)
            {
                Console.Error.WriteLine($"Error : {TrakHoundPackageVerificationPolicy.Describe(verification)}. Building runs code from the repository, so add the publisher (th trust add) or use --allow-untrusted if you trust the source");
                return false;
            }

            if (string.IsNullOrEmpty(manifest.BuildConfiguration))
            {
                Console.Error.WriteLine("Error : The Package wasn't built from a DotNet project (th packages dotnet), so it can't be rebuilt");
                return false;
            }

            var repositorySource = !string.IsNullOrEmpty(repository) ? repository : manifest.Repository;
            var projectDirectory = !string.IsNullOrEmpty(project) ? project : manifest.RepositoryDirectory;

            if (string.IsNullOrEmpty(repositorySource) || string.IsNullOrEmpty(manifest.RepositoryCommit) || string.IsNullOrEmpty(projectDirectory))
            {
                Console.Error.WriteLine($"Error : The Package doesn't record its source (repository '{repositorySource}', directory '{projectDirectory}', commit '{manifest.RepositoryCommit}'). Use --repository and --project for those it's missing");
                return false;
            }

            if (!_commitPattern.IsMatch(manifest.RepositoryCommit))
            {
                Console.Error.WriteLine($"Error : Invalid commit : {manifest.RepositoryCommit}");
                return false;
            }

            if (manifest.RepositoryDirty == true) Console.WriteLine("Warning : The Package was built with uncommitted changes, so it may not match the commit");

            var workDirectory = TrakHoundTemp.CreateDirectory();
            try
            {
                var cloneDirectory = Path.Combine(workDirectory, "source");

                Console.WriteLine($"Cloning ({repositorySource}) at {manifest.RepositoryCommit}...");

                if (!RunGit(null, "clone", "--no-checkout", "--", GetRepositoryArgument(repositorySource), cloneDirectory)) return false;
                if (!RunGit(cloneDirectory, "checkout", "--detach", manifest.RepositoryCommit)) return false;

                // The directory comes from the Package, so it can't be allowed to point outside of the clone
                var projectPath = Path.GetFullPath(Path.Combine(cloneDirectory, projectDirectory));
                if (!projectPath.StartsWith(Path.GetFullPath(cloneDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(projectPath))
                {
                    Console.Error.WriteLine($"Error : Project directory not Found in the repository : ({projectDirectory})");
                    return false;
                }

                var settings = new TrakHoundDotNetPackageSettings();
                settings.DotnetSdkLocation = TrakHoundCliConfiguration.Instance.DotnetSdkLocation;
                settings.Version = manifest.Version;
                settings.Configuration = manifest.BuildConfiguration;
                settings.IncludeDebugSymbols = manifest.Files.Any(o => o.Path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
                settings.IncludeSource = manifest.Files.Any(o => o.Path == GetSourceFilename());
                (settings.GitBranch, settings.GitCommit, settings.GitDirty) = GitCommands.GetBranchAndCommit(projectPath);

                // Reads the TrakHound version from the restore, as the original build did
                if (!TrakHoundDotNetPackage.Restore(Path.Combine(projectPath, $"{manifest.Id}.csproj"), settings))
                {
                    Console.Error.WriteLine("Error : Restore Failed");
                    return false;
                }

                var rebuiltBytes = TrakHoundDotNetPackage.Create(projectPath, settings);
                if (rebuiltBytes.IsNullOrEmpty())
                {
                    Console.Error.WriteLine("Error : The Package couldn't be rebuilt");
                    return false;
                }

                var rebuilt = TrakHoundPackageVerifier.VerifyArchive(rebuiltBytes).Manifest;

                Console.WriteLine("-----");

                if (rebuilt.DotnetSdkVersion != manifest.DotnetSdkVersion)
                {
                    Console.WriteLine($"Warning : Rebuilt with DotNet SDK {rebuilt.DotnetSdkVersion}, but the Package was built with {manifest.DotnetSdkVersion}. A different SDK can produce different files");
                }

                var differences = CompareFiles(packageBytes, manifest, rebuiltBytes, rebuilt);
                if (differences.Count > 0)
                {
                    Console.Error.WriteLine($"Different : {Path.GetFileName(filePath)} doesn't match commit {manifest.RepositoryCommit}");
                    foreach (var difference in differences) Console.Error.WriteLine($"  {difference}");

                    if (string.Equals(manifest.Category, "app", StringComparison.OrdinalIgnoreCase) && settings.IncludeDebugSymbols)
                    {
                        Console.Error.WriteLine("An App built with Debug Symbols only rebuilds to the same files from the same directory, so this doesn't show that the source differs. Build it with --include-debug-symbols false to check it");
                    }

                    return false;
                }

                Console.WriteLine($"Match : All {manifest.Files.Count} files in {Path.GetFileName(filePath)} match a rebuild of commit {manifest.RepositoryCommit}");
                return true;
            }
            finally
            {
                DeleteDirectory(workDirectory);
            }
        }


        private static string GetSourceFilename() => $"{TrakHoundPackage.SourceDirectory}.zip";

        /// <summary>
        /// A local repository is cloned from its full path, since git runs from a different directory
        /// </summary>
        private static string GetRepositoryArgument(string repository)
        {
            return Uri.TryCreate(repository, UriKind.Absolute, out var uri) && !uri.IsFile ? repository : CliPath.Resolve(repository);
        }

        private static List<string> CompareFiles(byte[] packageBytes, TrakHoundPackageManifest manifest, byte[] rebuiltBytes, TrakHoundPackageManifest rebuilt)
        {
            var differences = new List<string>();
            var rebuiltFiles = rebuilt.Files.ToDictionary(o => o.Path, StringComparer.OrdinalIgnoreCase);

            foreach (var file in manifest.Files)
            {
                if (!rebuiltFiles.TryGetValue(file.Path, out var rebuiltFile))
                {
                    differences.Add($"Not in the rebuild : {file.Path}");
                }
                else if (file.Path == GetSourceFilename())
                {
                    // A zip's timestamps differ on every build, so its contents are compared instead
                    differences.AddRange(CompareSourceZip(packageBytes, rebuiltBytes));
                }
                else if (file.Path.EndsWith(_staticAssetEndpointsExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (NormalizeStaticAssetEndpoints(packageBytes, file.Path) != NormalizeStaticAssetEndpoints(rebuiltBytes, file.Path)) differences.Add($"Different : {file.Path}");
                }
                else if (!string.Equals(file.Sha256, rebuiltFile.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    differences.Add($"Different : {file.Path}");
                }

                rebuiltFiles.Remove(file.Path);
            }

            differences.AddRange(rebuiltFiles.Keys.OrderBy(o => o, StringComparer.Ordinal).Select(o => $"Only in the rebuild : {o}"));

            return differences;
        }

        /// <summary>
        /// Read an App's static asset endpoints without their Last-Modified headers, which only record when the files were checked out
        /// </summary>
        private static string NormalizeStaticAssetEndpoints(byte[] packageBytes, string path)
        {
            using (var package = new ZipArchive(new MemoryStream(packageBytes), ZipArchiveMode.Read))
            using (var reader = new StreamReader(package.Entries.First(o => o.FullName.Replace('\\', '/') == path).Open()))
            {
                return _lastModifiedHeaderPattern.Replace(reader.ReadToEnd(), "");
            }
        }

        private static IEnumerable<string> CompareSourceZip(byte[] packageBytes, byte[] rebuiltBytes)
        {
            var original = ReadSourceZipHashes(packageBytes);
            var rebuilt = ReadSourceZipHashes(rebuiltBytes);

            foreach (var name in original.Keys.Union(rebuilt.Keys).OrderBy(o => o, StringComparer.Ordinal))
            {
                if (!rebuilt.ContainsKey(name)) yield return $"Not in the rebuild : {GetSourceFilename()} : {name}";
                else if (!original.ContainsKey(name)) yield return $"Only in the rebuild : {GetSourceFilename()} : {name}";
                else if (original[name] != rebuilt[name]) yield return $"Different : {GetSourceFilename()} : {name}";
            }
        }

        private static Dictionary<string, string> ReadSourceZipHashes(byte[] packageBytes)
        {
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (var package = new ZipArchive(new MemoryStream(packageBytes), ZipArchiveMode.Read))
            using (var source = new ZipArchive(package.GetEntry(GetSourceFilename()).Open(), ZipArchiveMode.Read))
            {
                foreach (var entry in source.Entries.Where(o => !string.IsNullOrEmpty(o.Name)))
                {
                    using (var stream = entry.Open())
                    {
                        hashes[entry.FullName.Replace('\\', '/')] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
                    }
                }
            }

            return hashes;
        }

        private static bool RunGit(string workingDirectory, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo("git");
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            if (workingDirectory != null) startInfo.WorkingDirectory = workingDirectory;

            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            // The repository can come from the Package, so limit how it can be reached and don't wait on a credentials prompt
            startInfo.Environment["GIT_ALLOW_PROTOCOL"] = "https:ssh:file";
            startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    process.OutputDataReceived += (s, e) => { };
                    process.BeginOutputReadLine();

                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0) return true;

                    Console.Error.WriteLine($"Error : git {arguments[0]} failed : {error.Trim()}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error : Unable to run git : {ex.Message}");
                return false;
            }
        }

        private static void DeleteDirectory(string path)
        {
            try
            {
                // Git makes its object files read only, which stops them being deleted on Windows
                foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);

                Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning : Unable to remove temporary files ({path}) : {ex.Message}");
            }
        }
    }
}
