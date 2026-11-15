// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Http;

namespace TrakHound.Instance.Http.Tests
{
    public class HttpQueryMaskTests
    {
        [Theory]
        [InlineData("?token=abc", "?token=***")]
        [InlineData("?access_token=abc&take=10", "?access_token=***&take=10")]
        [InlineData("?apiKey=abc", "?apiKey=***")]
        [InlineData("?x-api-key=abc", "?x-api-key=***")]
        [InlineData("?client_secret=abc&code=xyz&state=1", "?client_secret=***&code=***&state=1")]
        [InlineData("?Password=abc", "?Password=***")]
        [InlineData("?key=abc", "?key=***")]
        [InlineData("?api%5Fkey=abc", "?api%5Fkey=***")]
        public void Sensitive_Values_Are_Masked(string query, string expected)
        {
            Assert.Equal(expected, TrakHoundHttpQueryMask.Apply(query));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("?take=10&skip=0")]
        [InlineData("?keyword=abc")]
        [InlineData("?token=")]
        [InlineData("?token")]
        public void Other_Queries_Are_Unchanged(string query)
        {
            Assert.Equal(query, TrakHoundHttpQueryMask.Apply(query));
        }
    }
}
