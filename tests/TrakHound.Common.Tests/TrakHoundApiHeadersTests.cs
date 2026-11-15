// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// The Request Headers that are passed to Apis ([FromHeader] Parameters)
    /// </summary>
    public class TrakHoundApiHeadersTests
    {
        [Theory]
        [InlineData("Cookie")]
        [InlineData("authorization")]
        [InlineData("Proxy-Authorization")]
        [InlineData("X-API-KEY")]
        [InlineData("x-trakhound-module-token")]
        [InlineData("Token")]
        public void CredentialHeaders_AreWithheld(string name)
        {
            Assert.True(TrakHoundApiHeaders.IsWithheld(name));
            Assert.Null(TrakHoundApiHeaders.Filter(new[] { new KeyValuePair<string, string>(name, "secret") }));
        }

        [Fact]
        public void PackageSourceTokenHeader_IsPassed()
        {
            // A Package Source sends its Access Token in X-TrakHound-Api (see TrakHoundPackageClient)
            Assert.False(TrakHoundApiHeaders.IsWithheld("X-TrakHound-Api"));
        }

        [Fact]
        public void Filter_KeepsOtherHeaders_CaseInsensitive()
        {
            var headers = TrakHoundApiHeaders.Filter(new Dictionary<string, string>
            {
                ["X-TrakHound-Api"] = "abc123",
                ["Authorization"] = "Bearer secret",
                ["Accept"] = "application/json"
            });

            Assert.Equal(2, headers.Count);
            Assert.Equal("abc123", headers["x-trakhound-api"]);
            Assert.Equal("application/json", headers["ACCEPT"]);
            Assert.False(headers.ContainsKey("Authorization"));
        }

        [Fact]
        public void Filter_WithoutHeaders_IsNull()
        {
            Assert.Null(TrakHoundApiHeaders.Filter(null));
            Assert.Null(TrakHoundApiHeaders.Filter(new Dictionary<string, string>()));
        }
    }
}
