// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Deployments;
using TrakHound.Packages;

namespace TrakHound.CLI.Deployments
{
    [CommandGroup("deployments", "Create, Install and Clean TrakHound Deployments")]
    internal static class Deployments
    {
        [Command("install", "Install a Deployment from a local File")]
        [CommandHelp(null, "th deployments install ./MyDeployment-1.0.0.thd ./instance")]
        public static async Task<bool> InstallLocal(
            [CommandParameter("The path of the Deployment file (.thd)")]
            string target,
            [CommandParameter("The path of the directory to install the Deployment to. If not specified, the current working directory is used")]
            string path = null
            )
        {
            var targetPath = CliPath.Resolve(target);
            var installPath = CliPath.Resolve(path);

            Console.WriteLine($"Install Local Deployment : {targetPath} : to {installPath}");

            if (!File.Exists(targetPath))
            {
                Console.Error.WriteLine($"Error : Deployment File not Found : ({targetPath})");
                return false;
            }

            Console.Write("Reading Deployment Information from File..");

            var deploymentBytes = await File.ReadAllBytesAsync(targetPath);
            var deployment = TrakHoundDeployment.ReadInformationFromDeployment(deploymentBytes);
            if (deployment == null)
            {
                Console.WriteLine("ERROR");
                Console.Error.WriteLine("Error : Unable to read the Deployment Information. The file may not be a valid Deployment");
                return false;
            }

            Console.WriteLine("Done");
            PrintDeployment(deployment);

            return InstallDeployment(deploymentBytes, installPath);
        }

        [Command("create", "Create a new Deployment file (.thd) from a directory")]
        [CommandHelp(null, "th deployments create my-profile ./instance --version 1.0.0 --output ./dist")]
        public static async Task<bool> Create(
            [CommandParameter($"The Profile ID of the Deployment to Create")]
            string profileId,
            [CommandParameter("The path of the root directory to create the Deployment from. If not specified, the current working directory is used")]
            string path = null,
            [CommandOption("The Version of the Deployment", "-v")]
            string version = null,
            [CommandOption("The path of the directory to write the created Deployment to. If not specified, the current working directory is used", "-o")]
            string output = null
            )
        {
            var sourcePath = CliPath.Resolve(path);
            var outputPath = CliPath.Resolve(output);

            Console.WriteLine($"Create Deployment : {profileId} : version = {version} : from {sourcePath}");

            if (!Directory.Exists(sourcePath))
            {
                Console.Error.WriteLine($"Error : Source Directory not Found : ({sourcePath})");
                return false;
            }

            var deployment = new TrakHoundDeployment();
            deployment.ProfileId = profileId;
            deployment.Version = version;
            deployment.BuildDate = DateTime.UtcNow;

            Console.Write("Creating Deployment..");

            var deploymentResult = deployment.Create(sourcePath);
            if (!deploymentResult.Success)
            {
                Console.WriteLine("ERROR");
                PrintErrors(deploymentResult.Errors);
                return false;
            }

            Console.WriteLine("Done");

            string outputFilename;
            if (!string.IsNullOrEmpty(version)) outputFilename = $"{profileId}-{version}{TrakHoundDeployment.FileExtension}";
            else outputFilename = $"{profileId}{TrakHoundDeployment.FileExtension}";

            var writePath = Path.Combine(outputPath, outputFilename);
            Console.Write($"Writing Deployment to ({writePath})..");

            if (!Directory.Exists(outputPath)) Directory.CreateDirectory(outputPath);
            await File.WriteAllBytesAsync(writePath, deploymentResult.Content);
            Console.WriteLine("Done");

            return true;
        }

        [Command("clean", "Remove the installed Packages, Configurations, Packages File and Deployment File from a directory")]
        [CommandHelp(null, "th deployments clean ./instance --yes")]
        public static bool Clean(
            [CommandParameter("The path of the directory to clean. If not specified, the current working directory is used")]
            string path = null,
            [CommandOption("Skip the confirmation prompt", "-y")]
            bool yes = false
            )
        {
            var installPath = CliPath.Resolve(path);

            // Never delete the root directory itself (only known subdirectories)
            var directories = new string[]
            {
                Path.Combine(installPath, TrakHoundPackage.PackageDirectory),
                Path.Combine(installPath, TrakHoundConfigurations.Directory)
            }
            .Where(o => Directory.Exists(o) && !string.Equals(Path.GetFullPath(o).TrimEnd(Path.DirectorySeparatorChar), installPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            .ToList();

            var files = new string[]
            {
                Path.Combine(installPath, TrakHoundPackagesFile.Filename),
                Path.Combine(installPath, TrakHoundDeployment.DeploymentFile)
            }.Where(File.Exists).ToList();

            if (directories.Count == 0 && files.Count == 0)
            {
                Console.WriteLine($"Nothing to Clean in ({installPath})");
                return true;
            }

            Console.WriteLine($"The following will be permanently deleted from ({installPath}):");
            foreach (var directory in directories) Console.WriteLine($"  {directory}{Path.DirectorySeparatorChar}");
            foreach (var file in files) Console.WriteLine($"  {file}");

            if (!yes)
            {
                if (Console.IsInputRedirected)
                {
                    Console.Error.WriteLine("Error : Confirmation required. Use --yes to clean without a prompt");
                    return false;
                }

                Console.Write("Continue? [y/N] ");
                var answer = Console.ReadLine()?.Trim();
                if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase) && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Clean Cancelled");
                    return false;
                }
            }

            foreach (var directory in directories)
            {
                Console.Write($"Removing ({directory})..");
                Directory.Delete(directory, true);
                Console.WriteLine("Done");
            }

            foreach (var file in files)
            {
                Console.Write($"Removing ({file})..");
                File.Delete(file);
                Console.WriteLine("Done");
            }

            return true;
        }


        private static bool InstallDeployment(byte[] deploymentBytes, string installPath)
        {
            Console.Write("Installing Deployment..");

            var installResult = TrakHoundDeployment.Install(deploymentBytes, installPath);
            if (installResult.Success)
            {
                Console.WriteLine("Done");
                return true;
            }

            Console.WriteLine("ERROR");
            PrintErrors(installResult.Errors);
            return false;
        }

        private static void PrintDeployment(TrakHoundDeployment deployment)
        {
            Console.WriteLine();
            Console.WriteLine(string.Format("{0,-12} : {1}", "ID", deployment.Id));
            Console.WriteLine(string.Format("{0,-12} : {1}", "Profile ID", deployment.ProfileId));
            Console.WriteLine(string.Format("{0,-12} : {1}", "Version", deployment.Version));
            Console.WriteLine(string.Format("{0,-12} : {1}", "BuildDate", deployment.BuildDate));
            Console.WriteLine(string.Format("{0,-12} : {1}", "Description", deployment.Description));
            Console.WriteLine();
        }

        private static void PrintErrors(IEnumerable<string> errors)
        {
            if (!errors.IsNullOrEmpty())
            {
                foreach (var error in errors)
                {
                    Console.Error.WriteLine($"ERROR : {error}");
                }
            }
        }
    }
}
