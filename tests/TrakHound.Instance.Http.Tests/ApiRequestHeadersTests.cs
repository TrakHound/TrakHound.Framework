// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http;
using TrakHound.Instance.Api;

namespace TrakHound.Instance.Http.Tests
{
    /// <summary>
    /// The Headers the Instance's HTTP pipeline passes to an Api, without the caller's credentials for the Instance
    /// </summary>
    public class ApiRequestHeadersTests
    {
        [Fact]
        public void RequestHeaders_WithholdCredentials()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers["X-TrakHound-Api"] = "abc123";
            context.Request.Headers["Accept"] = "application/json";
            context.Request.Headers["Cookie"] = ".TrakHound.Session=secret";
            context.Request.Headers["Authorization"] = "Bearer secret";
            context.Request.Headers["X-API-Key"] = "secret";
            context.Request.Headers["X-TrakHound-Module-Token"] = "secret";
            context.Request.Headers["token"] = "secret";

            var headers = TrakHoundApiMiddleware.GetRequestHeaders(context);

            Assert.Equal(2, headers.Count);
            Assert.Equal("abc123", headers["x-trakhound-api"]);
            Assert.Equal("application/json", headers["Accept"]);
        }

        [Fact]
        public void RepeatedHeader_IsJoined()
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Append("X-Tag", "a");
            context.Request.Headers.Append("X-Tag", "b");

            Assert.Equal("a,b", TrakHoundApiMiddleware.GetRequestHeaders(context)["X-Tag"]);
        }
    }
}
