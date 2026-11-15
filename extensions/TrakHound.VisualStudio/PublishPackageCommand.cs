// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// Builds, Packs and Publishes the selected project using the TrakHound CLI ('th packages dotnet --project-configuration Release --publish', run from the project directory)
    /// </summary>
    [VisualStudioContribution]
    internal class PublishPackageCommand : PublishCommand
    {
        // Written by the CLI when the Package is created : "TrakHound Package (Id : vVersion) Created Successfully"
        private static readonly Regex _packageCreatedRegex = new(@"TrakHound Package \((?<id>.+) : v(?<version>.+)\) Created Successfully");


        public PublishPackageCommand(PublishLog log) : base(log) { }


        public override CommandConfiguration CommandConfiguration => new("%TrakHound.VisualStudio.PublishPackageCommand.DisplayName%")
        {
            Icon = new(ImageMoniker.KnownValues.Upload, IconSettings.IconAndText),
            Placements =
            [
                // Solution Explorer Project context menu (same group as Build / Rebuild / Publish)
                CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 518, priority: 0x0100),
                CommandPlacement.KnownPlacements.ToolsMenu,
            ],
            EnabledWhen = ActivationConstraint.SolutionState(SolutionState.FullyLoaded),
        };


        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            if (!await TryStartAsync(cancellationToken)) return;

            try
            {
                var projectPath = await GetProjectPathAsync(context, cancellationToken);
                if (projectPath == null)
                {
                    await ShowMessageAsync("Select a Project in Solution Explorer to Publish", PromptOptions.OK, cancellationToken);
                    return;
                }

                var projectDirectory = Path.GetDirectoryName(projectPath);
                if (!File.Exists(Path.Combine(projectDirectory, PackageConfigurationFilename)))
                {
                    await ShowMessageAsync($"{Path.GetFileNameWithoutExtension(projectPath)} is not a TrakHound Package : {PackageConfigurationFilename} was not found in {projectDirectory}", PromptOptions.ErrorConfirm, cancellationToken);
                    return;
                }

                var cliPath = await FindCliAsync(cancellationToken);
                if (cliPath == null) return;

                await PublishAsync(cliPath, projectPath, projectDirectory, cancellationToken);
            }
            finally
            {
                Stop();
            }
        }

        private async Task PublishAsync(string cliPath, string projectPath, string projectDirectory, CancellationToken cancellationToken)
        {
            string packageId = null;
            string packageVersion = null;
            var stopwatch = Stopwatch.StartNew();

            // The project is read from the Working Directory (the project directory)
            var exitCode = await RunCliAsync(
                cliPath,
                projectDirectory,
                ["packages", "dotnet", "--project-configuration", "Release", "--publish"],
                $"Publish TrakHound Package : {projectPath}",
                $"Publishing {Path.GetFileNameWithoutExtension(projectPath)}...",
                line =>
                {
                    var match = _packageCreatedRegex.Match(line);
                    if (match.Success)
                    {
                        packageId = match.Groups["id"].Value;
                        packageVersion = match.Groups["version"].Value;
                    }
                },
                cancellationToken);
            if (exitCode == null) return;

            var package = packageId != null ? $"{packageId} v{packageVersion}" : Path.GetFileNameWithoutExtension(projectPath);

            // The PublishToolWindow shows the result, so only a failure shows a prompt
            if (exitCode == 0)
            {
                var status = $"Publish Succeeded : {package} ({stopwatch.Elapsed.TotalSeconds:0.0}s)";
                await WriteOutputAsync(status, cancellationToken);
                Log.Complete(status);
            }
            else
            {
                var status = $"Publish Failed : {package} (Exit Code {exitCode})";
                await WriteOutputAsync(status, cancellationToken);
                Log.Complete(status);
                await ShowMessageAsync($"TrakHound Package Publish Failed : {package}. See the TrakHound Publish window for details", PromptOptions.ErrorConfirm, cancellationToken);
            }
        }
    }
}
