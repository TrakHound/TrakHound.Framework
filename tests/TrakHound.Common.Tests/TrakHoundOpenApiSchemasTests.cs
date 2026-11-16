// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TrakHound.Api;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Each object and enum is described once, as a Component under its Type's name, and referenced wherever it is used, so a client
    /// generator gets real names instead of inventing them (ex. "Page1") and doesn't generate a collection's item Type twice
    /// </summary>
    public class TrakHoundOpenApiSchemasTests
    {
        // Written as a string by the properties that say so, like TrakHound's own enums
        public enum Shade { Light, Dark }

        public class Page
        {
            public string Route { get; set; }

            [JsonConverter(typeof(JsonStringEnumConverter))]
            public Shade Shade { get; set; }
        }

        public class Site
        {
            public string Name { get; set; }
            public List<Page> Pages { get; set; }
            public Page Home { get; set; }

            [JsonConverter(typeof(JsonStringEnumConverter))]
            public Shade? Theme { get; set; }

            // Written as a number, unlike the Shade Component
            public Shade Fallback { get; set; }
        }

        public class Streams
        {
            public List<Page> Open { get; set; }
            public List<Page> Closed { get; set; }
        }

        public class Parent
        {
            public string Name { get; set; }
            public Child Child { get; set; }
        }

        public class Child
        {
            public string Name { get; set; }
            public Parent Parent { get; set; }
        }


        [Fact]
        public void NestedObjectsAndEnumsAreReferencedComponents()
        {
            var schemas = new TrakHoundOpenApiSchemas();

            Assert.Equal("#/components/schemas/Site", GetReference(schemas.GetSchema(typeof(Site))));

            var site = schemas.Schemas["Site"];
            Assert.Equal("#/components/schemas/Page", GetReference(site["properties"]["pages"]["items"]));
            Assert.Equal("#/components/schemas/Page", GetReference(site["properties"]["home"]));

            var page = schemas.Schemas["Page"];
            Assert.NotNull(page["properties"]["route"]);
            Assert.Equal("#/components/schemas/Shade", page["properties"]["shade"]["$ref"]?.GetValue<string>());

            var shade = schemas.Schemas["Shade"];
            Assert.Equal(new[] { "Light", "Dark" }, shade["enum"].AsArray().Select(o => o.GetValue<string>()));
        }

        [Fact]
        public void NullablePropertiesStillAllowNull()
        {
            var schemas = new TrakHoundOpenApiSchemas();
            schemas.GetSchema(typeof(Site));

            var anyOf = schemas.Schemas["Site"]["properties"]["theme"]["anyOf"].AsArray();
            Assert.Equal("#/components/schemas/Shade", anyOf[0]["$ref"]?.GetValue<string>());
            Assert.Equal("null", anyOf[1]["type"]?.GetValue<string>());
        }

        [Fact]
        public void CollectionsReferenceTheirItemsInsteadOfACopy()
        {
            var schemas = new TrakHoundOpenApiSchemas();

            var collection = schemas.GetSchema(typeof(IEnumerable<Page>));

            Assert.Null(collection["$ref"]);
            Assert.Equal("#/components/schemas/Page", GetReference(collection["items"]));
            Assert.False(schemas.Schemas.ContainsKey("IEnumerableOfPage"));
            Assert.Single(schemas.Schemas, o => o.Key.StartsWith("Page"));
        }

        [Fact]
        public void AnEnumWrittenDifferentlyStaysInline()
        {
            var schemas = new TrakHoundOpenApiSchemas();
            schemas.GetSchema(typeof(Site));

            var fallback = schemas.Schemas["Site"]["properties"]["fallback"];
            Assert.Null(GetReference(fallback));
            Assert.Equal("integer", fallback["type"]?.GetValue<string>());
        }

        [Fact]
        public void ASecondUseOfATypeReferencesItsComponent()
        {
            var schemas = new TrakHoundOpenApiSchemas();
            schemas.GetSchema(typeof(Streams));

            // Not a pointer into the first property ("#/components/schemas/Streams/properties/open/items")
            var properties = schemas.Schemas["Streams"]["properties"];
            Assert.Equal("#/components/schemas/Page", GetReference(properties["open"]["items"]));
            Assert.Equal("#/components/schemas/Page", GetReference(properties["closed"]["items"]));
        }

        [Fact]
        public void TypesThatContainEachOtherAreDescribedOnce()
        {
            var schemas = new TrakHoundOpenApiSchemas();

            schemas.GetSchema(typeof(Parent));

            Assert.Equal(new[] { "Child", "Parent" }, schemas.Schemas.Select(o => o.Key).OrderBy(o => o));
            Assert.Equal("#/components/schemas/Child", GetReference(schemas.Schemas["Parent"]["properties"]["child"]));
            Assert.Equal("#/components/schemas/Parent", GetReference(schemas.Schemas["Child"]["properties"]["parent"]));
        }

        [Fact]
        public void ParameterEnumsStayInline()
        {
            var schemas = new TrakHoundOpenApiSchemas();

            var schema = schemas.GetSchema(typeof(Shade));

            Assert.Null(schema["$ref"]);
            Assert.Empty(schemas.Schemas);
        }


        // A reference, or the reference in a nullable property's "anyOf"
        private static string GetReference(JsonNode schema)
        {
            return (schema["$ref"] ?? schema["anyOf"]?[0]?["$ref"])?.GetValue<string>();
        }
    }
}
