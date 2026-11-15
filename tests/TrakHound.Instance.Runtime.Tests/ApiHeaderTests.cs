// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Instance.Api;
using TrakHound.Instances;
using TrakHound.Volumes;

namespace TrakHound.Instance.Runtime.Tests
{
    public class HeaderTestController : TrakHoundApiController
    {
        public static string StreamToken;


        [ApiGet("echo", Description = "Returns the Header Parameters it was sent.")]
        public TrakHoundApiResponse Echo(
            [FromHeader("X-TrakHound-Api", Description = "The caller's access token")] string token,
            [FromHeader("Authorization")] string authorization = "withheld",
            [FromHeader("Cookie")] string cookie = "withheld",
            [FromHeader("token")] string identityToken = "withheld",
            [FromHeader("X-TrakHound-Module-Token")] string moduleToken = "withheld",
            [FromHeader("X-API-Key")] string apiKey = "withheld",
            [FromHeader] int count = -1
            )
        {
            return Ok(string.Join("|", token ?? "null", authorization, cookie, identityToken, moduleToken, apiKey, count));
        }

        [ApiStream("stream", Description = "Opens a Stream with the Header Parameters it was sent.")]
        public ITrakHoundStream<TrakHoundApiResponse> Stream([FromHeader("X-TrakHound-Api")] string token)
        {
            StreamToken = token;
            return new TrakHoundStream<TrakHoundApiResponse>();
        }
    }


    /// <summary>
    /// [FromHeader] Parameters bound by the Instance's Api Provider (the Instance Client passes the Headers through), without the Withheld Headers
    /// </summary>
    public class ApiHeaderTests : IAsyncLifetime
    {
        private readonly RuntimeManagerFixture _fixture = new RuntimeManagerFixture(typeof(HeaderTestController), "api");
        private TrakHoundApiProvider _provider;
        private ITrakHoundApiClient _client;


        public async Task InitializeAsync()
        {
            var instance = Fake.Create<ITrakHoundInstance>(o => o
                .On("get_Id", _ => "test-instance")
                .On("get_Information", _ => new TrakHoundInstanceInformation())
                .On("get_Configuration", _ => new TrakHoundInstanceConfiguration())
                .On("get_SecurityManager", _ => Fake.Create<TrakHound.Security.ITrakHoundSecurityManager>()));
            var volumeProvider = Fake.Create<ITrakHoundVolumeProvider>(o => o.On("GetVolume", _ => Fake.Create<ITrakHoundVolume>()));

            _provider = new TrakHoundApiProvider(instance, _fixture.ConfigurationProfile, _fixture.ModuleProvider, _fixture.ClientProvider, volumeProvider, _fixture.VariableProvider, _fixture.LogProvider, _fixture.PackageManager, new TrakHoundApiConnectionManager(), new TrakHoundApiStreamManager());

            _fixture.ConfigurationProfile.Add(new TrakHoundApiConfiguration { Id = "headers", Route = "/headers", PackageId = "Test.Package", PackageVersion = "1.0.0" });
            await _provider.Load();
            Assert.True(_provider.IsRouteValid("/headers/echo"));

            _client = new TrakHoundInstanceApiClient(null, _provider);
        }

        public Task DisposeAsync()
        {
            _provider.Dispose();
            _fixture.Dispose();
            return Task.CompletedTask;
        }


        [Fact]
        public async Task HeaderParameters_AreBound_CaseInsensitive()
        {
            var headers = new Dictionary<string, string> { ["x-trakhound-api"] = "abc123", ["COUNT"] = "5" };

            var response = await _client.Get("/headers/echo", null, headers);

            Assert.Equal(200, response.StatusCode);
            Assert.Equal("abc123|withheld|withheld|withheld|withheld|withheld|5", response.GetContentUtf8String());
        }

        [Fact]
        public async Task MissingHeaders_BindNullOrTheDefault()
        {
            var response = await _client.Get("/headers/echo", null, new Dictionary<string, string>());

            Assert.Equal("null|withheld|withheld|withheld|withheld|withheld|-1", response.GetContentUtf8String());
        }

        [Fact]
        public async Task WithheldHeaders_AreNotBound()
        {
            var headers = new Dictionary<string, string>
            {
                ["X-TrakHound-Api"] = "abc123",
                ["Authorization"] = "Bearer secret",
                ["cookie"] = ".TrakHound.Session=secret",
                ["Token"] = "secret",
                ["X-TrakHound-Module-Token"] = "secret",
                ["x-api-key"] = "secret"
            };

            var response = await _client.Send(TrakHoundApiMethod.GET, "/headers/echo", null, null, null, headers);

            Assert.Equal("abc123|withheld|withheld|withheld|withheld|withheld|-1", response.GetContentUtf8String());
        }

        [Fact]
        public async Task StreamHeaderParameters_AreBound()
        {
            HeaderTestController.StreamToken = null;

            using var stream = await _client.Stream("/headers/stream", null, new Dictionary<string, string> { ["X-TrakHound-Api"] = "abc123" });

            Assert.NotNull(stream);
            Assert.Equal("abc123", HeaderTestController.StreamToken);
        }

        [Fact]
        public void Information_ListsHeaderParameters()
        {
            var endpoint = _provider.GetInformation("headers").Controllers.SelectMany(o => o.EndPoints).First(o => o.Route.EndsWith("echo"));
            var token = endpoint.Parameters.First(o => o.Name == "X-TrakHound-Api");

            Assert.Equal("Header", token.Type);
            Assert.True(token.Required);
            Assert.Equal("The caller's access token", token.Description);
            Assert.Contains(endpoint.Parameters, o => o.Name == "count" && o.Type == "Header" && !o.Required);
        }
    }
}
