// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Extensions;
using TrakHound.Packages;

namespace TrakHound.CLI.Packages
{
    [CommandGroup("packages", "Create, Publish and Install TrakHound Packages")]
    internal static partial class Packages
    {
        [Command("list", "List the Installed Packages")]
        [CommandHelp(null, "th packages list --category Api")]
        public static bool List(
            [CommandParameter($"The root directory (Packages are installed in its '{TrakHoundPackage.PackageDirectory}' subdirectory). If not specified, the current working directory is used")]
            string path = null,
            [CommandOption("The Category of the Packages to list", "-c")]
            string category = null
            )
        {
            var packagesPath = GetPackagesPath(CliPath.Resolve(path));

            var packages = TrakHoundPackage.GetInstalled(packagesPath);

            if (!string.IsNullOrEmpty(category))
            {
                packages = packages?.Where(x => string.Equals(x.Category, category, StringComparison.OrdinalIgnoreCase));
            }

            if (!packages.IsNullOrEmpty())
            {
                foreach (var package in packages)
                {
                    Console.WriteLine($"{package.Category} : {package.Id} : {package.Version}");
                }
            }
            else
            {
                Console.WriteLine($"No Packages Found : {packagesPath}" + (!string.IsNullOrEmpty(category) ? $" : category = {category}" : null));
            }

            return true;
        }


        [Command("install", "Download and Install the specified Package (and its Dependencies), or Install a local Package file (.thp)")]
        [CommandHelp(null, "th packages install TrakHound.Entities.Api --version 1.0.0  |  th packages install --file ./MyPackage-1.0.0.thp")]
        public static async Task<bool> Install(
            [CommandParameter($"The ID of the Package to Install (or the path to a {TrakHoundPackage.FileExtension} file). If not specified, the {TrakHoundPackagesFile.Filename} file is processed")]
            string packageId = null,
            [CommandOption($"The path to a local Package file ({TrakHoundPackage.FileExtension}) to Install instead of downloading the Package. Its Dependencies are downloaded unless --no-dependencies is set", "-f")]
            string file = null,
            [CommandOption("Do not Install the Package's Dependencies")]
            bool noDependencies = false,
            [CommandOption("The Version of the Package to Install. If not specified, the latest package is installed", "-v")]
            string version = null,
            [CommandOption($"The root directory to install the package to (Packages are installed in its '{TrakHoundPackage.PackageDirectory}' subdirectory). If not specified, the current working directory is used", "-p")]
            string path = null,
            [CommandOption("The URL of the TrakHound Package Server to download the package from", "-s")]
            string server = null,
            [CommandOption("The Organization that the package is stored under")]
            string organization = null,
            [CommandOption("Add a default Configuration (as installing from the Admin interface does) for the Package and each of its Dependencies that doesn't already have one")]
            bool configure = false,
            [CommandOption("The Configuration Profile to add the Configurations to (with --configure)")]
            string profile = TrakHoundConfigurationProfile.Default,
            [CommandOption("The Group to put the added Configurations in (with --configure). The Group is created if it doesn't exist")]
            string group = null
            )
        {
            if (!string.IsNullOrWhiteSpace(group) && !configure) throw new CommandException("--group is only used with --configure");

            var rootPath = CliPath.Resolve(path);
            var packagesPath = GetPackagesPath(rootPath);
            var installPackages = new List<TrakHoundPackageDependency>();
            var installed = new List<TrakHoundPackage>();
            var allowPackage = GetInstallCheck(path, profile);

            // A Package ID that is the path to an existing Package file is treated as --file
            if (string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(packageId)
                && packageId.EndsWith(TrakHoundPackage.FileExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(CliPath.Resolve(packageId)))
            {
                file = packageId;
                packageId = null;
            }

            if (!string.IsNullOrEmpty(file))
            {
                if (!string.IsNullOrEmpty(packageId) || !string.IsNullOrEmpty(version))
                {
                    throw new CommandException("--file cannot be used with a Package ID or --version (the Package file contains its ID and Version)");
                }

                var fileSuccess = await InstallFile(CliPath.Resolve(file), rootPath, packagesPath, !noDependencies, organization, server, installed, allowPackage);
                if (configure && !AddDefaultConfigurations(rootPath, profile, installed, group)) fileSuccess = false;
                return fileSuccess;
            }

            // Read TrakHound Packages File
            var packagesFile = ReadPackagesFile(rootPath);

            if (!string.IsNullOrEmpty(packageId))
            {
                installPackages.Add(new TrakHoundPackageDependency(packageId, version));
            }
            else if (packagesFile != null && !packagesFile.Packages.IsNullOrEmpty())
            {
                foreach (var filePackage in packagesFile.Packages)
                {
                    installPackages.Add(new TrakHoundPackageDependency(filePackage.Id, filePackage.Version));
                }
            }

            if (installPackages.Count == 0)
            {
                Console.WriteLine($"No Packages to Install : No Package ID specified and no Packages found in ({Path.Combine(packagesPath, TrakHoundPackagesFile.Filename)})");
                return true;
            }

            Console.WriteLine($"Installing Packages to ({packagesPath})");

            var success = true;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var installPackage in installPackages)
            {
                if (!await InstallPackage(installPackage, organization, server, packagesPath, visited, !noDependencies, installed, allowPackage)) success = false;
            }

            UpdatePackagesFile(rootPath, packagesPath);

            if (configure && !AddDefaultConfigurations(rootPath, profile, installed, group)) success = false;

            return success;
        }

        /// <summary>
        /// Install a local Package file (.thp), downloading its Dependencies from the Package Server (if includeDependencies is set)
        /// </summary>
        private static async Task<bool> InstallFile(string filePath, string rootPath, string packagesPath, bool includeDependencies, string organization, string server, List<TrakHoundPackage> installed, Func<byte[], bool> allowPackage)
        {
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"Error : Package File not Found : ({filePath})");
                return false;
            }

            Console.Write($"Reading Package File ({filePath})...");

            var packageBytes = await File.ReadAllBytesAsync(filePath);
            var information = TrakHoundPackage.ReadInformationFromPackage(packageBytes);
            if (information == null || string.IsNullOrEmpty(information.Id) || string.IsNullOrEmpty(information.Version))
            {
                Console.WriteLine("ERROR");
                Console.Error.WriteLine($"Error : Unable to read the Package Information. The file may not be a valid Package ({TrakHoundPackage.FileExtension})");
                return false;
            }

            Console.WriteLine("DONE");
            PrintPackage(information);

            // Checked first so that a refused Package doesn't bring in its Dependencies
            if (!allowPackage(packageBytes)) return false;

            Console.WriteLine($"Installing Packages to ({packagesPath})");

