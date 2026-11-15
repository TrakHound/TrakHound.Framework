// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http;
using System.Net;
using TrakHound.Security;

namespace TrakHound.Instance.Http.Tests
{
    public class RateLimiterTests
    {
        private static TrakHoundInstanceRateLimitConfiguration CreateConfiguration(int permitLimit, int window = 60, int concurrencyLimit = 0)
        {
            return new TrakHoundInstanceRateLimitConfiguration
            {
                Enabled = true,
                Api = new TrakHoundRateLimit(permitLimit, window, concurrencyLimit),
                SystemApi = new TrakHoundRateLimit(permitLimit, window, concurrencyLimit),
                McpTools = new TrakHoundRateLimit(permitLimit, window, concurrencyLimit)
            };
        }

        [Fact]
        public void Rejects_Requests_Over_The_Permit_Limit()
        {
            using var limiter = new TrakHoundRateLimiter(() => CreateConfiguration(3));

            for (var i = 0; i < 3; i++)
            {
                using var lease = limiter.Acquire(TrakHoundRateLimitAreas.Api, "a");
                Assert.True(lease.IsAcquired);
            }

            using var rejected = limiter.Acquire(TrakHoundRateLimitAreas.Api, "a");
            Assert.False(rejected.IsAcquired);
            Assert.NotNull(rejected.Reason);
            Assert.True(rejected.GetRetryAfterSeconds() >= 1);
        }

        [Fact]
        public void Each_Caller_And_Area_Is_Limited_Separately()
        {
            using var limiter = new TrakHoundRateLimiter(() => CreateConfiguration(1));

            Assert.True(limiter.Acquire(TrakHoundRateLimitAreas.Api, "a").IsAcquired);
            Assert.False(limiter.Acquire(TrakHoundRateLimitAreas.Api, "a").IsAcquired);

            Assert.True(limiter.Acquire(TrakHoundRateLimitAreas.Api, "b").IsAcquired);
            Assert.True(limiter.Acquire(TrakHoundRateLimitAreas.SystemApi, "a").IsAcquired);
            Assert.True(limiter.Acquire(TrakHoundRateLimitAreas.McpTools, "a").IsAcquired);
        }

        [Fact]
        public void Nothing_Is_Limited_When_Disabled()
        {
            var configuration = CreateConfiguration(1);
            configuration.Enabled = false;
            using var limiter = new TrakHoundRateLimiter(() => configuration);

            for (var i = 0; i < 10; i++) Assert.True(limiter.Acquire(TrakHoundRateLimitAreas.Api, "a").IsAcquired);
        }

        [Fact]
        public void Zero_Limits_Allow_Everything()
        {
            using var limiter = new TrakHoundRateLimiter(() => CreateConfiguration(0, 60, 0));

            for (var i = 0; i < 10; i++) Assert.True(limiter.Acquire(TrakHoundRateLimitAreas.Api, "a").IsAcquired);
        }

        [Fact]
        public void Concurrency_Limit_Is_Released_When_The_Lease_Is_Disposed()
        {
            using var limiter = new TrakHoundRateLimiter(() => CreateConfiguration(0, 60, 1));

            var first = limiter.Acquire(TrakHoundRateLimitAreas.Api, "a");
            Assert.True(first.IsAcquired);
            Assert.False(limiter.Acquire(TrakHoundRateLimitAreas.Api, "a").IsAcquired);

            first.Dispose();
            Assert.True(limiter.Acquire(TrakHoundRateLimitAreas.Api, "a").IsAcquired);
        }

        [Fact]
        public void Streams_Are_Not_Counted_Toward_The_Concurrency_Limit()
        {
            using var limiter = new TrakHoundRateLimiter(() => CreateConfiguration(0, 60, 1));

            using var stream = limiter.Acquire(TrakHoundRateLimitAreas.Api, "a", countConcurrency: false);
            Assert.True(stream.IsAcquired);
            using var request = limiter.Acquire(TrakHoundRateLimitAreas.Api, "a");
            Assert.True(request.IsAcquired);
        }

        [Fact]
        public void A_Request_Rejected_By_The_Permit_Limit_Releases_Its_Concurrency()
        {
            using var limiter = new TrakHoundRateLimiter(() => CreateConfiguration(1, 60, 1));

            limiter.Acquire(TrakHoundRateLimitAreas.Api, "a").Dispose();
            Assert.False(limiter.Acquire(TrakHoundRateLimitAreas.Api, "a").IsAcquired); // Permit Limit

            // The rejected Request didn't keep its place in the Concurrency Limit (it would otherwise be rejected for Concurrency)
            Assert.Contains("every", limiter.Acquire(TrakHoundRateLimitAreas.Api, "a").Reason);
        }

        [Fact]
        public void Changed_Limits_Apply_To_The_Next_Request()
        {
            var configuration = CreateConfiguration(1, 60, 1);
            using var limiter = new TrakHoundRateLimiter(() => configuration);

            var running = limiter.Acquire(TrakHoundRateLimitAreas.Api, "a");
            Assert.False(limiter.Acquire(TrakHoundRateLimitAreas.Api, "a").IsAcquired);

            configuration.Api = new TrakHoundRateLimit(5, 60, 5);
            Assert.True(limiter.Acquire(TrakHoundRateLimitAreas.Api, "a").IsAcquired);

            // A Request that started before the change completes normally
            running.Dispose();
        }

        [Fact]
        public void Configuration_Is_Saved_And_Read()
        {
            var path = Path.Combine(Path.GetTempPath(), $"trakhound-ratelimits-{Guid.NewGuid():N}.yaml");
            try
            {
                var configuration = new TrakHound.Instances.TrakHoundInstanceConfiguration();
                configuration.RateLimits.Enabled = true;
                configuration.RateLimits.Api = new TrakHoundRateLimit(0, 30, 0);
                configuration.RateLimits.McpTools = new TrakHoundRateLimit(10, 5, 2);
                configuration.Save(path, false);

                var read = TrakHound.Instances.TrakHoundInstanceConfiguration.Read(path);

                Assert.True(read.RateLimits.Enabled);
                Assert.Equal(0, read.RateLimits.Api.PermitLimit);
                Assert.Equal(30, read.RateLimits.Api.Window);
                Assert.Equal(0, read.RateLimits.Api.ConcurrencyLimit);
                Assert.Equal(600, read.RateLimits.SystemApi.PermitLimit);
                Assert.Equal(10, read.RateLimits.McpTools.PermitLimit);
                Assert.Equal(5, read.RateLimits.McpTools.Window);
                Assert.Equal(2, read.RateLimits.McpTools.ConcurrencyLimit);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void Caller_Key_Uses_The_Identity_Or_The_Address()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:10.0.0.5");

            var identity = new TrakHoundIdentity { ProviderId = "api-keys", UserId = "agent" };

            Assert.Equal("identity:api-keys:agent", TrakHoundRateLimiter.GetCallerKey(identity, httpContext));
            Assert.Equal("address:10.0.0.5", TrakHoundRateLimiter.GetCallerKey(null, httpContext));
        }
    }
}
