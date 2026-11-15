// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Win32;

namespace TrakHound.VisualStudio
{
    /// <summary>
    /// Locates the TrakHound CLI executable (th.exe)
    /// </summary>
    internal static class TrakHoundCli
    {
        public const string ExecutableName = "th.exe";

        // The AppId of the CLI Installer. Inno Setup appends "_is1"
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7E85AB51-2024-4F0B-A8BF-288D9EE48D34}_is1";


        public static string Find()
        {
            // Read PATH from the registry as well as the process, since the process PATH is inherited when Visual Studio
            // starts and won't include the CLI if it was installed (or added to PATH) while Visual Studio was running
            var paths = new List<string>();
            AddPaths(paths, Environment.GetEnvironmentVariable("PATH"));
            AddPaths(paths, Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
            AddPaths(paths, Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));

            foreach (var path in paths)
            {
                var filePath = GetExecutablePath(path);
                if (filePath != null) return filePath;
            }

            // The installer doesn't add the CLI to PATH unless the 'Add to PATH variable' task is selected
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = baseKey.OpenSubKey(UninstallKey);

                    var filePath = GetExecutablePath(key?.GetValue("InstallLocation") as string);
                    if (filePath != null) return filePath;
                }
                catch { }
            }

            // Default install directory
            return GetExecutablePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TrakHound", "Cli"));
        }

        /// <summary>
        /// Remove the .NET runtime settings that the Visual Studio extension host sets for itself. The host sets DOTNET_ROOT to
        /// Visual Studio's private .NET 8 runtime (with DOTNET_MULTILEVEL_LOOKUP=0), so the CLI (and the 'dotnet' builds it runs)
        /// would fail with "You must install or update .NET to run this application"
        /// </summary>
        public static void ConfigureEnvironment(IDictionary<string, string> environment)
        {
            foreach (var name in environment.Keys.Where(IsRuntimeVariable).ToList())
            {
                environment.Remove(name);
            }

            // Keep the variables that are actually configured for the Machine or User
            foreach (var target in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
            {
                foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables(target))
                {
                    var name = (string)variable.Key;
                    if (IsRuntimeVariable(name)) environment[name] = (string)variable.Value;
                }
            }
        }

        private static bool IsRuntimeVariable(string name)
        {
            return name.StartsWith("DOTNET_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("COMPlus_", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddPaths(List<string> paths, string value)
        {
            if (string.IsNullOrEmpty(value)) return;

            foreach (var path in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var expanded = Environment.ExpandEnvironmentVariables(path.Trim('"'));
                if (!paths.Contains(expanded, StringComparer.OrdinalIgnoreCase)) paths.Add(expanded);
            }
        }

        private static string GetExecutablePath(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return null;

            try
            {
                var filePath = Path.Combine(directory, ExecutableName);
                if (File.Exists(filePath)) return filePath;
            }
            catch { }

            return null;
        }
    }
}
