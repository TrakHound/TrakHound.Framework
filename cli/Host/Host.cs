// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TrakHound.Packages;

namespace TrakHound.CLI.Host
{
    /// <summary>
    /// Creates a standalone host for a Package project: a project next to it (from the trakhound.host template) that runs the Package
    /// without an Instance, as a console app, Windows Service, systemd service, IIS application or container
    /// </summary>
    [CommandGroup("host", "Create a standalone host that runs a Package project without an Instance")]
    internal static class Host
    {
        private const string TemplateName = "trakhound.host";
        private const string DebugConfigurationFilename = "trakhound.debug.json";

        // The module classes the template's Program.cs uses, for each kind
        private static readonly Dictionary<string, (string BaseClass, string TemplateClass, string Code)> _kinds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["api"] = ("TrakHoundApiController", "Controller", "typeof({0})"),
            ["worker"] = ("TrakHoundWorker", "Worker", "AddTrakHoundWorker<{0}>()"),
            ["function"] = ("TrakHoundFunction", "Function", "RunTrakHoundFunction<{0}>()")
        };

        private static readonly JsonDocumentOptions _jsonOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };


        [Command("create", "Create a host project next to an Api, Worker or Function Package project (the Package project doesn't change)")]
        [CommandHelp(null, "th host create ./Contoso.Hello --api-key")]
        public static async Task<bool> Create(
            [CommandParameter("The path to the DotNet project directory (or .csproj file). If not specified, the current working directory is used")]
            string path = null,
            [CommandOption("The name of the host project. If not specified, the project's name followed by '.Host' is used")]
            string name = null,
            [CommandOption("The module class the host runs, with its namespace (ex. Contoso.Hello.Worker). If not specified, it is found in the project's source")]
            string className = null,
            [CommandOption("Api hosts: generate an API Key, add its hash to appsettings.json and print it (callers send it in the X-API-Key header)")]
            bool apiKey = false
            )
        {
            var projectPath = CliPath.Resolve(path);
            var projectDirectory = File.Exists(projectPath) ? Path.GetDirectoryName(projectPath) : projectPath;
            var projectFile = File.Exists(projectPath) ? projectPath : Directory.GetFiles(projectDirectory, "*.csproj").FirstOrDefault();
            if (projectFile == null)
            {
                Console.Error.WriteLine($"Error : No DotNet project (.csproj) Found : ({projectDirectory})");
                return false;
            }

            var information = TrakHoundPackage.ReadInformationFromFile(Path.Combine(projectDirectory, TrakHoundPackage.PackageConfigurationFilename));
            if (information == null)
            {
                Console.Error.WriteLine($"Error : Package Information File not Found : ({Path.Combine(projectDirectory, TrakHoundPackage.PackageConfigurationFilename)})");
                return false;
            }

            var kind = information.Category?.ToLower();
            if (kind == null || !_kinds.ContainsKey(kind))
            {
                Console.Error.WriteLine($"Error : Only Api, Worker and Function Packages can run standalone (the Package Category is '{information.Category}')");
                return false;
            }

            // The template references the Package project in the folder next to the host
            var moduleFolder = Path.GetFileName(projectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var parentDirectory = Path.GetDirectoryName(projectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(name)) name = $"{Path.GetFileNameWithoutExtension(projectFile)}.Host";
            var hostDirectory = Path.Combine(parentDirectory, name);
            if (Directory.Exists(hostDirectory) && Directory.EnumerateFileSystemEntries(hostDirectory).Any())
            {
                Console.Error.WriteLine($"Error : The host directory already exists : ({hostDirectory})");
                return false;
            }

            if (string.IsNullOrEmpty(className))
            {
                className = FindModuleClass(projectDirectory, _kinds[kind].BaseClass, out var error);
                if (className == null)
                {
                    Console.Error.WriteLine($"Error : {error} (set --class-name)");
                    return false;
                }
            }

            var project = File.ReadAllText(projectFile);
            var debugSettings = ReadDebugSettings(projectDirectory);
            var frameworkRoot = FindFrameworkRoot(projectDirectory, project);
            var trakhoundVersion = Regex.Match(project, "<PackageReference\\s+Include=\"TrakHound\\.[\\w.]+\"\\s+Version=\"([^\"]+)\"").Groups[1].Value;

            Console.WriteLine(string.Format("{0,-14} : {1}", "Package", $"{information.Id} ({kind})"));
            Console.WriteLine(string.Format("{0,-14} : {1}", "Module Class", className));
            Console.WriteLine(string.Format("{0,-14} : {1}", "Host", hostDirectory));
            Console.WriteLine();

            // Create the project from the template
            var arguments = new List<string> { "new", TemplateName, "-n", name, "-o", hostDirectory, "--module", moduleFolder, "--kind", kind, "--skipRestore" };
            if (!string.IsNullOrEmpty(trakhoundVersion)) arguments.AddRange(new[] { "--trakhoundVersion", trakhoundVersion });
            if (!await RunDotNet(arguments))
            {
                Console.Error.WriteLine($"Error : Unable to create the host from the '{TemplateName}' template. Install or update the templates with: dotnet new install TrakHound.DotNet.Templates");
                return false;
            }

            var hostProjectPath = Path.Combine(hostDirectory, $"{name}.csproj");
            var hostProject = File.ReadAllText(hostProjectPath);

            // The Package project's file may not be named after its folder
            hostProject = hostProject.Replace($"..\\{moduleFolder}\\{moduleFolder}.csproj", Path.GetRelativePath(hostDirectory, projectFile));

            // A Package project in the TrakHound Framework repository references the libraries as projects, so the host does too
            if (frameworkRoot != null)
            {
                hostProject = Regex.Replace(hostProject, "<PackageReference Include=\"(TrakHound\\.Hosting(?:\\.AspNetCore)?)\" Version=\"[^\"]*\" />", match =>
                {
                    var library = match.Groups[1].Value;
                    return $"<ProjectReference Include=\"{Path.GetRelativePath(hostDirectory, Path.Combine(frameworkRoot, "libraries", library, $"{library}.csproj"))}\" />";
                });
            }
            File.WriteAllText(hostProjectPath, hostProject);

            // Use the module class that was found (the template uses the class names of the Package templates)
            var programPath = Path.Combine(hostDirectory, "Program.cs");
            var code = _kinds[kind].Code;
            File.WriteAllText(programPath, File.ReadAllText(programPath).Replace(string.Format(code, _kinds[kind].TemplateClass), string.Format(code, className)));

            // The debug settings are the Development settings of the host
            if (debugSettings != null) WriteDevelopmentSettings(Path.Combine(hostDirectory, "appsettings.Development.json"), debugSettings.Value, kind);

            string key = null;
            if (apiKey && kind == "api") key = AddApiKey(Path.Combine(hostDirectory, "appsettings.json"));

            Console.WriteLine();
            Console.WriteLine($"Host Created : {hostDirectory}");
            if (debugSettings != null) Console.WriteLine($"The settings in {DebugConfigurationFilename} are in appsettings.Development.json");
            if (frameworkRoot != null) Console.WriteLine("The host references the TrakHound libraries as projects (the Package project does too)");
            if (key != null)
            {
                Console.WriteLine();
                Console.WriteLine($"API Key : {key}");
                Console.WriteLine("Callers send it in the X-API-Key header. Only its hash is kept (in appsettings.json), so copy it now");
            }
            else if (kind == "api")
            {
                Console.WriteLine("The host refuses every request until it has an API Key in \"TrakHound:ApiKeys\" (th host create --api-key generates one)");
            }

            Console.WriteLine();
            Console.WriteLine($"Run it : dotnet run --project \"{hostDirectory}\"");
            return true;
        }


        /// <summary>
        /// Finds the class that derives from the base class in the project's source (not built, so the project doesn't need to build first).
        /// Returns its name with its namespace
        /// </summary>
        internal static string FindModuleClass(string projectDirectory, string baseClass, out string error)
        {
            var classes = new List<string>();
            var classPattern = new Regex($@"^[ \t]*(?:public\s+)?(?:sealed\s+|partial\s+)*class\s+(\w+)\s*:\s*(?:[\w.]+\.)?{baseClass}\b", RegexOptions.Multiline);
            var namespacePattern = new Regex(@"^[ \t]*namespace\s+([\w.]+)", RegexOptions.Multiline);

            foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(projectDirectory, file);
                if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(o => o is "bin" or "obj")) continue;

                var source = File.ReadAllText(file);
                foreach (Match match in classPattern.Matches(source))
                {
                    // The namespace declared before the class (block or file scoped), or none
                    var ns = namespacePattern.Matches(source).Where(o => o.Index < match.Index).LastOrDefault()?.Groups[1].Value;
                    classes.Add(!string.IsNullOrEmpty(ns) ? $"{ns}.{match.Groups[1].Value}" : match.Groups[1].Value);
                }
            }

            error = null;
            classes = classes.Distinct().OrderBy(o => o).ToList();
            if (classes.Count == 0)
            {
                error = $"No public class that derives from {baseClass} was Found";
                return null;
            }

            // An Api host only uses the class to find the assembly, so any Controller will do
            if (classes.Count > 1 && baseClass != "TrakHoundApiController")
            {
                error = $"More than one class derives from {baseClass} : {string.Join(", ", classes)}";
                return null;
            }

            return classes[0];
        }

        /// <summary>
        /// The root of the TrakHound Framework repository when the project references its libraries (or debug hosts) as projects
        /// </summary>
        private static string FindFrameworkRoot(string projectDirectory, string project)
        {
            foreach (Match match in Regex.Matches(project, "<ProjectReference\\s+Include=\"([^\"]+)\""))
            {
                var referencePath = Path.GetFullPath(Path.Combine(projectDirectory, match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar)));
                var referenceName = Path.GetFileNameWithoutExtension(referencePath);
                if (referenceName == "TrakHound.Common" || referenceName.StartsWith("TrakHound.Debug"))
                {
                    var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(referencePath)));
                    if (root != null && File.Exists(Path.Combine(root, "libraries", "TrakHound.Hosting", "TrakHound.Hosting.csproj"))) return root;
                }
            }
            return null;
        }

        private static JsonElement? ReadDebugSettings(string projectDirectory)
        {
            var debugPath = Path.Combine(projectDirectory, DebugConfigurationFilename);
            if (!File.Exists(debugPath)) return null;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(debugPath), _jsonOptions);
                return document.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Warning : Unable to read ({debugPath}) : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Writes the settings of trakhound.debug.json as the "TrakHound" section (the same names; Parameter values become strings)
        /// </summary>
        internal static void WriteDevelopmentSettings(string path, JsonElement debugSettings, string kind)
        {
            var section = new JsonObject();
            foreach (var property in new[] { "instanceUrl", "apiKey", "bearerToken", "configurationId", "volumePath" })
            {
                if (TryGetProperty(debugSettings, property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()))
                {
                    section[char.ToUpper(property[0]) + property.Substring(1)] = value.GetString();
                }
            }

            foreach (var property in kind == "function" ? new[] { "parameters", "input" } : new[] { "parameters" })
            {
                if (TryGetProperty(debugSettings, property, out var values) && values.ValueKind == JsonValueKind.Object)
                {
                    var converted = new JsonObject();
                    foreach (var value in values.EnumerateObject())
                    {
                        if (value.Value.ValueKind == JsonValueKind.Null) continue;
                        converted[value.Name] = value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString() : value.Value.GetRawText();
                    }
                    if (converted.Count > 0) section[char.ToUpper(property[0]) + property.Substring(1)] = converted;
                }
            }

            var root = new JsonObject { ["TrakHound"] = section };
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        }

        /// <summary>
        /// Generates an API Key and adds its hash to the "ApiKeys" in appsettings.json. Returns the Key
        /// </summary>
        private static string AddApiKey(string appSettingsPath)
        {
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
            var entry = $"{{ \"Name\": \"default\", \"Hash\": \"{hash}\" }}";

            // Replace the template's example (appsettings.json has comments, so it's edited as text)
            var appSettings = File.ReadAllText(appSettingsPath);
            var example = Regex.Match(appSettings, @"//\s*\{\s*""Name"":\s*""caller-name"",\s*""Hash"":\s*""""\s*\}");
            if (example.Success)
            {
                File.WriteAllText(appSettingsPath, appSettings.Remove(example.Index, example.Length).Insert(example.Index, entry));
            }
            else
            {
                Console.Error.WriteLine($"Warning : Add the API Key's hash to \"TrakHound:ApiKeys\" in appsettings.json : {entry}");
            }

            return key;
        }

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }

        private static async Task<bool> RunDotNet(IEnumerable<string> arguments)
        {
            var startInfo = new ProcessStartInfo("dotnet");
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            startInfo.UseShellExecute = false;

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
    }
}
