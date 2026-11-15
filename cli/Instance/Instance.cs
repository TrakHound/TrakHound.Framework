// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;
using TrakHound.Configurations;
using TrakHound.Instances;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.CLI.Instance
{
    /// <summary>
    /// Reads and changes the Instance Settings ("[path]/config/[profile]/instance.config.yaml"). The Instance reads the file when it starts,
    /// so changes are applied when it is restarted
    /// </summary>
    [CommandGroup("instance", "Read and Change the Settings of a TrakHound Instance (instance.config.yaml)")]
    internal static class Instance
    {
        private const string PathDescription = "The root directory (ex. the TrakHound Instance directory) that contains the 'config' directory. If not specified, the current working directory is used";
        private const string ProfileDescription = "The Configuration Profile";
        private const string NameDescription = "The name of the Setting (ex. httpPort). Use '.' for nested Settings (ex. metrics.enabled, rateLimits.api.permitLimit)";
        private const string _tempFileExtension = ".tmp";


        [Command("settings", "Print the Settings (the YAML of instance.config.yaml)")]
        [CommandHelp(null, "th instance settings ./instance")]
        public static bool Settings(
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            var filePath = GetFilePath(path, profile);
            if (filePath == null) return false;

            if (File.Exists(filePath))
            {
                if (Read(filePath) == null) return false;

                Console.Write(File.ReadAllText(filePath));
            }
            else
            {
                PrintDefaultsWarning(filePath);
                Console.Write(Serialize(new TrakHoundInstanceConfiguration()));
            }

            return true;
        }

        [Command("get", "Print the value of a Setting")]
        [CommandHelp(null, "th instance get httpPort ./instance")]
        public static bool Get(
            [CommandParameter(NameDescription)]
            string name,
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            var filePath = GetFilePath(path, profile);
            if (filePath == null) return false;

            if (!File.Exists(filePath)) PrintDefaultsWarning(filePath);

            var configuration = File.Exists(filePath) ? Read(filePath) : new TrakHoundInstanceConfiguration();
            if (configuration == null) return false;

            // A Setting that references a Variable (ex. "${port}") is printed as the reference
            if (configuration.References.TryGetValue(GetSettingName(name), out var reference))
            {
                Console.WriteLine(reference);
                return true;
            }

            var (target, property) = Resolve(configuration, name);
            var value = target != null ? property.GetValue(target) : null;

            // A group of Settings (ex. metrics) is printed as YAML
            if (value != null && !SettingValue.IsSupported(property.PropertyType)) Console.Write(Serialize(value));
            else Console.WriteLine(SettingValue.Format(value));

            return true;
        }

        [Command("set", "Change a Setting. The Instance applies it when it is restarted")]
        [CommandHelp(null, "th instance set httpPort 8080 ./instance")]
        public static bool Set(
            [CommandParameter(NameDescription)]
            string name,
            [CommandParameter("The value. Lists are comma separated (ex. 127.0.0.1,10.0.0.0/8)")]
            string value,
            [CommandParameter(PathDescription)]
            string path = null,
            [CommandOption(ProfileDescription)]
            string profile = TrakHoundConfigurationProfile.Default
            )
        {
            var filePath = GetFilePath(path, profile);
            if (filePath == null) return false;

            // A new file has the defaults the Instance would create it with
            var configuration = File.Exists(filePath) ? Read(filePath) : new TrakHoundInstanceConfiguration();
            if (configuration == null) return false;

            var (target, property) = Resolve(configuration, name, true);
            var settingName = GetSettingName(name);
            property.SetValue(target, SettingValue.Parse(property.PropertyType, value, settingName));
            configuration.RemoveReference(settingName); // The value replaces a Variable reference

            var directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            // The Instance ID isn't saved with the settings : one in an older file moves to its own file first, so it isn't lost
            if (!string.IsNullOrEmpty(configuration.FileInstanceId)) TrakHoundInstanceIdentity.Load(directory, configuration);

            // Written to a temporary file and then moved over the file so that it is never read partially written (with the other Settings' Variable references)
            var tempPath = filePath + _tempFileExtension;
            File.WriteAllText(tempPath, configuration.ToYaml());
            File.Move(tempPath, filePath, true);

            Console.WriteLine($"Set {settingName} to {SettingValue.Format(property.GetValue(target))} in ({filePath}). Restart the Instance to apply it");
            return true;
        }


        /// <summary>
        /// Returns null (with the error printed) if the root directory doesn't exist
        /// </summary>
        private static string GetFilePath(string path, string profile)
        {
            var rootPath = CliPath.Resolve(path);
            if (!Directory.Exists(rootPath))
            {
                Console.Error.WriteLine($"Error : Directory not Found : ({rootPath})");
                return null;
            }

            if (string.IsNullOrEmpty(profile)) profile = TrakHoundConfigurationProfile.Default;

            return Path.Combine(rootPath, TrakHoundConfigurations.Directory, profile, TrakHoundInstanceConfiguration.Filename);
        }

        /// <summary>
        /// Returns null (with the error printed) if the file can't be read, so that it isn't replaced by the defaults
        /// </summary>
        private static TrakHoundInstanceConfiguration Read(string filePath)
        {
            var configuration = TrakHoundInstanceConfiguration.Read(filePath);
            if (configuration == null) Console.Error.WriteLine($"Error : Unable to read ({filePath}). Check that it is valid YAML");
            return configuration;
        }

        private static void PrintDefaultsWarning(string filePath)
        {
            Console.Error.WriteLine($"Warning : ({filePath}) doesn't exist. These are the defaults (the Instance creates the file when it starts)");
        }

        /// <summary>
        /// Finds the property of a Setting name (ex. "rateLimits.api.permitLimit"). Groups of Settings that are null are created if create is true,
        /// otherwise the Target is null
        /// </summary>
        private static (object Target, PropertyInfo Property) Resolve(TrakHoundInstanceConfiguration configuration, string name, bool create = false)
        {
            var segments = (name ?? "").Split('.');
            object target = configuration;
            var type = typeof(TrakHoundInstanceConfiguration);

            for (var i = 0; i < segments.Length; i++)
            {
                var property = SettingValue.GetStoredProperties(type).FirstOrDefault(o => string.Equals(o.Name, segments[i], StringComparison.OrdinalIgnoreCase));
                if (property == null)
                {
                    throw new CommandException($"Unknown Setting : {name} (run 'th instance settings' to see the Settings)");
                }

                if (i == segments.Length - 1) return (target, property);

                if (SettingValue.IsSupported(property.PropertyType) || property.PropertyType.GetConstructor(Type.EmptyTypes) == null)
                {
                    throw new CommandException($"Unknown Setting : {name} ({string.Join('.', segments.Take(i + 1))} has no nested Settings)");
                }

                var child = target != null ? property.GetValue(target) : null;
                if (child == null && create && target != null)
                {
                    child = Activator.CreateInstance(property.PropertyType);
                    property.SetValue(target, child);
                }

                target = child;
                type = property.PropertyType;
            }

            throw new CommandException($"Unknown Setting : {name}");
        }

        private static string GetSettingName(string name)
        {
            return string.Join('.', name.Split('.').Select(o => o.ToCamelCase()));
        }

        private static string Serialize(object value)
        {
            // The same settings as TrakHoundInstanceConfiguration.Save()
            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            return serializer.Serialize(value);
        }
    }
}
