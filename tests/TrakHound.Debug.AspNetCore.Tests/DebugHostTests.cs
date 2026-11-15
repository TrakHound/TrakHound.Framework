// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Debug.AspNetCore;

namespace TrakHound.Debug.AspNetCore.Tests
{
    public class StreamTestController : TrakHoundApiController
    {
        [ApiGet("hello", Description = "Greets the name.")]
        public TrakHoundApiResponse Hello([FromQuery(Description = "Who to greet")] string name) => Ok($"Hello {name}");

        [ApiStream("ticks", Description = "Sends count ticks.")]
        public ITrakHoundStream<TrakHoundApiResponse> Ticks([FromQuery(Description = "How many ticks to send")] int count = 3)
        {
            var stream = new TrakHoundStream<TrakHoundApiResponse>();
            _ = Task.Run(async () =>
            {
                await Task.Delay(200); // Let the client subscribe
                for (var i = 0; i < count; i++) await stream.Push(Ok($"tick {i}"));
            });
            return stream;
        }

        // A GET Endpoint and a Stream on the same Route (as on the Instance, a WebSocket or Server-Sent Events Request goes to the Stream)
        [ApiGet("shared")]
        public TrakHoundApiResponse SharedGet() => Ok("request");

        [ApiStream("shared")]
        public ITrakHoundStream<TrakHoundApiResponse> SharedStream() => Ticks(1);
    }


    public class DebugHostTests : IAsyncLifetime
    {
        private WebApplication _app;
        private string _baseUrl;
        private int _port;
        private readonly HttpClient _http = new HttpClient();


        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            _app = builder.Build();
            _app.MapTrakHoundApiEndpoints(null, null, null, "test", typeof(StreamTestController).Assembly);
            await _app.StartAsync();

            _baseUrl = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First().TrimEnd('/');
            _port = new Uri(_baseUrl).Port;
        }

        public async Task DisposeAsync()
        {
            _http.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }


        [Fact]
        public async Task Request_IsServed()
        {
            Assert.Equal("Hello Ada", await _http.GetStringAsync($"{_baseUrl}/hello?name=Ada"));
        }

        [Fact]
        public async Task Stream_WebSocket_ReceivesMessages()
        {
            var client = new TrakHoundHttpClient(new TrakHoundHttpClientConfiguration("127.0.0.1", _port));
            var stream = await client.Api.Stream("ticks", new Dictionary<string, string> { ["count"] = "3" });
            Assert.NotNull(stream);

            var messages = new List<string>();
            var received = new TaskCompletionSource();
            stream.Received += (sender, response) =>
            {
                lock (messages)
                {
                    messages.Add(response.GetContentUtf8String());
                    if (messages.Count == 3) received.TrySetResult();
                }
            };
            stream.Start();

            await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stream.Dispose();

            Assert.Equal(new[] { "tick 0", "tick 1", "tick 2" }, messages);
        }

        [Fact]
        public async Task Stream_ServerSentEvents_ReceivesMessages()
        {
            var messages = await ReadEvents($"{_baseUrl}/ticks?count=2", 2);

            Assert.Equal(new[] { "tick 0", "tick 1" }, messages.Select(DecodeContent));
        }

        [Fact]
        public async Task SharedRoute_RequestAndStream()
        {
            Assert.Equal("request", await _http.GetStringAsync($"{_baseUrl}/shared"));

            var messages = await ReadEvents($"{_baseUrl}/shared", 1);
            Assert.Equal("tick 0", DecodeContent(messages[0]));
        }

        [Fact]
        public async Task OpenApi_ListsStreamParameters()
        {
            var document = JsonNode.Parse(await _http.GetStringAsync($"{_baseUrl}{WebApplicationExtensions.OpenApiRoute}"));

            var ticks = document["x-trakhound-streams"].AsArray().First(o => o["path"].GetValue<string>() == "/ticks");
            var parameter = ticks["parameters"].AsArray().Single();
            Assert.Equal("count", parameter["name"].GetValue<string>());
            Assert.Equal("How many ticks to send", parameter["description"].GetValue<string>());
        }

        [Fact]
        public async Task Debugger_IsServed()
        {
            var page = await _http.GetAsync($"{_baseUrl}{WebApplicationExtensions.DebuggerRoute}");
            Assert.Equal("text/html", page.Content.Headers.ContentType.MediaType);

            Assert.True((await _http.GetAsync($"{_baseUrl}/_debug/debugger.js")).IsSuccessStatusCode);
            Assert.True((await _http.GetAsync($"{_baseUrl}/_debug/debugger.css")).IsSuccessStatusCode);
            Assert.Equal("image/svg+xml", (await _http.GetAsync($"{_baseUrl}/_debug/logo.svg")).Content.Headers.ContentType.MediaType);
            Assert.Equal("image/svg+xml", (await _http.GetAsync($"{_baseUrl}/_debug/logo-white.svg")).Content.Headers.ContentType.MediaType);
            Assert.Equal(404, (int)(await _http.GetAsync($"{_baseUrl}/_debug/missing.js")).StatusCode);
        }

        [Fact]
        public async Task Logs_IncludeRequests()
        {
            await _http.GetStringAsync($"{_baseUrl}/hello?name=Logged");

            var entries = await ReadEvents($"{_baseUrl}{WebApplicationExtensions.LogsRoute}", 1, o => o.Contains("name=Logged"));
            Assert.Contains("GET /hello?name=Logged : 200", entries[0]);
        }

        [Fact]
        public async Task Stop_DoesNotWaitForTheLogPane()
        {
            await AssertStopsWithOpenEventStream(WebApplicationExtensions.LogsRoute);
        }

        [Fact]
        public async Task Stop_DoesNotWaitForAnEventStream()
        {
            await AssertStopsWithOpenEventStream("/ticks?count=0");
        }

        [Fact]
        public async Task Stop_DoesNotWaitForAWebSocket()
        {
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/ticks?count=0"), CancellationToken.None);

            // Answer the Close frame, as a browser does on its own
            var answerClose = Task.Run(async () =>
            {
                var result = await socket.ReceiveAsync(new byte[1024], CancellationToken.None);
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                return result.MessageType;
            });

            await AssertStops();
            Assert.Equal(WebSocketMessageType.Close, await answerClose);
        }


        private async Task AssertStopsWithOpenEventStream(string path)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{path}");
            request.Headers.Accept.ParseAdd("text/event-stream");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await AssertStops();
        }

        /// <summary>
        /// The host would otherwise wait for the open connection until its ShutdownTimeout (30 seconds)
        /// </summary>
        private async Task AssertStops()
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await _app.StopAsync();
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Stopping took {stopwatch.Elapsed.TotalSeconds:0.0} seconds");
        }


        /// <summary>
        /// Reads Server-Sent Events (the data of each) until count events match
        /// </summary>
        private async Task<List<string>> ReadEvents(string url, int count, Func<string, bool> filter = null)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("text/event-stream");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellation.Token), Encoding.UTF8);

            var events = new List<string>();
            while (events.Count < count)
            {
                var line = await reader.ReadLineAsync(cancellation.Token);
                if (line == null) break;
                if (line.StartsWith("data: ") && (filter == null || filter(line))) events.Add(line.Substring(6));
            }
            return events;
        }

        private static string DecodeContent(string json)
        {
            var content = JsonDocument.Parse(json).RootElement.GetProperty("content").GetString();
            return Encoding.UTF8.GetString(Convert.FromBase64String(content));
        }
    }
}
