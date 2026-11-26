// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Documents;
using Microsoft.VisualStudio.Extensibility.Shell;
using Microsoft.VisualStudio.ProjectSystem.Query;
using System.Diagnostics;
using System.Text;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// The base of the commands that Publish using the TrakHound CLI: runs the CLI and shows its output in the PublishToolWindow and the TrakHound Output pane
    /// </summary>
    internal abstract class PublishCommand : Command
    {
        // TrakHoundPackage.PackageConfigurationFilename (libraries\TrakHound.Common\Packages\TrakHoundPackage.cs)
        protected const string PackageConfigurationFilename = "trakhound.package.json";

        private const string OutputChannelName = "TrakHound";

        // Shared by the Publish commands, so they write to the same Output pane and only one Publish runs at a time
        private static readonly SemaphoreSlim _outputLock = new(1, 1);
        private static OutputChannel _outputChannel;
        private static int _running;


        protected PublishLog Log { get; }


        protected PublishCommand(PublishLog log)
        {
            Log = log;
        }


        /// <summary>
        /// Only allow one Publish at a time (a second would build the same version from the same commit, which can only be published once).
        /// Returns false (and shows a message) if one is running. Call <see cref="Stop"/> when finished
        /// </summary>
        protected async Task<bool> TryStartAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                await ShowMessageAsync("A TrakHound Package is already being Published", PromptOptions.OK, cancellationToken);
                return false;
            }

            return true;
        }

        protected static void Stop() => Interlocked.Exchange(ref _running, 0);

        /// <summary>
        /// Find the TrakHound CLI. Returns null (and shows a message) if it isn't installed
        /// </summary>
        protected async Task<string> FindCliAsync(CancellationToken cancellationToken)
        {
            var cliPath = TrakHoundCli.Find();
            if (cliPath == null)
            {
                await ShowMessageAsync($"The TrakHound CLI ({TrakHoundCli.ExecutableName}) was not found. Install the TrakHound CLI or add it to the PATH variable", PromptOptions.ErrorConfirm, cancellationToken);
            }

            return cliPath;
        }

        /// <summary>
        /// Run the TrakHound CLI and write its output to the PublishToolWindow and the Output pane.
        /// Returns the Exit Code, or null if it was cancelled or couldn't be run (which is already reported)
        /// </summary>
        /// <param name="title">What is published (ex. the project path), written above the output</param>
        /// <param name="status">The status shown while the CLI runs</param>
        /// <param name="onLine">Called with each line written to the CLI's Output stream</param>
        protected async Task<int?> RunCliAsync(string cliPath, string workingDirectory, IEnumerable<string> arguments, string title, string status, Action<string> onLine, CancellationToken cancellationToken)
        {
            // The CLI builds the projects from disk
            await this.Extensibility.Documents().SaveAllAsync(cancellationToken);

            // The Output Window pane can't be shown by an (out-of-process) extension, so the output is also shown in the PublishToolWindow
            Log.Start(status);
            await this.Extensibility.Shell().ShowToolWindowAsync<PublishToolWindow>(activate: true, cancellationToken);

            var startInfo = new ProcessStartInfo(cliPath)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

            TrakHoundCli.ConfigureEnvironment(startInfo.Environment);

            await WriteOutputAsync("-----------------------------------------------------", cancellationToken);
            await WriteOutputAsync(title, cancellationToken);
            await WriteOutputAsync($"{cliPath} {string.Join(" ", startInfo.ArgumentList.Select(o => o.Contains(' ') ? $"\"{o}\"" : o))}", cancellationToken);
            await WriteOutputAsync("-----------------------------------------------------", cancellationToken);

            try
            {
                using var process = Process.Start(startInfo);

                // The CLI never reads input, but close it so that nothing can wait on it
                process.StandardInput.Close();

                using var registration = cancellationToken.Register(() =>
                {
                    try { process.Kill(true); } catch { }
                });

                var outputTask = ReadOutputAsync(process.StandardOutput, onLine, cancellationToken);
                var errorTask = ReadOutputAsync(process.StandardError, null, cancellationToken);

                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(outputTask, errorTask);

                return process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                await WriteOutputAsync("Publish Cancelled", CancellationToken.None);
                Log.Complete("Publish Cancelled");
                return null;
            }
            catch (Exception ex)
            {
                await WriteOutputAsync($"Error : Unable to run the TrakHound CLI : {ex.Message}", cancellationToken);
                Log.Complete($"Publish Failed : Unable to run the TrakHound CLI : {ex.Message}");
                await ShowMessageAsync($"Unable to run the TrakHound CLI : {ex.Message}", PromptOptions.ErrorConfirm, cancellationToken);
                return null;
            }
        }

        private async Task ReadOutputAsync(StreamReader reader, Action<string> onLine, CancellationToken cancellationToken)
        {
            string line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                onLine?.Invoke(line);
                await WriteOutputAsync(line, cancellationToken);
            }
        }

        protected async Task WriteOutputAsync(string line, CancellationToken cancellationToken)
        {
            // Lines from the CLI's Output and Error streams are written from separate tasks
            await _outputLock.WaitAsync(cancellationToken);
            try
            {
                Log.AppendLine(line);

                _outputChannel ??= await this.Extensibility.Views().Output.CreateOutputChannelAsync(OutputChannelName, cancellationToken);
                await _outputChannel.Writer.WriteLineAsync(line);
            }
            finally
            {
                _outputLock.Release();
            }
        }

        protected Task<T> ShowMessageAsync<T>(string message, PromptOptions<T> options, CancellationToken cancellationToken) where T : struct
        {
            return this.Extensibility.Shell().ShowPromptAsync(message, options, cancellationToken);
        }

        /// <summary>
        /// Ask which Publish Target to publish to, when the Publish Settings files (trakhound.package.publish.json) nearest to the Package
        /// directories have more than one Target in common. Returns Continue = false if the prompt is dismissed. Target is null when nothing
        /// needs to be passed (no file, a file that can't be read, which the CLI reports, or no Target in common, so each one's defaultTarget is used)
        /// </summary>
        protected async Task<(bool Continue, string Target)> SelectPublishTargetAsync(IEnumerable<string> packageDirectories, CancellationToken cancellationToken)
        {
            var files = new List<PublishTargetsFile>();
            foreach (var path in packageDirectories.Select(PublishTargets.Find).Where(o => o != null).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    files.Add(PublishTargets.Read(path));
                }
                catch
                {
                    return (true, null);
                }
            }
            if (files.Count == 0) return (true, null);

            // Only the Targets every file has, since the CLI publishes each Package to the Target in its own file
            var targets = files[0].Targets.Where(name => files.All(file => file.Targets.Contains(name, StringComparer.OrdinalIgnoreCase))).ToList();
            if (targets.Count == 0) return (true, null);
            if (targets.Count == 1) return (true, targets[0]);

            // The default is preselected when the files agree on it
            var defaultTarget = files.Select(o => o.DefaultTarget).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 ? files[0].DefaultTarget : null;
            var defaultIndex = targets.FindIndex(o => string.Equals(o, defaultTarget, StringComparison.OrdinalIgnoreCase));

            var options = new PromptOptions<int>
            {
                DismissedReturns = -1,
                DefaultChoiceIndex = Math.Max(defaultIndex, 0),
            };
            for (var i = 0; i < targets.Count; i++)
            {
                options.Choices.Add(i == defaultIndex ? $"{targets[i]} (default)" : targets[i], i);
            }

            var source = files.Count == 1 ? files[0].Path : $"{files.Count} {PublishTargets.Filename} files";
            var index = await this.Extensibility.Shell().ShowPromptAsync($"Select the Publish Target ({source})", options, cancellationToken);
            return index >= 0 && index < targets.Count ? (true, targets[index]) : (false, null);
        }

        /// <summary>
        /// Get the Project selected in Solution Explorer, or the Active Project (ex. the project of the active document)
        /// </summary>
        protected static async Task<string> GetProjectPathAsync(IClientContext context, CancellationToken cancellationToken)
        {
            var selectedPath = await context.GetSelectedPathAsync(cancellationToken);
            if (selectedPath != null && selectedPath.IsFile && IsProjectFile(selectedPath.LocalPath)) return selectedPath.LocalPath;

            var project = await context.GetActiveProjectAsync(project => project.With(p => p.Path), cancellationToken);
            if (project?.Path != null && IsProjectFile(project.Path)) return project.Path;

            return null;
        }

        protected static bool IsProjectFile(string path) => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }
}
