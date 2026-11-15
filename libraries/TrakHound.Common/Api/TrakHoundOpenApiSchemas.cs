// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace TrakHound.Api
{
    /// <summary>
    /// Creates the JSON Schemas of the Types used by an OpenAPI Document. Types with properties are added once to Schemas (the Document's
    /// "components/schemas") and referenced. Create one for each Document (the Type cache would otherwise keep Module Types loaded after their Module is unloaded)
    /// </summary>
    public class TrakHoundOpenApiSchemas
    {
        private static readonly Regex _componentNameRegex = new Regex(@"[^A-Za-z0-9\.\-_]", RegexOptions.Compiled);

        private readonly JsonSerializerOptions _schemaOptions;
        private readonly JsonSchemaExporterOptions _exporterOptions = new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true };
        private readonly JsonObject _schemas = new JsonObject();
        private readonly Dictionary<Type, string> _schemaNames = new Dictionary<Type, string>();


        /// <summary>
        /// The Component Schemas that were created, keyed by name (the Document's "components/schemas")
        /// </summary>
        public JsonObject Schemas => _schemas;

        /// <summary>
        /// The options the Schemas are created with (also used to describe default values)
        /// </summary>
        public JsonSerializerOptions SerializerOptions => _schemaOptions;


        public TrakHoundOpenApiSchemas()
        {
            _schemaOptions = new JsonSerializerOptions(Json.DefaultOptions)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
                NumberHandling = JsonNumberHandling.Strict // Request Bodies accept numbers as strings, but describe them as numbers
            };
        }


        /// <summary>
        /// Gets the Schema of a Type (a reference to a Component Schema for Types with properties)
        /// </summary>
        public JsonNode GetSchema(Type type)
        {
            if (type == null) return new JsonObject();

            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            if (underlyingType == typeof(byte[]) || typeof(Stream).IsAssignableFrom(underlyingType))
            {
                return new JsonObject { ["type"] = "string", ["format"] = "binary" };
            }

            // Component Schemas are referenced (and created once)
            if (_schemaNames.TryGetValue(underlyingType, out var existingName))
            {
                return CreateReference(existingName);
            }

            var schema = ExportSchema(underlyingType);

            if (HasProperties(schema))
            {
                var name = CreateSchemaName(underlyingType);
                _schemaNames[underlyingType] = name;

                // Recursive Types reference their own Schema ("#" in an exported Schema is the Schema itself)
                RewriteReferences(schema, $"#/components/schemas/{name}");
                _schemas[name] = schema;

                return CreateReference(name);
            }

            return schema;
        }

        private JsonNode ExportSchema(Type type)
        {
            try
            {
                var schema = _schemaOptions.GetJsonSchemaAsNode(type, _exporterOptions);
                if (schema is JsonObject) return schema;
            }
            catch { }

            // "true" (any value), or a Type that can't be described
            return new JsonObject();
        }

        private static bool HasProperties(JsonNode schema)
        {
            if (schema is JsonObject schemaObject)
            {
                if (schemaObject.ContainsKey("properties")) return true;
                if (schemaObject["items"] is JsonObject items && HasProperties(items)) return true;
                if (schemaObject["additionalProperties"] is JsonObject additionalProperties && HasProperties(additionalProperties)) return true;
            }

            return false;
        }

        private static void RewriteReferences(JsonNode node, string basePointer)
        {
            if (node is JsonObject jsonObject)
            {
                if (jsonObject["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference) && reference.StartsWith('#'))
                {
                    jsonObject["$ref"] = basePointer + reference.Substring(1);
                }

                foreach (var property in jsonObject.ToList())
                {
                    RewriteReferences(property.Value, basePointer);
                }
            }
            else if (node is JsonArray jsonArray)
            {
                foreach (var item in jsonArray)
                {
                    RewriteReferences(item, basePointer);
                }
            }
        }

        private static JsonObject CreateReference(string name) => new JsonObject { ["$ref"] = $"#/components/schemas/{name}" };

        private string CreateSchemaName(Type type)
        {
            var baseName = _componentNameRegex.Replace(GetTypeName(type), "");
            if (string.IsNullOrEmpty(baseName)) baseName = "Schema";

            var name = baseName;
            var i = 2;
            while (_schemas.ContainsKey(name))
            {
                name = $"{baseName}{i++}";
            }

            return name;
        }

        private static string GetTypeName(Type type)
        {
            if (type.IsArray) return $"{GetTypeName(type.GetElementType())}Array";

            if (type.IsGenericType)
            {
                var name = type.Name;
                var index = name.IndexOf('`');
                if (index > 0) name = name.Substring(0, index);

                return $"{name}Of{string.Join("And", type.GetGenericArguments().Select(GetTypeName))}";
            }

            return type.Name;
        }
    }
}
