// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace TrakHound.Hosting.Tests
{
    public class ApiKeyAndFunctionTests : IAsyncLifetime
    {
        private const string HashedKey = "hashed-secret";
        private const string PlainKey = "plain-secret";

        private WebApplication _app;
        private string _baseUrl;
        private readonly HttpClient _http = new HttpClient();


        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
            {
                ["TrakHound:Parameters:greeting"] = "Hello",
                ["TrakHound:ApiKeys:0:Name"] = "line-1",
                ["TrakHound:ApiKeys:0:Hash"] = TrakHoundApiKeyAuthenticationHandler.GetHash(HashedKey),
                ["TrakHound:ApiKeys:1:Name"] = "line-2",
                ["TrakHound:ApiKeys:1:Key"] = PlainKey
            });

            builder.Services.AddTrakHound(builder.Configuration.GetSection(TrakHoundHostOptions.SectionName));
            builder.Services.AddTrakHoundApiKeyAuthentication();

            _app = builder.Build();
            _app.MapTrakHoundApi(typeof(SampleController).Assembly).Group.RequireAuthorization();
            _app.MapTrakHoundFunction<SampleFunction>("/run");
            await _app.StartAsync();

            _baseUrl = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First().TrimEnd('/');
        }

        public async Task DisposeAsync()
        {
            _http.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }


        [Theory]
        [InlineData(null, null, HttpStatusCode.Unauthorized)]
        [InlineData("X-API-Key", "wrong", HttpStatusCode.Unauthorized)]
        [InlineData("X-API-Key", HashedKey, HttpStatusCode.OK)]
        [InlineData("Authorization", "Bearer " + PlainKey, HttpStatusCode.OK)]
        [InlineData("Authorization", "Bearer not-a-key", HttpStatusCode.Unauthorized)]
        public async Task ApiKey_IsRequired(string header, string value, HttpStatusCode expected)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/greeting");
            if (header != null) request.Headers.TryAddWithoutValidation(header, value);

            using var response = await _http.SendAsync(request);
            Assert.Equal(expected, response.StatusCode);
        }

        [Fact]
        public async Task Function_RunsOverHttp()
        {
            using var response = await _http.PostAsync($"{_baseUrl}/run?punctuation=?", new StringContent("{\"name\":\"Ada\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(200, json["statusCode"].GetValue<int>());
        }

        [Fact]
        public async Task Function_RefusesMissingRequiredParameter()
        {
            using var response = await _http.PostAsync($"{_baseUrl}/run", new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Function_RunsWithParametersAndDefaults()
        {
            var context = _app.Services.GetRequiredService<TrakHoundHostContext>();
            var response = await context.RunFunction<SampleFunction>(NullLogger.Instance, new Dictionary<string, string> { ["name"] = "Ada" });

            Assert.True(response.Success);
            Assert.Equal("Hello Ada!", response.GetParameter("message"));
        }

        [Fact]
        public async Task Function_RunsOnceFromTheConsole()
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string> { ["TrakHound:Input:name"] = "Ada" });
            builder.Services.AddTrakHound(builder.Configuration.GetSection(TrakHoundHostOptions.SectionName));
            using var host = builder.Build();

            Assert.Equal(0, await host.RunTrakHoundFunction<SampleFunction>());
        }
    }
}
