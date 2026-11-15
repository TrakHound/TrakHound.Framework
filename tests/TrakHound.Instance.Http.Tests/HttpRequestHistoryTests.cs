// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Http;

namespace TrakHound.Instance.Http.Tests
{
    public class HttpRequestHistoryTests
    {
        [Fact]
        public void Keeps_The_Most_Recent_Requests_Newest_First()
        {
            var history = new TrakHoundHttpRequestHistory { Capacity = 2 };
            history.Add(new TrakHoundHttpRequestRecord { Url = "/a" });
            history.Add(new TrakHoundHttpRequestRecord { Url = "/b" });
            history.Add(new TrakHoundHttpRequestRecord { Url = "/c" });

            var recent = history.Get();

            Assert.Equal(2, recent.Capacity);
            Assert.Equal(new[] { "/c", "/b" }, recent.Requests.Select(o => o.Url));
            Assert.Equal(new long[] { 3, 2 }, recent.Requests.Select(o => o.Id));
        }

        [Fact]
        public void Capacity_Zero_Keeps_None()
        {
            var history = new TrakHoundHttpRequestHistory { Capacity = 0 };
            history.Add(new TrakHoundHttpRequestRecord { Url = "/a" });

            var recent = history.Get();

            Assert.Equal(0, recent.Capacity);
            Assert.Empty(recent.Requests);
        }

        [Fact]
        public void Capacity_Is_Limited()
        {
            var history = new TrakHoundHttpRequestHistory { Capacity = int.MaxValue };

            Assert.Equal(TrakHoundHttpRequestHistory.MaxCapacity, history.Capacity);
        }

        [Fact]
        public void Default_Capacity_Is_Serialized()
        {
            var configuration = new TrakHoundInstanceMetricsConfiguration { HttpRequestHistory = 0 };

            var json = Json.Convert(configuration);
            var read = Json.Convert<TrakHoundInstanceMetricsConfiguration>(json);

            Assert.Equal(0, read.HttpRequestHistory);
        }
    }
}
