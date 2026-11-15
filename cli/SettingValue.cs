// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Globalization;
using System.Reflection;
using YamlDotNet.Serialization;

namespace TrakHound.CLI
{
    /// <summary>
    /// Sets the properties of a Configuration object from command line text (ex. --property packageId=MyPackage)
    /// </summary>
    internal static class SettingValue
    {
        /// <summary>
        /// Returns true if a value of the Type can be set from text (text, numbers, true/false, enums and lists of text)
        /// </summary>
        public static bool IsSupported(Type type)
        {
            var targetType = Nullable.GetUnderlyingType(type) ?? type;
            return targetType == typeof(string) || targetType.IsEnum || targetType.IsPrimitive || targetType == typeof(decimal) || targetType.IsAssignableFrom(typeof(List<string>));
        }

        /// <summary>
        /// Converts the text to the Type. Lists are comma separated
        /// </summary>
        public static object Parse(Type type, string value, string name)
        {
            var targetType = Nullable.GetUnderlyingType(type) ?? type;

            try
            {
                if (targetType == typeof(string)) return value;
                if (targetType.IsEnum) return Enum.Parse(targetType, value, true);
                if (targetType.IsAssignableFrom(typeof(List<string>))) return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                if (targetType.IsPrimitive || targetType == typeof(decimal)) return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            }
            catch
            {
                throw new CommandException($"Invalid value for {name} : '{value}' (expected {targetType.Name})");
            }

            throw new CommandException($"{name} can't be set from the command line. Edit the file instead");
        }

        /// <summary>
        /// Formats a value for output (true/false in lower case and lists comma separated, as they are entered)
        /// </summary>
        public static string Format(object value)
        {
            switch (value)
            {
                case null: return "";
                case bool b: return b ? "true" : "false";
                case string s: return s;
                case IEnumerable<string> list: return string.Join(",", list);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        /// <summary>
        /// The public properties that are stored in the file, by their YAML (camelCase) name
        /// </summary>
        public static IEnumerable<PropertyInfo> GetStoredProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(o => o.CanRead && o.CanWrite && o.GetIndexParameters().Length == 0 && o.GetCustomAttribute<YamlIgnoreAttribute>() == null);
        }
    }
}
