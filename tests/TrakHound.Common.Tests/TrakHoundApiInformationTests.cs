// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// The Parameter Information that TrakHoundApiInformation reads from Endpoint Methods
    /// </summary>
    public class TrakHoundApiInformationTests
    {
        public class TestController
        {
            [ApiPost("items/{id}")]
            public Task<TrakHoundApiResponse> SaveItem(
                [FromRoute(Description = "The Item ID")] string id,
                [FromQuery(Description = "The Item tags")] List<string> tags = null,
                [FromQuery(Description = "The most Items to return")] int? take = 100,
                [FromBody(ContentType = "application/json", Description = "The Item's values")] Dictionary<string, string> values = null
                )
            {
                return Task.FromResult(new TrakHoundApiResponse(200));
            }

            [ApiPost("files")]
            public Task<TrakHoundApiResponse> SaveFile(
                [FromBody(Description = "The file")] byte[] content
                )
            {
                return Task.FromResult(new TrakHoundApiResponse(200));
            }

            [ApiPost("lookup")]
            public Task<TrakHoundApiResponse> SaveLookup(
                [FromBody(ContentType = "application/json", Description = "The lookup")] IReadOnlyDictionary<string, int> lookup
                )
            {
                return Task.FromResult(new TrakHoundApiResponse(200));
            }
        }


        private static Dictionary<string, TrakHoundApiParameterInformation> GetParameters(string route)
        {
            var configuration = new TrakHoundApiConfiguration { Id = "test", Route = "test" };
            var endpoint = TrakHoundApiInformation.GetEndpointInformation(configuration, typeof(TestController)).Single(o => o.Route == route);
            return endpoint.Parameters.ToDictionary(o => o.Name);
        }

        [Fact]
        public void DataType_Dictionary_IsDescribedAsDictionary()
        {
            var parameters = GetParameters("/items/{id}");

            // Not "String[]" (its Key Type), which describes it as a JSON array
            Assert.Equal("Dictionary<String,String>", parameters["values"].DataType);
            Assert.Equal("Dictionary<String,Int32>", GetParameters("/lookup")["lookup"].DataType);
        }

        [Fact]
        public void DataType_ScalarsAndCollections_AreUnchanged()
        {
            var parameters = GetParameters("/items/{id}");

            Assert.Equal("String", parameters["id"].DataType);
            Assert.Equal("String[]", parameters["tags"].DataType);
            Assert.Equal("Int32", parameters["take"].DataType);
            Assert.Equal("100", parameters["take"].DefaultValue);
            Assert.Equal("Byte[]", GetParameters("/files")["content"].DataType);
        }
    }
}
