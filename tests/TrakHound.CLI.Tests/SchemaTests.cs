// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrakHound.Debug;
using TrakHound.Packages;

namespace TrakHound.CLI.Tests
{
    /// <summary>
    /// The JSON Schemas in schemas/ (for editors) : they're written by hand, so these check that they list the same properties as the
    /// types that read the files, and that the templates point at them
    /// </summary>
    public class SchemaTests
    {
        private const string SchemaBaseUrl = "https://www.trakhound.com/schemas/";
        private const string SchemaProperty = "$schema";

        // Written by the CLI when it packs, not set in trakhound.package.json
        private static readonly string[] _packageBuildProperties = { "uuid", "buildDate", "hash", "contentHash" };

        private static readonly string[] _packageTemplates = { "Api", "App", "Function", "Identity", "Service", "Worker" };

        private static readonly (string File, string Schema)[] _templateFiles =
        {
            (TrakHoundPackage.PackageConfigurationFilename, "trakhound.package.schema.json"),
            (TrakHoundPackagePublishSettings.Filename, "trakhound.package.publish.schema.json"),
            (TrakHoundDebugConfiguration.Filename, "trakhound.debug.schema.json")
        };

        private static readonly JsonDocumentOptions _jsonOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };


        [Fact]
        public void PackageSchema_MatchesTheTypes()
        {
            using var schema = ReadSchema("trakhound.package.schema.json");

            var expected = JsonProperties(typeof(TrakHoundPackageJson)).Except(_packageBuildProperties);
            AssertSameProperties(expected, schema.RootElement);

            var dependency = schema.RootElement.GetProperty("properties").GetProperty("dependencies").GetProperty("items");
            AssertSameProperties(JsonProperties(typeof(TrakHoundPackageDependency)), dependency);
        }

        [Fact]
        public void PackageSchema_ListsTheMetadataKeys()
        {
            using var schema = ReadSchema("trakhound.package.schema.json");

            var metadata = PropertyNames(schema.RootElement.GetProperty("properties").GetProperty("metadata"));
            var keys = new[]
            {
                TrakHoundPackage.PublisherName, TrakHoundPackage.DescriptionName, TrakHoundPackage.ProjectName,
                TrakHoundPackage.RepositoryName, TrakHoundPackage.RepositoryDirectoryName, TrakHoundPackage.ImageName,
                TrakHoundPackageConfigurations.DefaultNameMetadataKey
            };

            foreach (var key in keys) Assert.Contains(key, metadata);
        }

        [Fact]
        public void PublishSchema_MatchesTheTypes()
        {
            using var schema = ReadSchema("trakhound.package.publish.schema.json");

            AssertSameProperties(JsonProperties(typeof(TrakHoundPackagePublishSettings)), schema.RootElement);

            var definitions = schema.RootElement.GetProperty("definitions");
            AssertSameProperties(JsonProperties(typeof(TrakHoundPackagePublishTarget)), definitions.GetProperty("target"));
            AssertSameProperties(JsonProperties(typeof(TrakHoundPackagePublishDestination)), definitions.GetProperty("destination"));
        }

        [Fact]
        public void DebugSchema_MatchesTheType()
        {
            using var schema = ReadSchema("trakhound.debug.schema.json");

            AssertSameProperties(JsonProperties(typeof(TrakHoundDebugConfiguration)), schema.RootElement);
        }

        [Fact]
        public void SchemaIds_MatchTheirFilenames()
        {
            foreach (var path in Directory.GetFiles(Path.Combine(RepositoryRoot, "schemas"), "*.schema.json"))
            {
                using var schema = JsonDocument.Parse(File.ReadAllText(path), _jsonOptions);
                Assert.Equal(SchemaBaseUrl + Path.GetFileName(path), schema.RootElement.GetProperty("$id").GetString());
            }
        }

        [Fact]
        public void Templates_PointAtTheSchemas()
        {
            foreach (var template in _packageTemplates)
            {
                foreach (var (file, schema) in _templateFiles)
                {
                    var path = Path.Combine(TemplateDirectory(template), file);
                    Assert.True(File.Exists(Path.Combine(RepositoryRoot, "schemas", schema)), schema);
                    Assert.Contains($"\"{SchemaProperty}\": \"{SchemaBaseUrl}{schema}\"", File.ReadAllText(path));
                }
            }
        }

        [Fact]
        public void Templates_OnlyUseSchemaProperties()
        {
            // trakhound.package.publish.json has template conditions (//#if) that don't parse until the template runs
            using var packageSchema = ReadSchema("trakhound.package.schema.json");
            using var debugSchema = ReadSchema("trakhound.debug.schema.json");

            foreach (var template in _packageTemplates)
            {
                AssertUsesSchemaProperties(Path.Combine(TemplateDirectory(template), TrakHoundPackage.PackageConfigurationFilename), packageSchema.RootElement);
                AssertUsesSchemaProperties(Path.Combine(TemplateDirectory(template), TrakHoundDebugConfiguration.Filename), debugSchema.RootElement);
            }
        }


        private static void AssertSameProperties(IEnumerable<string> expected, JsonElement schema)
        {
            var actual = PropertyNames(schema).Where(o => o != SchemaProperty);
            Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));
        }

        private static void AssertUsesSchemaProperties(string path, JsonElement schema)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path), _jsonOptions);
            var allowed = PropertyNames(schema);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                Assert.True(allowed.Contains(property.Name), $"{path} : \"{property.Name}\" isn't in the schema");
            }
        }

        private static string[] PropertyNames(JsonElement schema) => schema.GetProperty("properties").EnumerateObject().Select(o => o.Name).ToArray();

        private static IEnumerable<string> JsonProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(o => o.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
                .Where(o => o != null);
        }

        private static JsonDocument ReadSchema(string filename) => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, "schemas", filename)), _jsonOptions);

        private static string TemplateDirectory(string template) => Path.Combine(RepositoryRoot, "templates", "TrakHound.DotNet.Templates", "content", $"TrakHound.{template}.Template");

        private static string RepositoryRoot
        {
            get
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "schemas")))
                {
                    directory = directory.Parent;
                }

                Assert.NotNull(directory);
                return directory.FullName;
            }
        }
    }
}
