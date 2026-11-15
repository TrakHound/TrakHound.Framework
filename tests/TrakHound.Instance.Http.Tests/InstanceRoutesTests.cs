// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http;

namespace TrakHound.Instance.Http.Tests
{
    /// <summary>
    /// The fixed route prefixes are matched like endpoint routing matches Controllers (by whole segment, ignoring case), so a request can't
    /// reach a System API Controller without being recognized as a System API request
    /// </summary>
    public class InstanceRoutesTests
    {
        [Theory]
        [InlineData("/_logs", true)]
        [InlineData("/_logs/loggers", true)]
        [InlineData("/_LOGS/loggers", true)]
        [InlineData("/_Api/reload/orders", true)]
        [InlineData("/_services/keyvalues", true)]
        [InlineData("/_services/time-series/read", true)]
        [InlineData("/_mcp", true)]
        [InlineData("/_logsx", false)]
        [InlineData("/_apiexplorer", false)]
        [InlineData("/orders/_logs", false)]
        [InlineData("/", false)]
        public void IsSystemApiRoute(string path, bool expected)
        {
            Assert.Equal(expected, TrakHoundInstanceRoutes.IsSystemApiRoute(new PathString(path)));
        }

        [Theory]
        [InlineData("/_blazor/negotiate", true)]
        [InlineData("/_BLAZOR/negotiate", true)]
        [InlineData("/_framework/blazor.web.js", true)]
        [InlineData("/TrakHound.Instance.styles.css", true)]
        [InlineData("/css/site.css", true)]
        [InlineData("/cssx/site.css", false)]
        [InlineData("/_logs", false)]
        public void IsStaticRoute(string path, bool expected)
        {
            Assert.Equal(expected, TrakHoundInstanceRoutes.IsStaticRoute(new PathString(path)));
        }

        [Theory]
        [InlineData("/_error", true)]
        [InlineData("/_ERROR/404", true)]
        [InlineData("/_errors", false)]
        public void IsErrorRoute(string path, bool expected)
        {
            Assert.Equal(expected, TrakHoundInstanceRoutes.IsErrorRoute(new PathString(path)));
        }
    }
}
