// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Redis.Driver;
using TrakHound.Services;

namespace TrakHound.Redis.Service.Tests
{
    /// <summary>
    /// Runs when TRAKHOUND_REDIS_TEST_SERVER is set (optionally with TRAKHOUND_REDIS_TEST_PORT and TRAKHOUND_REDIS_TEST_PASSWORD).
    /// Each test uses its own Collections and deletes its Keys, so any Redis database can be used
    /// </summary>
    public class RedisKeyValueServiceTests : IAsyncLifetime
    {
        private const string Prefix = "TRAKHOUND_REDIS_TEST_";

        private readonly string _collection = $"tests:{Guid.NewGuid():N}";
        private readonly string _otherCollection = $"tests-other-{Guid.NewGuid():N}";
        private RedisKeyValueService _service;


        public Task InitializeAsync()
        {
            var server = Environment.GetEnvironmentVariable($"{Prefix}SERVER");
            if (!string.IsNullOrEmpty(server))
            {
                var configuration = new TrakHoundServiceConfiguration { Id = "redis-tests", Name = "redis-tests" };
                configuration.SetParameter("server", server);
                configuration.SetParameter("port", Environment.GetEnvironmentVariable($"{Prefix}PORT") ?? "0");
                configuration.SetParameter("password", Environment.GetEnvironmentVariable($"{Prefix}PASSWORD"));
                _service = new RedisKeyValueService(configuration);
            }
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            if (_service != null)
            {
                foreach (var collection in new[] { _collection, _otherCollection })
                {
                    var keys = await _service.ListKeys(collection, "*", 0, 10000);
                    if (keys.Success) await _service.Delete(collection, keys.Result);
                }
                _service.Dispose();
            }
        }

        private RedisKeyValueService Service
        {
            get
            {
                Skip.If(_service == null, $"Set {Prefix}SERVER to run the Redis tests");
                return _service;
            }
        }

        private Task Publish(string collection, params string[] keys)
        {
            return Service.Publish(collection, keys.ToDictionary(o => o, o => $"value-{o}"));
        }


        [SkippableFact]
        public async Task A_published_value_can_be_read()
        {
            await Publish(_collection, "a");

            var response = await Service.Read(_collection, new[] { "a", "missing" });

            Assert.True(response.Success);
            Assert.Equal("value-a", response.Result.Values["a"]);
            Assert.Null(response.Result.Values["missing"]);
        }

        [SkippableFact]
        public async Task A_key_is_only_read_from_its_own_collection()
        {
            await Publish(_collection, "a");

            var response = await Service.Read(_otherCollection, new[] { "a" });

            Assert.Null(response.Result.Values["a"]);
        }

        [SkippableFact]
        public async Task Count_and_ListKeys_only_include_the_collection()
        {
            await Publish(_collection, "a", "b");
            await Publish(_otherCollection, "c");

            Assert.Equal(2, (await Service.Count(_collection)).Result);
            Assert.Equal(new[] { "a", "b" }, (await Service.ListKeys(_collection, "*", 0, 100)).Result);
        }

        [SkippableFact]
        public async Task ListCollections_returns_the_collection_names_unescaped()
        {
            await Publish(_collection, "a:b");

            var response = await Service.ListCollections();

            Assert.Contains(_collection, response.Result);
        }

        [SkippableFact]
        public async Task ListKeys_matches_the_wildcard_and_keeps_other_characters_literal()
        {
            await Publish(_collection, "line-1", "line-2", "line?", "other");

            Assert.Equal(new[] { "line-1", "line-2", "line?" }, (await Service.ListKeys(_collection, "line*", 0, 100)).Result);
            Assert.Equal(new[] { "line?" }, (await Service.ListKeys(_collection, "line?", 0, 100)).Result);
        }

        [SkippableFact]
        public async Task ListKeys_pages_in_a_stable_order()
        {
            await Publish(_collection, "d", "b", "a", "c");

            Assert.Equal(new[] { "b", "c" }, (await Service.ListKeys(_collection, "*", 1, 2)).Result);
        }

        [SkippableFact]
        public async Task A_key_with_a_ttl_expires()
        {
            var entries = new Dictionary<string, string> { { "a", "1" } };
            await Service.Publish(_collection, entries, TimeSpan.FromMilliseconds(100));

            await Task.Delay(500);

            Assert.Null((await Service.Read(_collection, new[] { "a" })).Result.Values["a"]);
        }

        [SkippableFact]
        public async Task A_deleted_key_is_gone()
        {
            await Publish(_collection, "a", "b");

            var response = await Service.Delete(_collection, new[] { "a" });

            Assert.True(response.Success);
            Assert.Equal(1, (await Service.Count(_collection)).Result);
        }

        [SkippableFact]
        public async Task CheckAvailability_reports_a_reachable_server()
        {
            var response = await Service.CheckAvailability();

            Assert.True(response.Result.IsAvailable);
        }

        [Fact]
        public async Task An_unconfigured_service_is_not_available()
        {
            using var service = new RedisKeyValueService(new TrakHoundServiceConfiguration { Id = "redis-tests", Name = "redis-tests" });

            Assert.False((await service.CheckAvailability()).Result.IsAvailable);
            Assert.False((await service.Read("collection", new[] { "a" })).Success);
        }
    }
}
