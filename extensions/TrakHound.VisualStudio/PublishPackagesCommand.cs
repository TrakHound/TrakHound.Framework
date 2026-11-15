// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.ProjectSystem.Query;
using Microsoft.VisualStudio.RpcContracts.Notifications;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// Builds and Publishes the Projects listed in a Build File (trakhound.build.json) using the TrakHound CLI ('th packages build'),
    /// after selecting which of them to include
    /// </summary>
    [VisualStudioContribution]
    internal class PublishPackagesCommand : PublishCommand
    {
        // Written by the CLI after the Build Summary : "3 Succeeded, 1 Failed, 2 Not Published"
        private static readonly Regex _summaryRegex = new(@"^(?<succeeded>\d+) Succeeded, \d+ Failed");


        public PublishPackagesCommand(PublishLog log) : base(log) { }


        public override CommandConfiguration CommandConfiguration => new("%TrakHound.VisualStudio.PublishPackagesCommand.DisplayName%")
        {
            Icon = new(ImageMoniker.KnownValues.Upload, IconSettings.IconAndText),
            Placements =
            [
                // Solution Explorer Project context menu, below Publish TrakHound Package
                CommandPlacement.VsctParent(new Guid("{d309f791-903f-11d0-9efc-00a0c911004f}"), id: 518, priority: 0x0101),
                CommandPlacement.KnownPlacements.ToolsMenu,
            ],
            EnabledWhen = ActivationConstraint.SolutionState(SolutionState.FullyLoaded),
        };


        public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
        {
            if (!await TryStartAsync(cancellationToken)) return;

            try
            {
                var solutionDirectory = await GetSolutionDirectoryAsync(context, cancellationToken);
                if (solutionDirectory == null)
                {
                    await ShowMessageAsync("Open a Solution to Publish TrakHound Packages", PromptOptions.OK, cancellationToken);
                    return;
                }

                var projectPath = await GetProjectPathAsync(context, cancellationToken);

                var buildFilePath = await SelectBuildFileAsync(solutionDirectory, Path.GetDirectoryName(projectPath), cancellationToken);
                if (buildFilePath == null) return;

                List<BuildFileProject> projects;
                try
                {
                    projects = BuildFile.ReadProjects(buildFilePath);
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync($"Unable to read {buildFilePath} : {ex.Message}", PromptOptions.ErrorConfirm, cancellationToken);
                    return;
                }

                if (projects.Count == 0)
                {
                    await ShowMessageAsync($"No Projects are listed in {buildFilePath}", PromptOptions.ErrorConfirm, cancellationToken);
                    return;
                }

                var data = new PublishPackagesDialogData(buildFilePath, projects);
                var result = await this.Extensibility.Shell().ShowDialogAsync(new PublishPackagesDialog(data), "Publish TrakHound Packages", DialogOption.OKCancel, cancellationToken);
                if (result != DialogResult.OK) return;

                var selected = data.Projects.Where(o => o.IsSelected).Select(o => o.Project).ToList();
                if (selected.Count == 0) return;

                var cliPath = await FindCliAsync(cancellationToken);
                if (cliPath == null) return;

                await PublishAsync(cliPath, buildFilePath, selected, selected.Count == projects.Count, cancellationToken);
            }
            finally
            {
                Stop();
            }
        }

        private async Task PublishAsync(string cliPath, string buildFilePath, List<BuildFileProject> projects, bool all, CancellationToken cancellationToken)
        {
            var arguments = new List<string> { "packages", "build", buildFilePath };
            if (!all)
            {
                foreach (var project in projects)
                {
                    // The full path, since two Projects can have the same directory name
                    arguments.Add("--project");
                    arguments.Add(project.Directory);
                }
            }

            string summary = null;
            var stopwatch = Stopwatch.StartNew();

            var exitCode = await RunCliAsync(
                cliPath,
                Path.GetDirectoryName(buildFilePath),
                arguments,
                $"Publish TrakHound Packages : {buildFilePath}",
                $"Publishing {projects.Count} Project(s)...",
                line =>
                {
                    if (_summaryRegex.IsMatch(line)) summary = line;
                },
                cancellationToken);
            if (exitCode == null) return;

            // The PublishToolWindow shows the result, so only a failure shows a prompt
            if (exitCode == 0)
            {
                var status = $"Publish Succeeded : {summary ?? $"{projects.Count} Project(s)"} ({stopwatch.Elapsed.TotalSeconds:0.0}s)";
                await WriteOutputAsync(status, cancellationToken);
                Log.Complete(status);
            }
            else
            {
                var status = $"Publish Failed : {summary ?? $"Exit Code {exitCode}"}";
                await WriteOutputAsync(status, cancellationToken);
                Log.Complete(status);
                await ShowMessageAsync($"TrakHound Packages Publish Failed : {summary ?? $"Exit Code {exitCode}"}. See the TrakHound Publish window for details", PromptOptions.ErrorConfirm, cancellationToken);
            }
        }

        /// <summary>
        /// Find the Build Files for the Solution and, if there is more than one, ask which to use.
        /// Returns null (after showing a message) if there are none, or if the prompt is dismissed
        /// </summary>
        /// <param name="projectDirectory">The directory of the selected Project (if any). A Build File that lists it is used without asking</param>
        private async Task<string> SelectBuildFileAsync(string solutionDirectory, string projectDirectory, CancellationToken cancellationToken)
        {
            var buildFilePaths = BuildFile.Find(solutionDirectory);
            if (buildFilePaths.Count == 0)
            {
                await ShowMessageAsync($"No Build File ({BuildFile.Filename}) was found in or near {solutionDirectory}. A Build File lists the Projects to Publish together (see the TrakHound CLI documentation)", PromptOptions.OK, cancellationToken);
                return null;
            }

            if (buildFilePaths.Count == 1) return buildFilePaths[0];

            if (!string.IsNullOrEmpty(projectDirectory))
            {
                var listedIn = buildFilePaths.Where(o => ListsProject(o, projectDirectory)).ToList();
                if (listedIn.Count == 1) return listedIn[0];
            }

            var options = new PromptOptions<int>
            {
                DismissedReturns = -1,
                DefaultChoiceIndex = 0,
            };
            for (var i = 0; i < buildFilePaths.Count; i++)
            {
                // Shown relative to the Solution (ex. "modules\trakhound.build.json")
                options.Choices.Add(Path.GetRelativePath(solutionDirectory, buildFilePaths[i]), i);
            }

            var index = await this.Extensibility.Shell().ShowPromptAsync("Select the Build File to Publish", options, cancellationToken);
            return index >= 0 && index < buildFilePaths.Count ? buildFilePaths[index] : null;
        }

        private static bool ListsProject(string buildFilePath, string projectDirectory)
        {
            try
            {
                var directory = projectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return BuildFile.ReadProjects(buildFilePath).Any(o => string.Equals(o.Directory, directory, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                // A Build File that can't be read is reported if it is selected
                return false;
            }
        }

        private async Task<string> GetSolutionDirectoryAsync(IClientContext context, CancellationToken cancellationToken)
        {
            var solutions = await this.Extensibility.Workspaces().QuerySolutionAsync(solution => solution.With(s => s.Directory), cancellationToken);
            var directory = solutions.FirstOrDefault()?.Directory;
            if (!string.IsNullOrEmpty(directory)) return directory;

            // A Project opened without a Solution file
            var project = await context.GetActiveProjectAsync(project => project.With(p => p.Path), cancellationToken);
            return project?.Path != null ? Path.GetDirectoryName(project.Path) : null;
        }
    }
}
