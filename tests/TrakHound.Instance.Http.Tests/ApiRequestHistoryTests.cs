// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Instance.Api;

namespace TrakHound.Instance.Http.Tests
{
    public class ApiRequestHistoryTests
    {
        private static TrakHoundApiRequestRecord CreateRequest(string path, int statusCode = 200)
        {
            var requested = DateTime.UtcNow;
            return new TrakHoundApiRequestRecord
            {
                ApiId = "api-1",
                PackageId = "package-1",
                Method = TrakHoundApiMethod.GET,
                Route = "runs/{id}",
                Path = path,
                StatusCode = statusCode,
                Requested = requested,
                Completed = requested.AddMilliseconds(5)
            };
        }

        [Fact]
        public void Keeps_The_Most_Recent_Requests_Newest_First()
        {
            var manager = new TrakHoundApiConnectionManager { RequestHistory = 2 };
            manager.Update(CreateRequest("/runs/1"));
            manager.Update(CreateRequest("/runs/2"));
            manager.Update(CreateRequest("/runs/3"));

            var recent = manager.GetRecentRequests();

            Assert.Equal(2, recent.Capacity);
            Assert.Equal(new[] { "/runs/3", "/runs/2" }, recent.Requests.Select(o => o.Path));
            Assert.Equal(new long[] { 3, 2 }, recent.Requests.Select(o => o.Id));
        }

        [Fact]
        public void Metrics_Are_Kept_When_No_Requests_Are_Kept()
        {
            var manager = new TrakHoundApiConnectionManager { RequestHistory = 0 };
            manager.Update(CreateRequest("/runs/1"));
            manager.Update(CreateRequest("/runs/2", 500));

            Assert.Empty(manager.GetRecentRequests().Requests);

            var metrics = manager.GetMetrics("api-1");
            Assert.Equal(2, metrics.TotalRequestCount);
            Assert.Equal(1, metrics.TotalServerErrorCount);
            Assert.Equal("package-1", metrics.PackageId);
        }

        [Fact]
        public void Request_History_Is_Limited()
        {
            var manager = new TrakHoundApiConnectionManager { RequestHistory = int.MaxValue };

            Assert.Equal(TrakHoundApiConnectionManager.MaxRequestHistory, manager.RequestHistory);
        }

        [Fact]
        public void Requests_Without_An_Api_Are_Ignored()
        {
            var manager = new TrakHoundApiConnectionManager();
            manager.Update(new TrakHoundApiRequestRecord { Path = "/runs/1" });

            Assert.Empty(manager.GetRecentRequests().Requests);
        }
    }
}
