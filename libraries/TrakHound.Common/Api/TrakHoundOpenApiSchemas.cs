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
    /// Creates the JSON Schemas of the Types used by an OpenAPI Document. Types with properties, and the objects and enums they contain, are
    /// added once to Schemas (the Document's "components/schemas") under their Type's name and referenced. Create one for each Document (the Type cache would otherwise keep Module Types loaded after their Module is unloaded)
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

            return GetComponent(underlyingType, false) ?? ExportSchema(underlyingType);
        }

        /// <summary>
        /// A reference to the Component Schema of the Type, created the first time. A Type whose Schema has no properties (ex. a collection
        /// of referenced items, or an enum when force is false) isn't made a Component, and null is returned
        /// </summary>
        private JsonObject GetComponent(Type type, bool force)
        {
            if (_schemaNames.TryGetValue(type, out var existingName)) return CreateReference(existingName);

            // The name is reserved before the Type is exported, so a Type that it contains which contains it in turn (ex. A > B > A)
            // references this Component instead of exporting the Type again
            var name = CreateSchemaName(type);
            _schemaNames[type] = name;

            var schema = ExportSchema(type);

            if (force || HasProperties(schema))
            {
                // Recursive Types reference their own Schema ("#" in an exported Schema is the Schema itself)
                RewriteReferences(schema, $"#/components/schemas/{name}");
                _schemas[name] = schema;

                return CreateReference(name);
            }

            _schemaNames.Remove(type);
            return null;
        }

        private JsonNode ExportSchema(Type type)
        {
            try
            {
                var exporterOptions = new JsonSchemaExporterOptions
                {
                    TreatNullObliviousAsNonNullable = _exporterOptions.TreatNullObliviousAsNonNullable,
                    TransformSchemaNode = ReferenceNamedTypes
                };

                var schema = _schemaOptions.GetJsonSchemaAsNode(type, exporterOptions);
                if (schema is JsonObject) return schema;
            }
            catch { }

            // "true" (any value), or a Type that can't be described
            return new JsonObject();
        }

        // Objects and enums inside a Schema (ex. a property, or a collection's items) become Components of their own and are referenced,
        // so each Type is described once, under its own name. Inline, a client generator would have to make up a name (ex. "Page1")
        // and would generate a collection's item Type again for each collection
        private JsonNode ReferenceNamedTypes(JsonSchemaExporterContext context, JsonNode node)
        {
            // The root is the Type being exported
            if (context.Path.IsEmpty || node is not JsonObject schema) return node;

            var type = Nullable.GetUnderlyingType(context.TypeInfo.Type) ?? context.TypeInfo.Type;

            // The exporter describes a Type it has already described in this Schema (ex. a second property of the same Type, or a
            // recursive one) with a pointer to the first ("#/properties/open/items"). A Type with a Component references it instead
            if (schema.ContainsKey("$ref"))
            {
                return _schemaNames.TryGetValue(type, out var componentName) ? CreateReference(componentName) : node;
            }

            var isEnum = type.IsEnum;
            var isObject = context.TypeInfo.Kind == JsonTypeInfoKind.Object && schema.ContainsKey("properties");
            if (!isEnum && !isObject) return node;

            var reference = isEnum ? GetEnumComponent(type, schema) : GetComponent(type, false);
            if (reference == null) return node;

            // A nullable property keeps allowing null. A collection's items are referenced as they are (a generator would wrap
            // "anyOf [item, null]" items in a Type of their own, and the collections don't hold null items)
            var isItem = context.Path.Length > 0 && context.Path[^1] == "items";
            if (!isItem && IsNullable(schema))
            {
                return new JsonObject { ["anyOf"] = new JsonArray(reference, new JsonObject { ["type"] = "null" }) };
            }

            return reference;
        }

        // An enum's Component is made from the property's Schema rather than by exporting the enum Type: how an enum is written is often set
        // on the property (ex. [JsonConverter(typeof(JsonStringEnumConverter))]), so the Type alone would describe it as a number. A property
        // that writes the enum differently from its Component (ex. as a number) keeps its own Schema (null)
        private JsonObject GetEnumComponent(Type type, JsonObject propertySchema)
        {
            var schema = (JsonObject)propertySchema.DeepClone();

            // The Component describes the enum's values. Whether a property allows null stays with the property
            if (schema["type"] is JsonArray types)
            {
                var nonNull = types.Where(o => !(o is JsonValue value && value.TryGetValue<string>(out var s) && s == "null")).Select(o => o.GetValue<string>()).ToList();
                if (nonNull.Count == 1) schema["type"] = nonNull[0];
                else schema["type"] = new JsonArray(nonNull.Select(o => (JsonNode)o).ToArray());
            }
            if (schema["enum"] is JsonArray values)
            {
                schema["enum"] = new JsonArray(values.Where(o => o != null).Select(o => o.DeepClone()).ToArray());
            }

            if (_schemaNames.TryGetValue(type, out var existingName))
            {
                return JsonNode.DeepEquals(_schemas[existingName], schema) ? CreateReference(existingName) : null;
            }

            var name = CreateSchemaName(type);
            _schemaNames[type] = name;
            _schemas[name] = schema;

            return CreateReference(name);
        }

        // "null" in the type (ex. ["object", "null"]), or a null value in an enum (a string enum's Schema has no type)
        private static bool IsNullable(JsonObject schema)
        {
            if (schema["type"] is JsonArray types && types.Any(o => o is JsonValue value && value.TryGetValue<string>(out var s) && s == "null")) return true;
            if (schema["enum"] is JsonArray values && values.Any(o => o == null)) return true;
            return false;
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
                // Only the exporter's own pointers ("#" and "#/properties/..."), not references to other Components
                if (jsonObject["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference) && reference.StartsWith('#') && !reference.StartsWith("#/components/"))
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

            // Names are reserved (in _schemaNames) before their Schemas are added
            var name = baseName;
            var i = 2;
            while (_schemas.ContainsKey(name) || _schemaNames.ContainsValue(name))
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
