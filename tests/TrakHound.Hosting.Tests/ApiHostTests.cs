// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;

namespace TrakHound.Hosting.Tests
{
    public class ApiHostTests : IAsyncLifetime
    {
        private const string UserHeader = "X-Test-User";

        private WebApplication _app;
        private string _baseUrl;
        private readonly HttpClient _http = new HttpClient();


        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string> { ["TrakHound:Parameters:greeting"] = "Hello" });

            builder.Services.AddTrakHound(builder.Configuration.GetSection(TrakHoundHostOptions.SectionName));
            builder.Services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.SchemeName, null);
            builder.Services.AddAuthorization();

            _app = builder.Build();
            var endpoints = _app.MapTrakHoundApi(typeof(SampleController).Assembly);
            endpoints.Group.RequireAuthorization();
            _app.MapTrakHoundOpenApi(endpoints);
            await _app.StartAsync();

            _baseUrl = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First().TrimEnd('/');
        }

        public async Task DisposeAsync()
        {
            _http.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }


        [Fact]
        public async Task Request_GetsParametersFromSettings()
        {
            using var response = await _http.SendAsync(CreateRequest("/greeting", authenticated: true));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Hello", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Request_RequiresAuthorization()
        {
            using var response = await _http.SendAsync(CreateRequest("/greeting", authenticated: false));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Stream_RequiresAuthorization()
        {
            using var denied = await _http.SendAsync(CreateRequest("/ticks", authenticated: false, eventStream: true), HttpCompletionOption.ResponseHeadersRead);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = await _http.SendAsync(CreateRequest("/ticks", authenticated: true, eventStream: true), HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellation.Token));
            string line;
            while ((line = await reader.ReadLineAsync(cancellation.Token)) != null && !line.StartsWith("data: ")) { }
            Assert.NotNull(line);
        }

        [Fact]
        public async Task OpenApi_IsServedWithoutAuthorization()
        {
            var document = JsonNode.Parse(await _http.GetStringAsync($"{_baseUrl}/openapi.json"));
            // Without trakhound.package.json, the version is the Controllers' assembly version
            Assert.Equal(typeof(SampleController).Assembly.GetName().Version.ToString(3), document["info"]["version"].GetValue<string>());
            Assert.NotNull(document["paths"]["/greeting"]);
        }

        [Fact]
        public async Task HeaderParameters_AreBound_WithoutCredentials()
        {
            var request = CreateRequest("/headers", authenticated: true);
            request.Headers.Add("x-trakhound-api", "abc123");
            request.Headers.Add("COUNT", "5");
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret");

            using var response = await _http.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("abc123|withheld|5", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task MissingHeaders_BindNullOrTheDefault()
        {
            using var response = await _http.SendAsync(CreateRequest("/headers", authenticated: true));
            Assert.Equal("null|withheld|-1", await response.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task OpenApi_DescribesHeaderParameters()
        {
            var document = JsonNode.Parse(await _http.GetStringAsync($"{_baseUrl}/openapi.json"));
            var parameters = document["paths"]["/headers"]["get"]["parameters"].AsArray();

            var token = parameters.Single(o => o["name"].GetValue<string>() == "X-TrakHound-Api");
            Assert.Equal("header", token["in"].GetValue<string>());
            Assert.True(token["required"].GetValue<bool>());
            Assert.Equal("The caller's access token", token["description"].GetValue<string>());

            var count = parameters.Single(o => o["name"].GetValue<string>() == "count");
            Assert.Equal("header", count["in"].GetValue<string>());
            Assert.False(count["required"].GetValue<bool>());
        }

        [Fact]
        public void MapTrakHoundApi_WithoutAddTrakHound_SaysWhy()
        {
            var app = WebApplication.CreateBuilder().Build();
            var exception = Assert.Throws<InvalidOperationException>(() => app.MapTrakHoundApi(typeof(SampleController).Assembly));
            Assert.Contains("AddTrakHound", exception.Message);
        }


        private HttpRequestMessage CreateRequest(string path, bool authenticated, bool eventStream = false)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{path}");
            if (authenticated) request.Headers.Add(UserHeader, "tester");
            if (eventStream) request.Headers.Accept.ParseAdd("text/event-stream");
            return request;
        }


        /// <summary>
        /// Signs in any Request with the X-Test-User header
        /// </summary>
        private class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
        {
            public const string SchemeName = "Test";

            public TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

            protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            {
                var user = Request.Headers[UserHeader].FirstOrDefault();
                if (string.IsNullOrEmpty(user)) return Task.FromResult(AuthenticateResult.NoResult());

                var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, user) }, SchemeName);
                return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
            }
        }
    }
}
