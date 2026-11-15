// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;
using System.Text.RegularExpressions;
using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Configurations;
using TrakHound.Functions;
using TrakHound.Groups;
using TrakHound.Instances;
using TrakHound.Packages;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Workers;
using TrakHound.Zones;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.CLI.Config
{
    /// <summary>
    /// Works on the Configuration files of a Profile directory ("[path]/config/[profile]/[category]/[id].yaml") through TrakHoundConfigurationProfile,
    /// so files are read and saved the same way the Instance does (secrets encrypted, saved to a temporary file and moved into place).
    /// A running Instance watches these directories and applies the changes
    /// </summary>
    [CommandGroup("config", "List, Add, Change, Remove and Validate the Configurations and Variables of a Configuration Profile")]
    internal static class Config
    {
        private const string PathDescription = "The root directory (ex. the TrakHound Instance directory) that contains the 'config' directory. If not specified, the current working directory is used";
        private const string ProfileDescription = "The Configuration Profile";
        private const string CategoryDescription = "The Configuration Category (api, app, function, identity, router, service, worker, group or zone)";

        // Properties that identify the Configuration (or aren't stored), so they can't be changed with --property
        private static readonly string[] _reservedProperties = new string[] { "Id", "Category", "Path", "Hash", "Parameters" };

        // The Configuration Categories that can be Zone Members (ex. "worker:my-worker")
        private static readonly HashSet<string> _memberCategories = new HashSet<string>(StringComparer.Ordinal)
        {
            TrakHoundApiConfiguration.ConfigurationCategory,
            TrakHoundAppConfiguration.ConfigurationCategory,
            TrakHoundFunctionConfiguration.ConfigurationCategory,
            TrakHoundWorkerConfiguration.ConfigurationCategory
        };

        // $${...} (escaped) or ${name} / ${env:NAME}, as TrakHoundConfigurationVariables replaces them
        private static readonly Regex _variableReferenceRegex = new Regex(@"\$\$\{|\$\{(env:)?([A-Za-z0-9_.\-]+)\}", RegexOptions.Compiled);

        // The Categories the Instance loads, and the Type each is read as
        private static readonly Dictionary<string, ConfigurationCategory> _categories = new ConfigurationCategory[]
        {
            ConfigurationCategory.Create<TrakHoundApiConfiguration>(TrakHoundApiConfiguration.ConfigurationCategory),
            ConfigurationCategory.Create<TrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory),
            ConfigurationCategory.Create<TrakHoundFunctionConfiguration>(TrakHoundFunctionConfiguration.ConfigurationCategory),
            ConfigurationCategory.Create<TrakHoundIdentityProviderConfiguration>(TrakHoundIdentityProviderConfiguration.ConfigurationCategory),
            ConfigurationCategory.Create<TrakHoundServiceRouterConfiguration>(TrakHoundServiceRouterConfiguration.ConfigurationCategory),
            ConfigurationCategory.Create<TrakHoundServiceConfiguration>(TrakHoundServiceConfiguration.ConfigurationCategory),
            ConfigurationCategory.Create<TrakHoundWorkerConfiguration>(TrakHoundWorkerConfiguration.ConfigurationCategory),
            ConfigurationCategory.Create<TrakHoundGroupConfiguration>(TrakHoundGroupConfiguration.ConfigurationCategory),
            ConfigurationCategory.Create<TrakHoundZoneConfiguration>(TrakHoundZoneConfiguration.ConfigurationCategory)
        }.ToDictionary(o => o.Name, StringComparer.OrdinalIgnoreCase);


        [Command("list", "List the Configurations in a Configuration Profile")]
        [CommandHelp(null, "th config list ./instance --category worker")]
        public static bool List(
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default,
            [CommandOption("The Category of the Configurations to list. If not specified, every Category is listed", "-c")]
            string category = null
            )
        {
            var categories = category != null ? new[] { GetCategory(category) } : _categories.Values.OrderBy(o => o.Name).ToArray();
            var configurationProfile = OpenProfile(path, profile, false, out var errors);
            if (configurationProfile == null) return false;

            var rows = new List<string[]>();
            foreach (var configurationCategory in categories)
            {
                foreach (var configuration in configurationCategory.Read(configurationProfile).OrderBy(o => o.Id))
                {
                    rows.Add(new string[]
                    {
                        configurationCategory.Name,
                        configuration.Id,
                        GetPropertyValue(configuration, "Name"),
                        GetPropertyValue(configuration, "PackageId"),
                        System.IO.Path.GetRelativePath(configurationProfile.Path, configuration.Path)
                    });
                }
            }

            if (rows.Count > 0)
            {
                PrintTable(new string[] { "Category", "ID", "Name", "Package", "File" }, rows);
            }
            else
            {
                Console.WriteLine($"No Configurations Found in ({configurationProfile.Path})");
            }

            return errors.Count == 0;
        }

        [Command("get", "Print the YAML of a Configuration")]
        [CommandHelp(null, "th config get worker my-worker ./instance")]
        public static bool Get(
            [CommandParameter(CategoryDescription)]
            string category,
            [CommandParameter("The ID of the Configuration")]
            string id,
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            var configurationCategory = GetCategory(category);
            var configurationProfile = OpenProfile(path, profile, false, out _);
            if (configurationProfile == null) return false;

            var configuration = Find(configurationProfile, configurationCategory, id);
            if (configuration == null)
            {
                Console.Error.WriteLine($"Error : Configuration not Found : ({configurationCategory.Name} : {id})");
                return false;
            }

            // The file as stored (secret Parameters are encrypted when the file is read)
            Console.Write(File.ReadAllText(configuration.Path));
            return true;
        }

        [Command("add", "Add a Configuration from a YAML file, or replace the Configuration with the same ID")]
        [CommandHelp(null, "th config add worker ./my-worker.yaml ./instance")]
        public static bool Add(
            [CommandParameter(CategoryDescription)]
            string category,
            [CommandParameter("The path of the YAML file to read the Configuration from")]
            string file,
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default,
            [CommandOption("The ID of the Configuration. If not specified, the 'id' in the file is used (or the filename if the file has none)")]
            string id = null
            )
        {
            var configurationCategory = GetCategory(category);
            var filePath = CliPath.Resolve(file);

            if (!File.Exists(filePath))
            {
                Console.Error.WriteLine($"Error : File not Found : ({filePath})");
                return false;
            }

            ITrakHoundConfiguration configuration;
            try
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                configuration = (ITrakHoundConfiguration)deserializer.Deserialize(File.ReadAllText(filePath), configurationCategory.Type);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error : Unable to read a {configurationCategory.Name} Configuration from ({filePath}) : {ex.Message}");
                return false;
            }

            if (configuration == null)
            {
                Console.Error.WriteLine($"Error : The file is empty : ({filePath})");
                return false;
            }

            if (!string.IsNullOrEmpty(id)) SetId(configuration, id);
            else if (string.IsNullOrEmpty(configuration.Id)) SetId(configuration, System.IO.Path.GetFileNameWithoutExtension(filePath));

            var configurationProfile = OpenProfile(path, profile, true, out var errors);
            if (configurationProfile == null) return false;

            var existing = Find(configurationProfile, configurationCategory, configuration.Id);
            configuration.Path = existing?.Path;

            return Save(configurationProfile, configuration, existing != null, errors);
        }

        [Command("set", "Change the Parameters or Properties of a Configuration (it is created if it doesn't exist)")]
        [CommandHelp(null, "th config set worker my-worker ./instance --property packageId=MyCompany.MyWorker --property packageVersion=* --param interval=5000")]
        public static bool Set(
            [CommandParameter(CategoryDescription)]
            string category,
            [CommandParameter("The ID of the Configuration")]
            string id,
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default,
            [CommandOption("A Parameter to set, as name=value. Can be repeated")]
            string[] param = null,
            [CommandOption("The name of a Parameter to remove. Can be repeated")]
            string[] removeParam = null,
            [CommandOption("A Property to set, as name=value (ex. packageId=MyCompany.MyWorker). Lists are comma separated. Can be repeated")]
            string[] property = null
            )
        {
            var configurationCategory = GetCategory(category);

            if (param.IsNullOrEmpty() && removeParam.IsNullOrEmpty() && property.IsNullOrEmpty())
            {
                throw new CommandException("Nothing to set. Specify --param, --remove-param or --property");
            }

            var configurationProfile = OpenProfile(path, profile, true, out var errors);
            if (configurationProfile == null) return false;

            var configuration = Find(configurationProfile, configurationCategory, id);
            var exists = configuration != null;
            if (!exists)
            {
                configuration = (ITrakHoundConfiguration)Activator.CreateInstance(configurationCategory.Type);
                SetId(configuration, id);
            }

            if (!property.IsNullOrEmpty())
            {
                foreach (var value in property)
                {
                    var (name, propertyValue) = SplitNameValue(value, "--property");
                    SetProperty(configuration, name, propertyValue);
                }
            }

            if (!param.IsNullOrEmpty() || !removeParam.IsNullOrEmpty())
            {
                var parametersProperty = configuration.GetType().GetProperty("Parameters");
                if (parametersProperty?.PropertyType != typeof(Dictionary<string, object>))
                {
                    throw new CommandException($"{configurationCategory.Name} Configurations have no Parameters");
                }

                var parameters = (Dictionary<string, object>)parametersProperty.GetValue(configuration);
                if (parameters == null)
                {
                    parameters = new Dictionary<string, object>();
                    parametersProperty.SetValue(configuration, parameters);
                }

                if (!param.IsNullOrEmpty())
                {
                    foreach (var value in param)
                    {
                        // Stored as strings, the same as Parameters read from YAML
                        var (name, parameterValue) = SplitNameValue(value, "--param");
                        parameters[name] = parameterValue;
                    }
                }

                if (!removeParam.IsNullOrEmpty())
                {
                    foreach (var name in removeParam)
                    {
                        if (!parameters.Remove(name)) Console.WriteLine($"Warning : Parameter not Found : {name}");
                    }
                }
            }

            return Save(configurationProfile, configuration, exists, errors);
        }

        [Command("remove", "Remove a Configuration")]
        [CommandHelp(null, "th config remove worker my-worker ./instance")]
        public static bool Remove(
            [CommandParameter(CategoryDescription)]
            string category,
            [CommandParameter("The ID of the Configuration")]
            string id,
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            var configurationCategory = GetCategory(category);
            var configurationProfile = OpenProfile(path, profile, false, out _);
            if (configurationProfile == null) return false;

            var configuration = Find(configurationProfile, configurationCategory, id);
            if (configuration == null)
            {
                Console.Error.WriteLine($"Error : Configuration not Found : ({configurationCategory.Name} : {id})");
                return false;
            }

            Console.Write($"Removing Configuration ({configurationCategory.Name} : {configuration.Id}) from ({configuration.Path})..");
            File.Delete(configuration.Path);
            Console.WriteLine("Done");

            return true;
        }


        [Command("validate", "Check a Configuration Profile for problems that stop Configurations from loading")]
        [CommandHelp(null, "th config validate ./instance")]
        public static bool Validate(
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default,
            [CommandOption("The directory the Packages are installed in. If not specified, the 'packages' subdirectory of the root directory is used")]
            string packagePath = null
            )
        {
            var configurationProfile = OpenProfile(path, profile, false, out var profileErrors);
            if (configurationProfile == null) return false;

            var errorCount = 0;
            var warningCount = 0;
            void Error(string message) { errorCount++; Console.WriteLine($"Error : {message}"); }
            void Warning(string message) { warningCount++; Console.WriteLine($"Warning : {message}"); }

            // Files that can't be read (or have no Id) are reported by the Profile
            var configurations = _categories.Values.OrderBy(o => o.Name).SelectMany(o => o.Read(configurationProfile)).ToList();

            foreach (var duplicate in configurations.GroupBy(o => (o.Category, o.Id)).Where(o => o.Count() > 1))
            {
                Error($"{duplicate.Key.Category} : {duplicate.Key.Id} : Duplicate ID in {string.Join(", ", duplicate.Select(o => $"({o.Path})"))}");
            }

            var packagesPath = !string.IsNullOrEmpty(packagePath) ? CliPath.Resolve(packagePath) : Packages.Packages.GetPackagesPath(CliPath.Resolve(path));
            var installedPackages = TrakHoundPackage.GetInstalled(packagesPath)?.ToList() ?? new List<TrakHoundPackage>();

            // A Router ID can be a Router's ID or Name, or a Service ID (the Service's direct Router)
            var routerKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var configuration in configurations)
            {
                if (configuration.Category == TrakHoundServiceRouterConfiguration.ConfigurationCategory)
                {
                    routerKeys.Add(configuration.Id);
                    var name = GetPropertyValue(configuration, "Name");
                    if (!string.IsNullOrEmpty(name)) routerKeys.Add(name);
                }
                else if (configuration.Category == TrakHoundServiceConfiguration.ConfigurationCategory)
                {
                    routerKeys.Add(configuration.Id);
                }
            }

            var variables = TrakHoundConfigurationVariables.Read(configurationProfile.Path);
            var keyPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(configurationProfile.Path)), TrakHoundSecrets.KeyFilename);

            foreach (var variable in variables)
            {
                if (TrakHoundSecrets.IsEncrypted(variable.Value) && TrakHoundSecrets.Decrypt(variable.Value) == null)
                {
                    Error($"Variable {variable.Key} : Can't be decrypted with the key in ({keyPath})");
                }
            }

            // A duplicate is reported once (above), so only the first file with each ID is checked
            foreach (var configuration in configurations.GroupBy(o => (o.Category, o.Id)).Select(o => o.First()))
            {
                var label = $"{configuration.Category} : {configuration.Id}";

                if (configuration.GetType().GetProperty("PackageId") != null)
                {
                    var packageId = GetPropertyValue(configuration, "PackageId");
                    var packageVersion = GetPropertyValue(configuration, "PackageVersion");

                    if (string.IsNullOrEmpty(packageId))
                    {
                        Error($"{label} : No packageId");
                    }
                    else if (string.IsNullOrEmpty(packageVersion))
                    {
                        // The Module Manager needs a Version to resolve
                        Error($"{label} : No packageVersion, so the Instance doesn't load it. Use '*' for the latest installed version");
                    }
                    else
                    {
                        var installedVersions = installedPackages.Where(o => string.Equals(o.Id, packageId, StringComparison.OrdinalIgnoreCase)).Select(o => o.Version).ToList();
                        if (installedVersions.Count == 0)
                        {
                            Error($"{label} : Package {packageId} isn't installed in ({packagesPath})");
                        }
                        else if (!installedVersions.Any(o => TrakHoundPackageDependency.IsSatisfiedBy(new TrakHoundPackageDependency(packageId, packageVersion), o)))
                        {
                            Error($"{label} : No installed version of {packageId} matches {packageVersion} (installed : {string.Join(", ", installedVersions)})");
                        }
                    }
                }

                var routerId = GetPropertyValue(configuration, "RouterId");
                if (!string.IsNullOrEmpty(routerId) && !routerKeys.Contains(routerId))
                {
                    Error($"{label} : Router {routerId} isn't configured (no Router with that ID or Name, and no Service with that ID)");
                }

                var groupId = GetPropertyValue(configuration, "GroupId");
                if (!string.IsNullOrEmpty(groupId) && !configurations.Any(o => o.Category == TrakHoundGroupConfiguration.ConfigurationCategory && o.Id == groupId))
                {
                    Warning($"{label} : Group {groupId} isn't configured");
                }

                if (configuration is TrakHoundZoneConfiguration zone && zone.Members != null)
                {
                    foreach (var member in zone.Members.Where(o => !string.IsNullOrEmpty(o) && !o.Contains('*')))
                    {
                        var separator = member.IndexOf(':');
                        var memberCategory = separator > 0 ? member.Substring(0, separator) : null;
                        var memberId = separator > 0 ? member.Substring(separator + 1) : null;

                        // A Group ("group:{groupId}") stands for every Module in it
                        if (memberCategory == null || !(_memberCategories.Contains(memberCategory) || memberCategory == TrakHoundGroupConfiguration.ConfigurationCategory))
                        {
                            Warning($"{label} : Member {member} isn't a Member ID (ex. \"worker:my-worker\") or a Group (ex. \"group:my-group\")");
                        }
                        else if (!configurations.Any(o => o.Category == memberCategory && o.Id == memberId))
                        {
                            Warning($"{label} : Member {member} isn't configured");
                        }
                    }
                }

                if (configuration.GetType().GetProperty("Parameters")?.GetValue(configuration) is Dictionary<string, object> parameters)
                {
                    foreach (var parameter in parameters)
                    {
                        if (parameter.Value is not string value) continue;

                        if (TrakHoundSecrets.IsEncrypted(value) && TrakHoundSecrets.Decrypt(value) == null)
                        {
                            Error($"{label} : Parameter {parameter.Key} can't be decrypted with the key in ({keyPath})");
                        }

                        foreach (Match match in _variableReferenceRegex.Matches(value))
                        {
                            if (match.Value == "$${" || match.Groups[1].Success) continue;

                            var variableName = match.Groups[2].Value;
                            if (!variables.ContainsKey(variableName))
                            {
                                Error($"{label} : Parameter {parameter.Key} references ${{{variableName}}}, which isn't defined in {TrakHoundConfigurationVariables.Filename}");
                            }
                        }
                    }
                }
            }

            var instanceConfigurationPath = System.IO.Path.Combine(configurationProfile.Path, TrakHoundInstanceConfiguration.Filename);
            if (File.Exists(instanceConfigurationPath))
            {
                // Settings can reference the Profile's Variables (ex. "httpPort: ${port}"), resolved the way the Instance resolves them
                var profileVariables = TrakHoundConfigurationVariables.Read(configurationProfile.Path);
                var instanceConfiguration = TrakHoundInstanceConfiguration.Read(instanceConfigurationPath, value => TrakHoundConfigurationVariables.Resolve(value, profileVariables));
                if (instanceConfiguration == null)
                {
                    Error($"{TrakHoundInstanceConfiguration.Filename} : Can't be read ({instanceConfigurationPath})");
                }
                else
                {
                    foreach (var reference in instanceConfiguration.UnresolvedReferences)
                    {
                        Warning($"{TrakHoundInstanceConfiguration.Filename} : {reference} isn't defined here, so the default is used unless the Instance has the Variable (or Environment Variable)");
                    }
                    if (instanceConfiguration.HttpPort < 1 || instanceConfiguration.HttpPort > 65535) Error($"{TrakHoundInstanceConfiguration.Filename} : httpPort {instanceConfiguration.HttpPort} isn't a valid port");
                    if (instanceConfiguration.AdminHttpPort < 0 || instanceConfiguration.AdminHttpPort > 65535) Error($"{TrakHoundInstanceConfiguration.Filename} : adminHttpPort {instanceConfiguration.AdminHttpPort} isn't a valid port");
                    if (instanceConfiguration.AdminHttpPort != 0 && instanceConfiguration.AdminHttpPort == instanceConfiguration.HttpPort) Error($"{TrakHoundInstanceConfiguration.Filename} : adminHttpPort is the same as httpPort (use 0 to serve the Admin interface on the HTTP port)");
                }
            }

            errorCount += profileErrors.Count;

            Console.WriteLine();
            if (errorCount == 0 && warningCount == 0) Console.WriteLine($"No problems found in ({configurationProfile.Path}) : {configurations.Count} Configurations");
            else Console.WriteLine($"{errorCount} Errors and {warningCount} Warnings in ({configurationProfile.Path}) : {configurations.Count} Configurations");

            return errorCount == 0;
        }

        [Command("variables", "List the Variables of a Configuration Profile")]
        [CommandHelp(null, "th config variables ./instance")]
        public static bool Variables(
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            var configurationProfile = OpenProfile(path, profile, false, out _);
            if (configurationProfile == null) return false;

            // Values as stored (secrets are encrypted when the file is read)
            var variables = TrakHoundConfigurationVariables.Read(configurationProfile.Path);
            if (variables.Count > 0)
            {
                PrintTable(new string[] { "Name", "Value" }, variables.OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase).Select(o => new string[] { o.Key, o.Value }).ToList());
            }
            else
            {
                Console.WriteLine($"No Variables Found in ({TrakHoundConfigurationVariables.GetPath(configurationProfile.Path)})");
            }

            return true;
        }

        [Command("set-variable", "Add or change a Variable of a Configuration Profile")]
        [CommandHelp(null, "th config set-variable dbHost sql01 ./instance")]
        public static bool SetVariable(
            [CommandParameter("The name of the Variable (letters, numbers, '_', '.' and '-')")]
            string name,
            [CommandParameter("The value. Secrets (ex. dbPassword) are encrypted. Use ${env:NAME} to read it from an Environment Variable of the Instance")]
            string value,
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            if (!TrakHoundConfigurationVariables.IsValidName(name))
            {
                throw new CommandException($"Invalid Variable name : {name} (letters, numbers, '_', '.' and '-')");
            }

            var configurationProfile = OpenProfile(path, profile, true, out _);
            if (configurationProfile == null) return false;

            var variables = TrakHoundConfigurationVariables.Read(configurationProfile.Path);
            var exists = variables.ContainsKey(name);
            variables[name] = value ?? "";

            TrakHoundConfigurationVariables.Save(configurationProfile.Path, variables);

            Console.WriteLine($"{(exists ? "Updated" : "Added")} Variable ({name}) in ({TrakHoundConfigurationVariables.GetPath(configurationProfile.Path)}). Restart the Instance to apply it");
            return true;
        }

        [Command("remove-variable", "Remove a Variable from a Configuration Profile")]
        [CommandHelp(null, "th config remove-variable dbHost ./instance")]
        public static bool RemoveVariable(
            [CommandParameter("The name of the Variable")]
            string name,
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            var configurationProfile = OpenProfile(path, profile, false, out _);
            if (configurationProfile == null) return false;

            var variables = TrakHoundConfigurationVariables.Read(configurationProfile.Path);
            if (!variables.Remove(name))
            {
                Console.Error.WriteLine($"Error : Variable not Found : {name}");
                return false;
            }

            TrakHoundConfigurationVariables.Save(configurationProfile.Path, variables);

            Console.WriteLine($"Removed Variable ({name}) from ({TrakHoundConfigurationVariables.GetPath(configurationProfile.Path)}). Restart the Instance to apply it");
            return true;
        }


        private static ConfigurationCategory GetCategory(string category)
        {
            if (!string.IsNullOrEmpty(category) && _categories.TryGetValue(category, out var configurationCategory)) return configurationCategory;

            throw new CommandException($"Unknown Category : {category} (expected {string.Join(", ", _categories.Keys.OrderBy(o => o))})");
        }

        /// <summary>
        /// Returns null (with the error printed) if the directory, or the Profile when create is false, doesn't exist
        /// </summary>
        /// <summary>
        /// Add the default Configuration (TrakHoundPackageConfigurations) of each Package that doesn't have a Configuration in the Profile yet (th packages install --configure)
        /// </summary>
        internal static bool AddDefaultConfigurations(string path, string profile, IEnumerable<TrakHoundPackage> packages, string groupId = null)
        {
            var configurationProfile = OpenProfile(path, profile, true, out var errors);
            if (configurationProfile == null) return false;

            var success = true;
            groupId = !string.IsNullOrWhiteSpace(groupId) ? groupId.Trim() : null;

            // The Group is created (with only its ID) if it doesn't exist, so the new Configurations don't name a missing Group
            if (groupId != null && !_categories[TrakHoundGroupConfiguration.ConfigurationCategory].Read(configurationProfile).Any(o => o.Id == groupId))
            {
                if (!Save(configurationProfile, new TrakHoundGroupConfiguration { Id = groupId }, false, errors)) success = false;
            }

            foreach (var package in packages.DistinctBy(o => o.Id))
            {
                var configuration = TrakHoundPackageConfigurations.CreateDefault(package);
                if (configuration == null) continue;

                var configurations = _categories[configuration.Category].Read(configurationProfile);
                if (configurations.Any(o => TrakHoundPackageConfigurations.GetPackageId(o) == package.Id))
                {
                    Console.WriteLine($"Configuration Already Exists : ({configuration.Category} : {package.Id})");

                    // Existing Configurations without a Volume ID get the Package's default (ex. one added by this version)
                    foreach (var existing in configurations.ToList())
                    {
                        var updated = TrakHoundPackageConfigurations.WithDefaultVolumeId(existing, package);
                        if (updated != null && !Save(configurationProfile, updated, true, errors)) success = false;
                    }

                    continue;
                }

                if (groupId != null) TrakHoundPackageConfigurations.SetGroupId(configuration, groupId);
                if (!Save(configurationProfile, configuration, false, errors)) success = false;
            }

            return success;
        }

        private static TrakHoundConfigurationProfile OpenProfile(string path, string profile, bool create, out List<string> errors)
        {
            var rootPath = CliPath.Resolve(path);
            if (!Directory.Exists(rootPath))
            {
                Console.Error.WriteLine($"Error : Directory not Found : ({rootPath})");
                errors = null;
                return null;
            }

            if (string.IsNullOrEmpty(profile)) profile = TrakHoundConfigurationProfile.Default;

            var profilePath = System.IO.Path.Combine(rootPath, TrakHoundConfigurations.Directory, profile);
            if (!create && !Directory.Exists(profilePath))
            {
                Console.Error.WriteLine($"Error : Configuration Profile not Found : ({profilePath})");
                errors = null;
                return null;
            }

            var configurationProfile = new TrakHoundConfigurationProfile(profile, path: profilePath);

            var profileErrors = new List<string>();
            configurationProfile.ErrorReceived += (sender, message) =>
            {
                profileErrors.Add(message);
                Console.Error.WriteLine($"Error : {message}");
            };

            errors = profileErrors;
            return configurationProfile;
        }

        private static ITrakHoundConfiguration Find(TrakHoundConfigurationProfile profile, ConfigurationCategory category, string id)
        {
            var configurations = category.Read(profile).Where(o => string.Equals(o.Id, id, StringComparison.Ordinal)).ToList();
            if (configurations.Count > 1)
            {
                Console.WriteLine($"Warning : Duplicate Configuration ({category.Name} : {id}) in {string.Join(", ", configurations.Select(o => $"({o.Path})"))}. Using ({configurations[0].Path})");
            }

            return configurations.FirstOrDefault();
        }

        private static bool Save(TrakHoundConfigurationProfile profile, ITrakHoundConfiguration configuration, bool exists, List<string> errors)
        {
            // Errors reading other files in the Category were already reported and don't stop the Save
            var errorCount = errors.Count;

            profile.Add(configuration, true);
            if (errors.Count > errorCount) return false;

            Console.WriteLine($"{(exists ? "Updated" : "Added")} Configuration ({configuration.Category} : {configuration.Id}) in ({configuration.Path})");
            return true;
        }

        private static void SetId(ITrakHoundConfiguration configuration, string id)
        {
            configuration.GetType().GetProperty("Id").SetValue(configuration, id);
        }

        private static (string Name, string Value) SplitNameValue(string value, string option)
        {
            var index = value?.IndexOf('=') ?? -1;
            if (index < 1) throw new CommandException($"Invalid value for {option} : '{value}' (expected name=value)");

            return (value.Substring(0, index).Trim(), value.Substring(index + 1));
        }

        private static void SetProperty(ITrakHoundConfiguration configuration, string name, string value)
        {
            var property = GetSettableProperties(configuration.GetType()).FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            if (property == null)
            {
                throw new CommandException($"Unknown Property : {name} (properties that can be set : {string.Join(", ", GetSettableProperties(configuration.GetType()).Select(o => o.Name.ToCamelCase()))})");
            }

            if (!SettingValue.IsSupported(property.PropertyType))
            {
                throw new CommandException($"The {property.Name.ToCamelCase()} Property can't be set with --property. Use 'th config add' with a YAML file instead");
            }

            property.SetValue(configuration, SettingValue.Parse(property.PropertyType, value, property.Name.ToCamelCase()));
        }

        private static IEnumerable<PropertyInfo> GetSettableProperties(Type type)
        {
            return SettingValue.GetStoredProperties(type).Where(o => !_reservedProperties.Contains(o.Name));
        }

        private static string GetPropertyValue(ITrakHoundConfiguration configuration, string name)
        {
            return configuration.GetType().GetProperty(name)?.GetValue(configuration)?.ToString();
        }

        private static void PrintTable(string[] headers, List<string[]> rows)
        {
            var widths = headers.Select((header, i) => Math.Max(header.Length, rows.Max(o => o[i]?.Length ?? 0))).ToArray();
            string Format(string[] values) => string.Join("  ", values.Select((value, i) => (value ?? "").PadRight(widths[i]))).TrimEnd();

            Console.WriteLine(Format(headers));
            Console.WriteLine(Format(widths.Select(o => new string('-', o)).ToArray()));
            foreach (var row in rows) Console.WriteLine(Format(row));
        }


        private class ConfigurationCategory
        {
            public string Name { get; init; }

            public Type Type { get; init; }

            public Func<TrakHoundConfigurationProfile, IEnumerable<ITrakHoundConfiguration>> Read { get; init; }


            public static ConfigurationCategory Create<TConfiguration>(string name) where TConfiguration : ITrakHoundConfiguration
            {
                return new ConfigurationCategory
                {
                    Name = name,
                    Type = typeof(TConfiguration),
                    // Files without an Id aren't loaded by the Instance (the Profile reports them)
                    Read = profile => profile.Read<TConfiguration>(name).Cast<ITrakHoundConfiguration>().Where(o => !string.IsNullOrEmpty(o.Id))
                };
            }
        }
    }
}
