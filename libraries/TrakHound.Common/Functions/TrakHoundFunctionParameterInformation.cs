// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Functions
{
    /// <summary>
    /// An Input Parameter of a Function, from a FunctionParameter attribute on the Function class
    /// </summary>
    public class TrakHoundFunctionParameterInformation
    {
        /// <summary>
        /// The name the Function reads the Parameter by
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// What the Parameter is for
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The kind of value it takes
        /// </summary>
        [JsonPropertyName("type")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundFunctionParameterType Type { get; set; }

        /// <summary>
        /// A Run without a value for the Parameter is refused
        /// </summary>
        [JsonPropertyName("required")]
        public bool Required { get; set; }

        /// <summary>
        /// The value used when the Parameter isn't passed
        /// </summary>
        [JsonPropertyName("defaultValue")]
        public string DefaultValue { get; set; }

        /// <summary>
        /// The values it can take (any value when empty)
        /// </summary>
        [JsonPropertyName("options")]
        public string[] Options { get; set; }

        /// <summary>
        /// The value is a secret: masked when entered and not saved with a Run's Input
        /// </summary>
        [JsonPropertyName("secret")]
        public bool Secret { get; set; }


        /// <summary>
        /// The Parameters described by FunctionParameter attributes on the Function type, in the order they are declared
        /// </summary>
        public static List<TrakHoundFunctionParameterInformation> Create(Type functionType)
        {
            var parameters = new List<TrakHoundFunctionParameterInformation>();
            if (functionType == null) return parameters;

            foreach (var attribute in functionType.GetCustomAttributes<FunctionParameterAttribute>(true))
            {
                if (string.IsNullOrWhiteSpace(attribute.Name)) continue;
                if (parameters.Any(o => string.Equals(o.Name, attribute.Name, StringComparison.OrdinalIgnoreCase))) continue; // The first one wins

                var parameter = new TrakHoundFunctionParameterInformation();
                parameter.Name = attribute.Name.Trim();
                parameter.Description = attribute.Description;
                parameter.Type = attribute.Type;
                parameter.Required = attribute.Required;
                parameter.DefaultValue = attribute.DefaultValue;
                parameter.Options = attribute.Options != null && attribute.Options.Length > 0 ? attribute.Options : null;
                parameter.Secret = attribute.Secret;
                parameters.Add(parameter);
            }

            return parameters;
        }

        /// <summary>
        /// The Input Parameters with each described Parameter's DefaultValue added when it isn't passed (or is empty)
        /// </summary>
        public static Dictionary<string, string> ApplyDefaults(IEnumerable<TrakHoundFunctionParameterInformation> definitions, IReadOnlyDictionary<string, string> parameters)
        {
            // ToDictionary rather than the Dictionary constructor, which can't take an IReadOnlyDictionary on .NET Standard 2.0 (TrakHound.Client)
            var result = parameters != null ? parameters.ToDictionary(o => o.Key, o => o.Value) : new Dictionary<string, string>();

            if (definitions != null)
            {
                foreach (var definition in definitions)
                {
                    if (definition.DefaultValue != null && string.IsNullOrEmpty(result.GetValueOrDefault(definition.Name)))
                    {
                        result[definition.Name] = definition.DefaultValue;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Why the Input Parameters can't be used (a missing Required Parameter, or a value that doesn't fit its Type or Options), or null when they can
        /// </summary>
        public static string Validate(IEnumerable<TrakHoundFunctionParameterInformation> definitions, IReadOnlyDictionary<string, string> parameters)
        {
            if (definitions == null) return null;

            var errors = new List<string>();
            var missing = new List<string>();

            foreach (var definition in definitions)
            {
                var value = parameters?.GetValueOrDefault(definition.Name);
                if (string.IsNullOrEmpty(value))
                {
                    if (definition.Required) missing.Add(definition.Name);
                    continue;
                }

                var error = ValidateValue(definition, value);
                if (error != null) errors.Add(error);
            }

            if (missing.Count > 0) errors.Insert(0, $"Missing Required {(missing.Count == 1 ? "Parameter" : "Parameters")} : {string.Join(", ", missing)}");

            return errors.Count > 0 ? string.Join(". ", errors) : null;
        }

        /// <summary>
        /// Why the value doesn't fit the Parameter's Type or Options, or null when it does
        /// </summary>
        public static string ValidateValue(TrakHoundFunctionParameterInformation definition, string value)
        {
            if (definition == null || string.IsNullOrEmpty(value)) return null;

            if (definition.Options != null && definition.Options.Length > 0 && !definition.Options.Contains(value))
            {
                return $"{definition.Name} must be one of : {string.Join(", ", definition.Options)}";
            }

            switch (definition.Type)
            {
                case TrakHoundFunctionParameterType.Integer:
                    if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return $"{definition.Name} must be a whole number";
                    break;

                case TrakHoundFunctionParameterType.Number:
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return $"{definition.Name} must be a number";
                    break;

                case TrakHoundFunctionParameterType.Boolean:
                    if (!bool.TryParse(value, out _)) return $"{definition.Name} must be true or false";
                    break;

                case TrakHoundFunctionParameterType.DateTime:
                    if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _)) return $"{definition.Name} must be a date and time (ex. 2026-10-28T08:00:00Z)";
                    break;

                case TrakHoundFunctionParameterType.Json:
                    try { using (JsonDocument.Parse(value)) { } }
                    catch (JsonException) { return $"{definition.Name} must be JSON"; }
                    break;
            }

            return null;
        }
    }
}