            var success = true;

            if (includeDependencies && !information.Dependencies.IsNullOrEmpty())
            {
                Console.WriteLine("Reading Package Dependencies...");

                // The Package itself is not downloaded if a Dependency refers back to it
                var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { $"{information.Id}@{information.Version}" };
                foreach (var dependency in information.Dependencies)
                {
                    if (!await InstallPackage(dependency, organization, server, packagesPath, visited, true, installed, allowPackage)) success = false;
                }
            }
            else if (!information.Dependencies.IsNullOrEmpty())
            {
                Console.WriteLine($"Skipping {information.Dependencies.Count()} Package Dependencies (--no-dependencies)");
            }

            if (InstallPackageBytes(information, packageBytes, packagesPath)) installed.Add(information);
            else success = false;

            UpdatePackagesFile(rootPath, packagesPath);

            return success;
        }

        /// <summary>
        /// Install a Package's contents, unless the same build (Hash) is already installed
        /// </summary>
        private static bool InstallPackageBytes(TrakHoundPackage package, byte[] packageBytes, string packagesPath)
        {
            var installedPackageInformation = TrakHoundPackage.ReadInformation(package.Category, package.Id, package.Version, packagesPath);
            if (TrakHoundPackage.IsSameBuild(installedPackageInformation, package))
            {
                Console.WriteLine($"Package Already Installed : {package.Id} => {package.Version}");
                return true;
            }

            Console.Write($"Installing Package ({package.Id})...");

            if (TrakHoundPackage.Install(packageBytes, packagesPath))
            {
                Console.WriteLine("DONE");
                Console.WriteLine("Package Installed : " + package.Id + " => " + package.Version + " : " + package.Category);
                return true;
            }

            Console.WriteLine("ERROR");
            Console.Error.WriteLine("Error Installing Package. Please try again");
            return false;
        }

        /// <summary>
        /// Add a default Configuration for each installed Package that doesn't have one (--configure)
        /// </summary>
        private static bool AddDefaultConfigurations(string rootPath, string profile, List<TrakHoundPackage> installed, string group)
        {
            Console.WriteLine("-----");
            return CLI.Config.Config.AddDefaultConfigurations(rootPath, profile, installed, group);
        }

        /// <summary>
        /// Write the installed Packages to the Packages File, keeping its Auto Updates and, for each Version it already listed, the requested
        /// Version and Origin (the CLI doesn't record Origins, so one recorded by an Instance is kept)
        /// </summary>
        internal static void UpdatePackagesFile(string rootPath, string packagesPath)
        {
            Console.Write("Updating Packages File...");

            var installedPackages = TrakHoundPackage.GetInstalled(packagesPath);
            if (!installedPackages.IsNullOrEmpty())
            {
                var fileItems = new List<TrakHoundPackagesFileItem>();
                foreach (var installedPackage in installedPackages)
                {
                    fileItems.Add(new TrakHoundPackagesFileItem(installedPackage.Category, installedPackage.Id, null, installedPackage.Version));
                }

                // A Packages File only in the legacy location (the root) is moved to the packages folder
                var packagesFilePath = Path.Combine(packagesPath, TrakHoundPackagesFile.Filename);
                var legacy = !File.Exists(packagesFilePath) ? TrakHoundPackagesFile.Read(rootPath) : null;
                if (legacy != null) TrakHoundPackagesFile.Write(legacy.Packages ?? Enumerable.Empty<TrakHoundPackagesFileItem>(), legacy.AutoUpdates, packagesPath);

                TrakHoundPackagesFile.Update(fileItems, packagesPath, keepOrigins: true);
            }

            Console.WriteLine("DONE");
        }

        private static void PrintPackage(TrakHoundPackage package)
        {
            Console.WriteLine();
            Console.WriteLine(string.Format("{0,-12} : {1}", "ID", package.Id));
            Console.WriteLine(string.Format("{0,-12} : {1}", "Version", package.Version));
            Console.WriteLine(string.Format("{0,-12} : {1}", "Category", package.Category));
            Console.WriteLine(string.Format("{0,-12} : {1}", "BuildDate", package.BuildDate));

            var packageDescription = package.GetMetadata(TrakHoundPackage.DescriptionName);
            if (!string.IsNullOrEmpty(packageDescription))
            {
                Console.WriteLine(string.Format("{0,-12} : {1}", "Description", packageDescription));
            }
            Console.WriteLine();
        }


        [Command("uninstall", "Uninstall the specified Package")]
        public static bool Uninstall(
            [CommandParameter("The ID of the Package to Uninstall")]
            string packageId,
            [CommandOption("The Version of the Package to Uninstall. If not specified, all versions are uninstalled", "-v")]
            string version = null,
            [CommandOption($"The root directory where the Package is installed (Packages are installed in its '{TrakHoundPackage.PackageDirectory}' subdirectory). If not specified, the current working directory is used", "-p")]
            string path = null
            )
        {
            var packagesPath = GetPackagesPath(CliPath.Resolve(path));

            Console.Write($"Uninstalling Package ({packageId})...");

            if (TrakHoundPackage.Uninstall(packageId, version, packagesPath))
            {
                Console.WriteLine("DONE");
                Console.WriteLine("Package Uninstalled : " + packageId + (!string.IsNullOrEmpty(version) ? " => " + version : null));
                return true;
            }
            else
            {
                Console.WriteLine("ERROR");
                Console.Error.WriteLine("Error Uninstalling Package. Please try again");
                return false;
            }
        }


        [Command("verify", "Check that a Package's files match its Manifest, and who signed it. Exits with an error if a Package has changed or has no Manifest")]
        [CommandHelp(null, "th packages verify  |  th packages verify TrakHound.Entities.Api --version 1.0.0  |  th packages verify --file ./MyPackage-1.0.0.thp")]
        public static bool Verify(
            [CommandParameter($"The ID of the installed Package to verify (or the path to a {TrakHoundPackage.FileExtension} file). If not specified, every installed Package is verified")]
            string packageId = null,
            [CommandOption($"The path to a local Package file ({TrakHoundPackage.FileExtension}) to verify", "-f")]
            string file = null,
            [CommandOption("The Version of the installed Package to verify. If not specified, every installed version is verified", "-v")]
            string version = null,
            [CommandOption($"The root directory that Packages are installed in (in its '{TrakHoundPackage.PackageDirectory}' subdirectory) and that holds the Configuration Profile. If not specified, the current working directory is used", "-p")]
            string path = null,
            [CommandOption("The Configuration Profile whose Trusted Publishers are used")]
            string profile = TrakHoundConfigurationProfile.Default,
            [CommandOption("Also exit with an error if a Package isn't signed by a Trusted Publisher")]
            bool requireSignature = false
            )
        {
            var trustedPublishers = TrakHoundTrustedPublishers.Read(Trust.Trust.GetProfilePath(path, profile));

            bool IsVerified(TrakHoundPackageVerificationResult result)
            {
                if (result.Status != TrakHoundPackageVerificationStatus.Valid) return false;
                if (!requireSignature || result.SignatureStatus == TrakHoundPackageSignatureStatus.Trusted) return true;

                Console.Error.WriteLine("  Error : Not signed by a Trusted Publisher (--require-signature)");
                return false;
            }

            // A Package ID that is the path to an existing Package file is treated as --file
            if (string.IsNullOrEmpty(file) && !string.IsNullOrEmpty(packageId)
                && packageId.EndsWith(TrakHoundPackage.FileExtension, StringComparison.OrdinalIgnoreCase) && File.Exists(CliPath.Resolve(packageId)))
            {
                file = packageId;
                packageId = null;
            }

            if (!string.IsNullOrEmpty(file))
            {
                if (!string.IsNullOrEmpty(packageId) || !string.IsNullOrEmpty(version))
                {
                    throw new CommandException("--file cannot be used with a Package ID or --version (the Package file contains its ID and Version)");
                }

                var filePath = CliPath.Resolve(file);
                if (!File.Exists(filePath))
                {
                    Console.Error.WriteLine($"Error : Package File not Found : ({filePath})");
                    return false;
                }

                var fileResult = TrakHoundPackageVerifier.VerifyArchive(File.ReadAllBytes(filePath), trustedPublishers);
                PrintVerificationResult(filePath, fileResult);
                return IsVerified(fileResult);
            }

            var packagesPath = GetPackagesPath(CliPath.Resolve(path));

            var packages = TrakHoundPackage.GetInstalled(packagesPath);
            if (!string.IsNullOrEmpty(packageId)) packages = packages?.Where(x => string.Equals(x.Id, packageId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(version)) packages = packages?.Where(x => string.Equals(x.Version, version, StringComparison.OrdinalIgnoreCase));

            if (packages.IsNullOrEmpty())
            {
                Console.Error.WriteLine($"Error : No Packages Found : {packagesPath}" + (!string.IsNullOrEmpty(packageId) ? $" : {packageId}" : null));
                return false;
            }

            var success = true;
            foreach (var package in packages)
            {
                var result = TrakHoundPackageVerifier.VerifyDirectory(package.Location, trustedPublishers);
                PrintVerificationResult(package.ToString(), result);
                if (!IsVerified(result)) success = false;
            }

            return success;
        }

        private static void PrintVerificationResult(string name, TrakHoundPackageVerificationResult result)
        {
            switch (result.Status)
            {
                case TrakHoundPackageVerificationStatus.Valid:
                    var commit = result.Manifest.RepositoryCommit;
                    var source = !string.IsNullOrEmpty(commit) ? $" : commit {commit}" + (result.Manifest.RepositoryDirty == true ? " (with uncommitted changes)" : null) : " : no commit recorded";
                    var signature = result.SignatureStatus switch
                    {
                        TrakHoundPackageSignatureStatus.Trusted => $"signed by {result.Publisher.Name} ({result.Publisher.KeyId})",
                        TrakHoundPackageSignatureStatus.Untrusted => $"signed by an untrusted key ({result.SignatureKeyId})",
                        _ => "unsigned"
                    };
                    Console.WriteLine($"Valid : {name} : {signature}{source}");
                    break;

                case TrakHoundPackageVerificationStatus.NoManifest:
                    Console.Error.WriteLine($"No Manifest : {name}");
                    break;

                default:
                    Console.Error.WriteLine($"Invalid : {name}");
                    foreach (var error in result.Errors) Console.Error.WriteLine($"  Error : {error}");
                    foreach (var changed in result.Changed) Console.Error.WriteLine($"  Changed : {changed}");
                    foreach (var missing in result.Missing) Console.Error.WriteLine($"  Missing : {missing}");
                    foreach (var added in result.Added) Console.Error.WriteLine($"  Added : {added}");
                    break;
            }
        }


        [Command("pack", "Create a new Package file (.thp) from the specified directory")]
        [CommandHelp(null, "th packages pack ./MyPackage --output ./dist")]
        public static bool Pack(
            [CommandParameter("The path to the directory to create the Package from. If not specified, the current working directory is used")]
            string path = null,
            [CommandOption($"The path to the Package configuration file. If not specified, the {TrakHoundPackage.PackageConfigurationFilename} file in the Package directory is used")]
            string configuration = null,
            [CommandOption("The Category of the Package")]
            string category = null,
            [CommandOption("The ID of the Package")]
            string id = null,
            [CommandOption(VersionDescription, "-v")]
            string version = null,
            [CommandOption(VersionSuffixDescription)]
            string versionSuffix = null,
            [CommandOption("The Description of the Package")]
            string description = null,
            [CommandOption("The Publisher of the Package")]
            string publisher = null,
            [CommandOption("The path to the directory to write the Package file to. If not specified, the Package directory is used", "-o")]
            string output = null,
            [CommandOption($"The path to the private key to sign the Package with. If not specified, the {SigningKeyVariable} environment variable is used if set")]
            string signKey = null,
            [CommandOption("Sign the Package even if the working tree has uncommitted changes")]
            bool allowDirty = false
            )
        {
            if (!IsValidVersionSuffix(versionSuffix)) return false;

            var sourcePath = CliPath.Resolve(path);
            var outputPath = !string.IsNullOrEmpty(output) ? CliPath.Resolve(output) : sourcePath;

            using var signer = GetSigner(signKey, sourcePath);

            var configurationPath = GetConfigurationPath(sourcePath, configuration);

            // A Package that is only Packed has no Publish Target, so only --version-suffix adds a Suffix
            var packageVersion = GetPackageVersion(sourcePath, configurationPath, version, versionSuffix, null);
            if (packageVersion == null) return false;

            var packageBytes = CreatePackage(sourcePath, configurationPath, category, id, packageVersion, description, publisher, signer, allowDirty);
            if (packageBytes.IsNullOrEmpty()) return false;

            return WritePackageFile(packageBytes, outputPath) != null;
        }


        [Command("publish", "Create a new Package from the specified directory and upload it to a Package Server")]
        [CommandHelp(null, "th packages publish ./MyPackage --server https://packages.trakhound.com --organization TrakHound")]
        public static async Task<bool> Publish(
            [CommandParameter("The path to the directory to create the Package from. If not specified, the current working directory is used")]
            string path = null,
            [CommandOption($"The path to the Package configuration file. If not specified, the {TrakHoundPackage.PackageConfigurationFilename} file in the Package directory is used")]
            string configuration = null,
            [CommandOption("The Category of the Package")]
            string category = null,
            [CommandOption("The ID of the Package")]
            string id = null,
            [CommandOption(VersionDescription, "-v")]
            string version = null,
            [CommandOption(VersionSuffixDescription)]
            string versionSuffix = null,
            [CommandOption("The Description of the Package")]
            string description = null,
            [CommandOption("The Publisher of the Package")]
            string publisher = null,
            [CommandOption(ServerDescription, "-s")]
            string server = null,
            [CommandOption(OrganizationDescription)]
            string organization = null,
            [CommandOption($"The path to the private key to sign the Package with. If not specified, the {SigningKeyVariable} environment variable is used if set")]
            string signKey = null,
            [CommandOption(AllowDirtyDescription)]
            bool allowDirty = false,
            [CommandOption(TargetDescription, "-t")]
            string target = null,
            [CommandOption(SkipExistingDescription)]
            bool skipExisting = false
            )
        {
            if (!IsValidVersionSuffix(versionSuffix)) return false;

            var sourcePath = CliPath.Resolve(path);

            using var signer = GetSigner(signKey, sourcePath);

            var publishSettings = ReadPublishSettings(sourcePath, target);
            if (publishSettings == null) return false;
            if (!ApplyServerOptions(publishSettings, server, organization)) return false;
            if (!CheckApiTokens(publishSettings)) return false;
            if (!CheckPublishTarget(publishSettings, sourcePath, signer != null, ref allowDirty)) return false;

            var configurationPath = GetConfigurationPath(sourcePath, configuration);
            var information = File.Exists(configurationPath) ? TrakHoundPackage.ReadInformationFromFile(configurationPath) : null;
            if (information == null)
            {
                Console.Error.WriteLine($"Error : Package Information File not Found : ({configurationPath})");
                return false;
            }

            var packageVersion = GetPackageVersion(sourcePath, configurationPath, version, versionSuffix, publishSettings);
            if (packageVersion == null) return false;

            var publishDestinations = await GetPublishDestinations(!string.IsNullOrEmpty(id) ? id : information.Id, packageVersion, publishSettings, organization, server, skipExisting, sourcePath);
            if (publishDestinations == null) return false;

            // Every Destination already has this Version from this commit (--skip-existing)
            if (publishDestinations.Destinations.Count < 1)
            {
                Console.WriteLine("Already Published to every Destination : Nothing to Upload");
                return true;
            }

            var packageBytes = CreatePackage(sourcePath, configurationPath, category, id, packageVersion, description, publisher, signer, allowDirty);
            if (packageBytes.IsNullOrEmpty()) return false;

            return await PublishPackage(packageBytes, publishDestinations.Destinations);
        }


        [Command("push", "Upload an existing Package file (.thp) to a Package Server")]
        [CommandHelp(null, "th packages push ./MyPackage-1.0.0.thp")]
        public static async Task<bool> Push(
            [CommandParameter("The path to the Package file (.thp) to upload")]
            string path,
            [CommandOption("The URL of the TrakHound Package Server to upload the package to", "-s")]
            string server = null,
            [CommandOption("The Organization to upload the package to")]
            string organization = null
            )
        {
            var filePath = CliPath.Resolve(path);
            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"Error : Package File not Found : ({filePath})");
                return false;
            }

            return await UploadPackage(await File.ReadAllBytesAsync(filePath), organization, server);
        }


        [Command("dotnet", "Build a DotNet project and create a Package from it (optionally Publishing it)")]
        [CommandHelp(null, "th packages dotnet ./MyFunction --publish --include-debug-symbols false")]
        public static async Task<bool> DotNet(
            [CommandParameter("The path to the DotNet project directory (or .csproj file). If not specified, the current working directory is used")]
            string path = null,
            [CommandOption("The path to the directory to write the Package file to. If not specified, the current working directory is used (unless --publish is set)", "-o")]
            string output = null,
            [CommandOption("The DotNet build configuration")]
            string projectConfiguration = "Release",
            [CommandOption("Include the Debug Symbols (.pdb) in the Package")]
            bool includeDebugSymbols = true,
            [CommandOption("Include the Source files (src.zip) in the Package")]
            bool includeSource = false,
            [CommandOption($"Upload the Package to the Publish Target's Destinations ({TrakHoundPackagePublishSettings.Filename}), or the specified Server/Organization")]
            bool publish = false,
            [CommandOption(VersionDescription, "-v")]
            string version = null,
            [CommandOption(VersionSuffixDescription)]
            string versionSuffix = null,
            [CommandOption(ServerDescription, "-s")]
            string server = null,
            [CommandOption(OrganizationDescription)]
            string organization = null,
            [CommandOption($"The path to the private key to sign the Package with. If not specified, the {SigningKeyVariable} environment variable is used if set")]
            string signKey = null,
            [CommandOption(AllowDirtyDescription)]
            bool allowDirty = false,
            [CommandOption(TargetDescription + " (with --publish)", "-t")]
            string target = null,
            [CommandOption(SkipExistingDescription + " (with --publish)")]
            bool skipExisting = false
            )
        {
            if (!IsValidVersionSuffix(versionSuffix)) return false;
            if (!string.IsNullOrEmpty(target) && !publish) throw new CommandException("--target is only used with --publish");
            if (skipExisting && !publish) throw new CommandException("--skip-existing is only used with --publish");

            var build = await BuildDotNet(path, output, projectConfiguration, includeDebugSymbols, includeSource, publish, version, versionSuffix, server, organization, signKey, allowDirty, target, skipExisting);
            if (build == null) return false;

            return !publish || await PublishDotNet(build);
        }

        /// <summary>
        /// A Package created from a DotNet project, with what is needed to publish it later
        /// </summary>
        private class DotNetBuild
        {
            public byte[] PackageBytes { get; set; }

            public TrakHoundPackage Information { get; set; }

            public string ProjectDirectory { get; set; }

            /// <summary>
            /// The Destinations to upload the Package to (null if the Package isn't published)
            /// </summary>
            public PublishDestinations PublishDestinations { get; set; }
        }

        /// <summary>
        /// Build a DotNet project and create a Package from it, without uploading it (see <see cref="PublishDotNet"/>).
        /// Returns null if the Package couldn't be created
        /// </summary>
        private static async Task<DotNetBuild> BuildDotNet(
            string path,
            string output,
            string projectConfiguration,
            bool includeDebugSymbols,
            bool includeSource,
            bool publish,
            string version,
            string versionSuffix,
            string server,
            string organization,
            string signKey,
            bool allowDirty,
            string target = null,
            bool skipExisting = false
            )
        {
            var rootPath = CliPath.Resolve(path);
            var projectDir = File.Exists(rootPath) ? Path.GetDirectoryName(rootPath) : rootPath;

            using var signer = GetSigner(signKey, projectDir);

            string outputPath = null;
            if (!string.IsNullOrEmpty(output)) outputPath = CliPath.Resolve(output);
            else if (!publish) outputPath = Environment.CurrentDirectory;

            var informationPath = Path.Combine(projectDir, TrakHoundPackage.PackageConfigurationFilename);
            var information = TrakHoundPackage.ReadInformationFromFile(informationPath);
            if (information == null)
            {
                Console.Error.WriteLine($"Error : Package Information File not Found : ({informationPath})");
                return null;
            }

            Console.WriteLine($"Package Information Read : ({informationPath})");

            // A Package that isn't published has no Publish Target, so only --version-suffix adds a Suffix
            PublishSettings publishSettings = null;
            if (publish)
            {
                publishSettings = ReadPublishSettings(projectDir, target);
                if (publishSettings == null) return null;
                if (!ApplyServerOptions(publishSettings, server, organization)) return null;
                if (!CheckApiTokens(publishSettings)) return null;
                if (!CheckPublishTarget(publishSettings, projectDir, signer != null, ref allowDirty)) return null;
            }

            var packageVersion = GetPackageVersion(projectDir, informationPath, version, versionSuffix, publishSettings);
            if (packageVersion == null) return null;

            // Checked before building, so a Version that's already published doesn't wait for a build that can't be uploaded
            PublishDestinations publishDestinations = null;
            if (publish)
            {
                publishDestinations = await GetPublishDestinations(information.Id, packageVersion, publishSettings, organization, server, skipExisting, projectDir);
                if (publishDestinations == null) return null;
            }

            Console.WriteLine($"Packing DotNet Project(s) : ({rootPath})");

            var dotnetSettings = new TrakHoundDotNetPackageSettings();
            dotnetSettings.DotnetSdkLocation = TrakHoundCliConfiguration.Instance.DotnetSdkLocation;
            dotnetSettings.Version = packageVersion;
            dotnetSettings.Configuration = projectConfiguration;
            dotnetSettings.IncludeDebugSymbols = includeDebugSymbols;
            dotnetSettings.IncludeSource = includeSource;

            // Set Git Parameters
            (dotnetSettings.GitBranch, dotnetSettings.GitCommit, dotnetSettings.GitDirty) = GitCommands.GetBranchAndCommit(projectDir);
            if (!CheckWorkingTree(dotnetSettings.GitCommit, dotnetSettings.GitDirty, signer, allowDirty)) return null;

            dotnetSettings.Signer = signer;

            var packageBytes = TrakHoundDotNetPackage.Create(rootPath, dotnetSettings);
            if (packageBytes.IsNullOrEmpty())
            {
                Console.Error.WriteLine("Error : DotNet Package Build Failed");
                return null;
            }

            information = TrakHoundPackage.ReadInformationFromPackage(packageBytes);
            if (information == null)
            {
                Console.Error.WriteLine("Error : Unable to read the Package Information from the created Package");
                return null;
            }

            Console.WriteLine($"TrakHound Package ({information.Id} : v{information.Version}) Created Successfully");

            if (outputPath != null)
            {
                if (WritePackageFile(packageBytes, outputPath) == null) return null;
            }

            return new DotNetBuild
            {
                PackageBytes = packageBytes,
                Information = information,
                ProjectDirectory = projectDir,
                PublishDestinations = publishDestinations
            };
        }

        /// <summary>
        /// Upload a Package created by <see cref="BuildDotNet"/> (with publish set) to its Destinations
        /// </summary>
        private static async Task<bool> PublishDotNet(DotNetBuild build)
        {
            if (build.PublishDestinations == null)
            {
                Console.Error.WriteLine($"Error : ({build.Information?.Id}) was built without publishing : Not Published");
                return false;
            }

            // Every Destination already has this Version from this commit (--skip-existing)
            if (build.PublishDestinations.Destinations.Count < 1)
            {
                Console.WriteLine($"({build.Information?.Id} : v{build.Information?.Version}) is already Published to every Destination : Nothing to Upload");
                return true;
            }

            return await PublishPackage(build.PackageBytes, build.PublishDestinations.Destinations);
        }


        /// <summary>
        /// Get the Packages directory for a root directory. TrakHoundPackage methods expect the Packages directory itself (ex. [root]/packages)
        /// </summary>
        public static string GetPackagesPath(string rootPath) => Path.Combine(rootPath, TrakHoundPackage.PackageDirectory);

        /// <summary>
        /// Read the Packages File from the Packages directory, falling back to the legacy location in the root directory
        /// </summary>
        private static TrakHoundPackagesFile ReadPackagesFile(string rootPath)
        {
            var packagesFile = TrakHoundPackagesFile.Read(GetPackagesPath(rootPath));
            if (packagesFile != null) return packagesFile;

            packagesFile = TrakHoundPackagesFile.Read(rootPath);
            if (packagesFile != null) Console.WriteLine($"Packages File Read from legacy location : ({Path.Combine(rootPath, TrakHoundPackagesFile.Filename)})");

            return packagesFile;
        }

        private static string GetConfigurationPath(string sourcePath, string configuration)
        {
            if (!string.IsNullOrEmpty(configuration))
            {
                var configurationPath = CliPath.Resolve(configuration);
                if (Directory.Exists(configurationPath)) configurationPath = Path.Combine(configurationPath, TrakHoundPackage.PackageConfigurationFilename);
                return configurationPath;
            }

            return Path.Combine(sourcePath, TrakHoundPackage.PackageConfigurationFilename);
        }

        private const string VersionDescription = "The Version of the Package. If not specified, the two part prefix in trakhound.package.json and the git height are used (ex. \"0.3\" and 4 commits is 0.3.4)";

        private const string VersionSuffixDescription = "A Prerelease Label to add to the Version (ex. dev.{timestamp} creates 0.3.4-dev.20261114153000). Replaces the Publish Target's \"versionSuffix\". {timestamp} and {branch} are replaced as in a Target. Instances only select a Prerelease when a Version requests one (ex. \"*-*\") or no Stable Version matches";

        private const string ServerDescription = $"The URL of the TrakHound Package Server to upload the package to. With a {TrakHoundPackagePublishSettings.Filename} file, it replaces the Publish Target's Package Server (its other settings still apply, and its apiTokenVariable is only used for its own server)";

        private const string OrganizationDescription = $"The Organization to upload the package to. With a {TrakHoundPackagePublishSettings.Filename} file, it replaces the Publish Target's Organization";

        private const string TargetDescription = $"The Publish Target to publish to (ex. development or release), from the nearest {TrakHoundPackagePublishSettings.Filename}. If not specified, its \"defaultTarget\" is used";

        private const string SkipExistingDescription = "Skip a Destination that already has the Version (instead of an error) when that Package was built from the same commit without uncommitted changes, since it is the same source. The working tree must be a clean commit. A Version published from another commit is still an error";

        private const string AllowDirtyDescription = "Build and sign the Package even if the working tree has uncommitted changes. Not allowed for a Publish Target with \"requireSigning\" (a Target with \"allowDirty\" doesn't need it)";

        /// <summary>
        /// Returns false (and writes an error) if the Version Suffix isn't a Semantic Versioning Prerelease Label (dot separated identifiers of letters, digits and '-')
        /// once its {timestamp} and {branch} Placeholders are replaced. A Suffix that isn't set is valid
        /// </summary>
        internal static bool IsValidVersionSuffix(string versionSuffix, string source = "--version-suffix")
        {
            if (string.IsNullOrEmpty(versionSuffix)) return true;

            // Checked as on a feature branch, so the characters {branch} adds off the default branch are checked too
            var expanded = PackageVersions.ExpandSuffix(versionSuffix, "feature/branch");
            if (System.Text.RegularExpressions.Regex.IsMatch(expanded, "^[0-9A-Za-z-]+(\\.[0-9A-Za-z-]+)*$")) return true;

            Console.Error.WriteLine($"Error : Invalid {source} ({versionSuffix}) : Use letters, digits, '-' and '.' separated identifiers, and the {TrakHoundPackagePublishTarget.TimestampPlaceholder} and {TrakHoundPackagePublishTarget.BranchPlaceholder} Placeholders (ex. dev{TrakHoundPackagePublishTarget.BranchPlaceholder}.{TrakHoundPackagePublishTarget.TimestampPlaceholder} or beta.1)");
            return false;
        }

        /// <summary>
        /// Add the Version Suffix to the Version (ex. 0.3.14 and dev.20261111224029 is 0.3.14-dev.20261111224029).
        /// A Version that already has a Prerelease Label (ex. --version 1.0.0-beta) is used as is
        /// </summary>
        private static string AddVersionSuffix(string version, string versionSuffix)
        {
            if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(versionSuffix)) return version;

            if (!VersionComparer.IsStable(version))
            {
                Console.WriteLine($"Version ({version}) already has a Prerelease Label : Version Suffix ({versionSuffix}) not added");
                return version;
            }

            return $"{version}-{versionSuffix}";
        }

        /// <summary>
        /// The Destinations to upload a Package to
        /// </summary>
        private class PublishDestinations
        {
            /// <summary>
            /// The Destinations of the Publish Target (or --server and --organization without one) to upload to. Each one's Versions were read.
            /// With --skip-existing, the ones that already have the Version (from the same commit) aren't included
            /// </summary>
            public List<TrakHoundPackagePublishDestination> Destinations { get; set; }

            /// <summary>
            /// The Destinations that already have the Version, built from the same commit (with --skip-existing)
            /// </summary>
            public List<TrakHoundPackagePublishDestination> AlreadyPublished { get; set; } = new List<TrakHoundPackagePublishDestination>();
        }

        /// <summary>
        /// Read the published Versions from each Destination (the Publish Target's, or --server and --organization without one), and check that the
        /// Version isn't already published. Versions are never replaced, so its upload would be rejected. This only checks : the Version is never changed.
        /// With skipExisting, a Destination that already has the Version is skipped (instead of an error) when that Package was built from the
        /// same commit without uncommitted changes, and the working tree is clean : it is the same source, so there is nothing new to publish.
        /// Returns null (after writing an error) if any Destination can't be read, so the Package is published to all of them or none,
        /// or if the Version is already published (and can't be skipped)
        /// </summary>
        private static async Task<PublishDestinations> GetPublishDestinations(string packageId, string version, PublishSettings publishSettings, string organization, string server, bool skipExisting = false, string sourcePath = null)
        {
            // The same Destinations that PublishPackage() uploads to
            var targets = publishSettings?.Destinations != null
                ? publishSettings.Destinations.ToList()
                : new List<TrakHoundPackagePublishDestination> { new TrakHoundPackagePublishDestination { Organization = organization, PackageServer = server } };

            var published = new List<TrakHoundPackagePublishDestination>();
            var unreadable = 0;
            foreach (var target in targets)
            {
                var packageClient = PackageClient.Get(target);
                Console.Write($"Reading Published Versions ({packageClient.BaseUrl} | {packageClient.Organization})...");

                var versionsResult = await packageClient.TryGetVersions(packageId);
                if (!versionsResult.Success)
                {
                    Console.WriteLine("ERROR");
                    Console.Error.WriteLine($"Error : Unable to read the Published Versions of ({packageId}) from ({packageClient.BaseUrl} | {packageClient.Organization}) : {versionsResult.Message}");
                    unreadable++;
                    continue;
                }

                var versions = versionsResult.Versions?.Where(o => !string.IsNullOrEmpty(o)).ToList() ?? new List<string>();
                if (versions.Contains(version, StringComparer.OrdinalIgnoreCase)) published.Add(target);

                Console.WriteLine($"DONE : {versions.Count} Published");
            }

            if (unreadable > 0)
            {
                Console.Error.WriteLine($"Error : {unreadable} of {targets.Count} Publish Destination(s) couldn't be read for ({packageId}) : Nothing Published");
                return null;
            }

            var result = new PublishDestinations { Destinations = targets };
            if (published.Count < 1) return result;

            var targetName = publishSettings?.HasTarget == true ? $" to ({publishSettings.TargetName})" : null;
            var alreadyPublished = $"Error : ({packageId} : v{version}) is already published{targetName} : Commit a change (the version adds the git height) or change the version prefix in {TrakHoundPackage.PackageConfigurationFilename}";

            if (!skipExisting)
            {
                Console.Error.WriteLine(alreadyPublished);
                return null;
            }

            // Only the same source can be skipped : a clean commit, and the published Package built from it
            var (_, commit, dirty) = GitCommands.GetBranchAndCommit(sourcePath ?? Environment.CurrentDirectory);
            if (string.IsNullOrEmpty(commit) || dirty != false)
            {
                Console.Error.WriteLine($"{alreadyPublished}. --skip-existing only skips it from a clean commit ({(string.IsNullOrEmpty(commit) ? "not in a git Repository" : "the working tree has uncommitted changes")})");
                return null;
            }

            foreach (var target in published)
            {
                var packageClient = PackageClient.Get(target);
                var reason = await GetPublishedFromOtherSource(packageClient, packageId, version, commit);
                if (reason != null)
                {
                    Console.Error.WriteLine($"{alreadyPublished}. It can't be skipped : the published Package in ({packageClient.BaseUrl} | {packageClient.Organization}) {reason}");
                    return null;
                }

                Console.WriteLine($"({packageId} : v{version}) is already published to ({packageClient.BaseUrl} | {packageClient.Organization}) from this commit ({ShortCommit(commit)}) : Skipped");
            }

            result.Destinations = targets.Except(published).ToList();
            result.AlreadyPublished = published;
            return result;
        }

        /// <summary>
        /// Why the published Version isn't the same source as the commit (another commit, uncommitted changes, or unknown). Null if it is
        /// </summary>
        private static async Task<string> GetPublishedFromOtherSource(TrakHoundPackageClient packageClient, string packageId, string version, string commit)
        {
            TrakHoundPackage package;
            try
            {
                package = await packageClient.Get(packageId, version);
            }
            catch (Exception ex)
            {
                return $"couldn't be read ({ex.Message})";
            }

            return GetOtherSourceReason(package, commit);
        }

        /// <summary>
        /// Why the Package isn't built from the commit without uncommitted changes (another commit, uncommitted changes, or unknown). Null if it is
        /// </summary>
        internal static string GetOtherSourceReason(TrakHoundPackage package, string commit)
        {
            if (package == null) return "couldn't be read";

            var publishedCommit = package.GetMetadata(TrakHoundPackage.RepositoryCommitName);
            if (string.IsNullOrEmpty(publishedCommit)) return "has no commit recorded";
            if (!string.Equals(publishedCommit, commit, StringComparison.OrdinalIgnoreCase)) return $"was built from another commit ({ShortCommit(publishedCommit)})";
            if (!string.Equals(package.GetMetadata(TrakHoundPackage.RepositoryDirtyName), "false", StringComparison.OrdinalIgnoreCase)) return "was built with uncommitted changes (or doesn't record whether it was)";

            return null;
        }

        private static string ShortCommit(string commit) => !string.IsNullOrEmpty(commit) ? commit.Substring(0, Math.Min(7, commit.Length)) : commit;

        private static string WritePackageFile(byte[] packageBytes, string outputPath)
        {
            var information = TrakHoundPackage.ReadInformationFromPackage(packageBytes);
            if (information == null)
            {
                Console.Error.WriteLine("Error : Unable to read the Package Information from the created Package");
                return null;
            }

            // Set filename ([PackageId]-[Version].thp)
            var filename = information.Id + "-" + information.Version + TrakHoundPackage.FileExtension;
            var destinationPath = Path.Combine(outputPath, filename);

            Console.Write($"Writing Package File...");

            if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);
            File.WriteAllBytes(destinationPath, packageBytes);

            Console.WriteLine("DONE");
            Console.WriteLine($"TrakHound Package ({filename}) File Created Successfully:");
            Console.WriteLine(destinationPath);

            return destinationPath;
        }

        private static byte[] CreatePackage(
            string rootPath,
            string configurationPath,
            string category = null,
            string id = null,
            string version = null,
            string description = null,
            string publisher = null,
            TrakHoundPackageSigner signer = null,
            bool allowDirty = false
            )
        {
            Console.WriteLine($"Package Source = \"{rootPath}\"");

            if (!Directory.Exists(rootPath))
            {
                Console.Error.WriteLine($"Error : Package Source Directory not Found : ({rootPath})");
                return null;
            }

            Dictionary<string, object> metadata = null;
            IEnumerable<TrakHoundPackageDependency> dependencies = null;

            // Read Configuration File (command options take priority over the configuration file)
            if (!string.IsNullOrEmpty(configurationPath) && File.Exists(configurationPath))
            {
                var configuration = Json.Read<TrakHoundPackageJson>(configurationPath);
                if (configuration != null)
                {
                    if (string.IsNullOrEmpty(category)) category = configuration.Category;
                    if (string.IsNullOrEmpty(id)) id = configuration.Id;

                    if (configuration.Metadata != null)
                    {
                        if (string.IsNullOrEmpty(description) && configuration.Metadata.ContainsKey("description")) description = configuration.Metadata["description"]?.ToString();
                        if (string.IsNullOrEmpty(publisher) && configuration.Metadata.ContainsKey("publisher")) publisher = configuration.Metadata["publisher"]?.ToString();
                    }

                    metadata = configuration.Metadata;
                    dependencies = configuration.Dependencies;
                }
            }
            else if (!string.IsNullOrEmpty(configurationPath))
            {
                Console.WriteLine($"Package Configuration File not Found : ({configurationPath})");
            }

            // Validate Dependencies (ex. an invalid Minimum Version)
            if (!dependencies.IsNullOrEmpty())
            {
                foreach (var dependency in dependencies)
                {
                    var dependencyError = TrakHoundPackageDependency.Validate(dependency);
                    if (dependencyError != null)
                    {
                        Console.Error.WriteLine($"Error : {dependencyError}");
                        return null;
                    }
                }
            }

            var (gitBranch, gitCommit, gitDirty) = GitCommands.GetBranchAndCommit(rootPath);
            if (!CheckWorkingTree(gitCommit, gitDirty, signer, allowDirty)) return null;

            Console.Write($"Packing Files...");

            // Create Package Model
            var packageBuilder = new TrakHoundPackageBuilder();
            packageBuilder.Category = category;
            packageBuilder.Id = id;
            packageBuilder.Version = version;
            packageBuilder.BuildDate = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(description)) packageBuilder.SetMetadata(TrakHoundPackage.DescriptionName, description);
            if (!string.IsNullOrEmpty(publisher)) packageBuilder.SetMetadata(TrakHoundPackage.PublisherName, publisher);

            if (!string.IsNullOrEmpty(gitBranch)) packageBuilder.SetMetadata(TrakHoundPackage.RepositoryBranchName, gitBranch);
            if (!string.IsNullOrEmpty(gitCommit)) packageBuilder.SetMetadata(TrakHoundPackage.RepositoryCommitName, gitCommit);
            if (gitDirty.HasValue) packageBuilder.SetMetadata(TrakHoundPackage.RepositoryDirtyName, gitDirty.Value ? "true" : "false");

            if (!metadata.IsNullOrEmpty())
            {
                foreach (var o in metadata)
                {
                    if (!string.IsNullOrEmpty(o.Key) && !packageBuilder.Metadata.ContainsKey(o.Key))
                    {
                        packageBuilder.Metadata.Add(o.Key, o.Value);
                    }
                }
            }

            packageBuilder.Dependencies = dependencies;

            var package = packageBuilder.Build();

            // Create a new Package (ZipFile) from the directory
            var packageBytes = package.Package(rootPath, signer);
            if (packageBytes.IsNullOrEmpty())
            {
                Console.WriteLine("ERROR");
                return null;
            }

            Console.WriteLine("DONE");
            Console.WriteLine($"TrakHound Package ({package.Id} v{package.Version}) Created Successfully");

            return packageBytes;
        }

        /// <param name="installed">The Packages that were Installed (or were already Installed) are added to it</param>
        private static async Task<bool> InstallPackage(ITrakHoundPackageDependency package, string organization, string server, string packagesPath, HashSet<string> visited, bool includeDependencies, List<TrakHoundPackage> installed, Func<byte[], bool> allowPackage)
        {
            // Skip Packages that were already processed (shared Dependencies or Dependency cycles)
            if (!visited.Add($"{package.Id}@{TrakHoundPackageDependency.ToVersionString(package)}")) return true;

            var packageClient = PackageClient.Get(server, organization);

            try
            {
                Console.WriteLine("-----");
                Console.Write($"Downloading Package Information ({package.Id}) : {packageClient.BaseUrl}...");

                // Get Package from Server
                TrakHoundPackage serverPackage;
                if (!string.IsNullOrEmpty(package.Version))
                {
                    serverPackage = await packageClient.Get(package.Id, package.Version);
                }
                else
                {
                    serverPackage = await packageClient.GetLatest(package.Id);
                }

                if (serverPackage == null)
                {
                    Console.WriteLine("ERROR");
                    Console.Error.WriteLine($"Package not Found : {package.Id}" + (!string.IsNullOrEmpty(package.Version) ? $" => {package.Version}" : null));
                    return false;
                }

                // The latest Version that matches the Version must also meet the Minimum Version
                if (!string.IsNullOrEmpty(package.MinimumVersion) && !TrakHoundPackageDependency.IsSatisfiedBy(package, serverPackage.Version))
                {
                    Console.WriteLine("ERROR");
                    Console.Error.WriteLine($"Package ({package.Id}) Version {serverPackage.Version} does not satisfy the Dependency : {TrakHoundPackageDependency.ToVersionString(package)}");
                    return false;
                }

                Console.WriteLine("DONE");

                Console.WriteLine();
                Console.WriteLine(string.Format("{0,-12} : {1}", "UUID", serverPackage.Uuid));
                Console.WriteLine(string.Format("{0,-12} : {1}", "ID", serverPackage.Id));
                Console.WriteLine(string.Format("{0,-12} : {1}", "Version", serverPackage.Version));
                Console.WriteLine(string.Format("{0,-12} : {1}", "BuildDate", serverPackage.BuildDate));

                var packageDescription = serverPackage.GetMetadata(TrakHoundPackage.DescriptionName);
                if (!string.IsNullOrEmpty(packageDescription))
                {
                    Console.WriteLine(string.Format("{0,-12} : {1}", "Description", packageDescription));
                }
                Console.WriteLine();

                var success = true;

                if (includeDependencies && !serverPackage.Dependencies.IsNullOrEmpty())
                {
                    Console.WriteLine("Reading Package Dependencies...");

                    foreach (var dependency in serverPackage.Dependencies)
                    {
                        if (!await InstallPackage(dependency, organization, server, packagesPath, visited, includeDependencies, installed, allowPackage)) success = false;
                    }
                }

                var install = true;
                var installedPackageInformation = TrakHoundPackage.ReadInformation(serverPackage.Category, serverPackage.Id, serverPackage.Version, packagesPath);
                if (installedPackageInformation != null)
                {
                    install = !TrakHoundPackage.IsSameBuild(installedPackageInformation, serverPackage);
                }

                if (!install)
                {
                    Console.WriteLine($"Package Already Installed : {serverPackage.Id} => {serverPackage.Version}");
                    installed.Add(serverPackage);
                    return success;
                }

                Console.WriteLine("-----");
                Console.Write($"Downloading Package Contents ({serverPackage.Id})...");

                var packageBytes = await packageClient.Download(serverPackage.Id, serverPackage.Version);
                if (packageBytes.IsNullOrEmpty())
                {
                    Console.WriteLine("ERROR");
                    Console.Error.WriteLine("Error Downloading Package. Please try again");
                    return false;
                }

                Console.WriteLine("DONE");

                if (!allowPackage(packageBytes)) return false;

                Console.Write($"Installing Package ({serverPackage.Id})...");

                if (TrakHoundPackage.Install(packageBytes, packagesPath))
                {
                    Console.WriteLine("DONE");
                    Console.WriteLine("Package Installed : " + serverPackage.Id + " => " + serverPackage.Version + " : " + serverPackage.Category);
                    installed.Add(serverPackage);
                    return success;
                }
                else
                {
                    Console.WriteLine("ERROR");
                    Console.Error.WriteLine("Error Installing Package. Please try again");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR");
                Console.Error.WriteLine($"Error Installing Package ({package.Id}) : {ex.Message}");
                return false;
            }
        }

        private static async Task<bool> PublishPackage(byte[] packageBytes, IEnumerable<TrakHoundPackagePublishDestination> destinations)
        {
            if (destinations.IsNullOrEmpty())
            {
                Console.Error.WriteLine("Error : No Publish Destinations : Nothing Published");
                return false;
            }

            var success = true;
            foreach (var publishDestination in destinations)
            {
                if (!await UploadPackage(packageBytes, PackageClient.Get(publishDestination))) success = false;
            }
            return success;
        }

        private static Task<bool> UploadPackage(byte[] packageBytes, string organization, string server)
        {
            return UploadPackage(packageBytes, PackageClient.Get(server, organization));
        }

        private static async Task<bool> UploadPackage(byte[] packageBytes, TrakHoundPackageClient packageClient)
        {
            Console.Write($"Uploading Package File ({packageClient.BaseUrl} | {packageClient.Organization})...");

            try
            {
                var (statusCode, package, message) = await packageClient.UploadPackage(packageBytes);
                if (package != null)
                {
                    Console.WriteLine("DONE");
                    Console.WriteLine("Package Uploaded Successfully");
                    return true;
                }

                Console.WriteLine("ERROR");

                // The server's reason (ex. a missing Api Token, or a Version that's already stored)
                var status = statusCode > 0 ? $" ({statusCode})" : " (the server couldn't be reached)";
                Console.Error.WriteLine($"Error Uploading Package{status} : {message}");

                if (statusCode == 401 || statusCode == 403)
                {
                    Console.Error.WriteLine("The Api Token is TRAKHOUND_API_TOKEN, apiToken in cli.config.yaml, or a Publish Destination's apiTokenVariable");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR");
                Console.Error.WriteLine($"Error Uploading Package : {ex.Message}");
            }

            return false;
        }
    }
}
