// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Redis.Driver;
using TrakHound.Services;

namespace TrakHound.Redis.Service.Tests
{
    /// <summary>
    /// A server that can't be reached returns 503 from every operation, so that a Service Router tries its next Target.
    /// These run without a Redis server
    /// </summary>
    public class RedisUnavailableTests : IDisposable
    {
        private readonly RedisKeyValueService _service;


        public RedisUnavailableTests()
        {
            // Nothing listens on port 1, so the connection is refused straight away
            var configuration = new TrakHoundServiceConfiguration { Id = "redis-unavailable", Name = "redis-unavailable" };
            configuration.SetParameter("server", "127.0.0.1");
            configuration.SetParameter("port", "1");
            _service = new RedisKeyValueService(configuration);
        }

        public void Dispose() => _service.Dispose();


        [Fact]
        public async Task Read_Returns503()
        {
            Assert.Equal(503, (await _service.Read("tests", new[] { "a" })).StatusCode);
        }

        [Fact]
        public async Task Publish_Returns503()
        {
            Assert.Equal(503, (await _service.Publish("tests", new Dictionary<string, string> { ["a"] = "1" })).StatusCode);
        }

        [Fact]
        public async Task Delete_Returns503()
        {
            Assert.Equal(503, (await _service.Delete("tests", new[] { "a" })).StatusCode);
        }

        [Fact]
        public async Task ListKeys_Returns503()
        {
            Assert.Equal(503, (await _service.ListKeys("tests", "*", 0, 10)).StatusCode);
        }

        [Fact]
        public async Task NotConfigured_Returns503()
        {
            using var service = new RedisKeyValueService(new TrakHoundServiceConfiguration { Id = "redis-empty", Name = "redis-empty" });

            Assert.Equal(503, (await service.Read("tests", new[] { "a" })).StatusCode);
        }
    }
}
