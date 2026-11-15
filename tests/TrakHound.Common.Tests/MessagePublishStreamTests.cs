using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using TrakHound.Clients;
using TrakHound.Http;
using TrakHound.Services;
using TrakHound.Services.Messages;

namespace TrakHound.Common.Tests;

public class MessagePublishStreamTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);


    [Fact]
    public void PublishFrame_RoundTrips()
    {
        var frame = TrakHoundHttpMessageResponse.CreatePublish("a/b", true, 2, Encoding.UTF8.GetBytes("hello"));

        Assert.True(TrakHoundHttpMessageResponse.TryParsePublish(frame, "default", false, 0, out var message, out var error));
        Assert.Null(error);
        Assert.Equal("a/b", message.Topic);
        Assert.True(message.Retain);
        Assert.Equal(2, message.Qos);
        Assert.Equal("hello", message.GetContentUtf8String());
    }

    [Fact]
    public void PublishFrame_EmptyFields_UseDefaults()
    {
        var frame = TrakHoundHttpMessageResponse.CreatePublish(null, null, null, Array.Empty<byte>());

        Assert.True(TrakHoundHttpMessageResponse.TryParsePublish(frame, "default/topic", true, 1, out var message, out _));
        Assert.Equal("default/topic", message.Topic);
        Assert.True(message.Retain);
        Assert.Equal(1, message.Qos);
        Assert.Empty(message.GetContentBytes());
    }

    [Fact]
    public void PublishFrame_ExplicitFalseAndZero_OverrideDefaults()
    {
        var frame = TrakHoundHttpMessageResponse.CreatePublish("t", false, 0, new byte[] { 1 });

        Assert.True(TrakHoundHttpMessageResponse.TryParsePublish(frame, null, true, 2, out var message, out _));
        Assert.False(message.Retain);
        Assert.Equal(0, message.Qos);
    }

    [Theory]
    [InlineData("t\r1\r3\r\r", "Invalid QoS (3)")]
    [InlineData("t\rx\r0\r\r", "Invalid Retain (x)")]
    [InlineData("\r\r\r\r", "No Topic Specified")]
    public void PublishFrame_InvalidFields_AreRejected(string header, string expectedError)
    {
        var frame = Encoding.UTF8.GetBytes(header).Concat(new byte[] { 10, 13, 65 }).ToArray();

        Assert.False(TrakHoundHttpMessageResponse.TryParsePublish(frame, null, false, 0, out _, out var error));
        Assert.StartsWith(expectedError, error);
    }

    [Fact]
    public void PublishFrame_WithoutDivider_IsRejected()
    {
        Assert.False(TrakHoundHttpMessageResponse.TryParsePublish(Encoding.UTF8.GetBytes("just content"), "t", false, 0, out _, out var error));
        Assert.StartsWith("Invalid Message Frame", error);
    }

    [Fact]
    public void CreatePublish_RejectsTopicWithLineBreaks()
    {
        Assert.Null(TrakHoundHttpMessageResponse.CreatePublish("a\nb", null, null, new byte[0]));
        Assert.Null(TrakHoundHttpMessageResponse.CreatePublish("a", null, null, null));
    }

    [Theory]
    [InlineData("a/b", 0, true, null)]
    [InlineData(null, 0, true, "No Topic Specified")]
    [InlineData(null, 0, false, null)]
    [InlineData("a\rb", 0, true, "Invalid Topic : Can't contain CR or LF")]
    [InlineData("a", 3, true, "Invalid QoS (3) : Must be 0, 1, or 2")]
    public void GetPublishError_ChecksTopicAndQos(string topic, int qos, bool requireTopic, string expected)
    {
        Assert.Equal(expected, TrakHoundMessageTopics.GetPublishError(topic, qos, requireTopic));
    }


    [Fact]
    public async Task PublishStream_ReportsFailures()
    {
        var published = new List<string>();
        var failures = new List<TrakHoundMessagePublishFailure>();

        using var stream = new TrakHoundMessagePublishStream<string>((item, token) =>
        {
            if (item == "bad") return Task.FromResult(new TrakHoundMessagePublishFailure("t", 403, "Not Allowed"));
            published.Add(item);
            return Task.FromResult<TrakHoundMessagePublishFailure>(null);
        });
        stream.PublishFailed += (s, f) => failures.Add(f);

        Assert.True(await stream.Push("good"));
        Assert.False(await stream.Push("bad"));

        Assert.Equal(new[] { "good" }, published);
        var failure = Assert.Single(failures);
        Assert.Equal(403, failure.StatusCode);
        Assert.Equal("Not Allowed", failure.Message);
    }

    [Fact]
    public async Task PublishStream_ReportsExceptionsAsFailures()
    {
        var failures = new List<TrakHoundMessagePublishFailure>();
        using var stream = new TrakHoundMessagePublishStream<string>((item, token) => throw new InvalidOperationException("boom"));
        stream.PublishFailed += (s, f) => failures.Add(f);

        Assert.False(await stream.Push("a"));
        Assert.Equal(500, Assert.Single(failures).StatusCode);
    }

    [Fact]
    public async Task PublishStream_PublishesOneAtATime_InOrder()
    {
        var published = new ConcurrentQueue<int>();
        var active = 0;
        var maxActive = 0;

        using var stream = new TrakHoundMessagePublishStream<int>(async (item, token) =>
        {
            var now = Interlocked.Increment(ref active);
            InterlockedMax(ref maxActive, now);
            await Task.Delay(5, token);
            published.Enqueue(item);
            Interlocked.Decrement(ref active);
            return null;
        });

        var pushes = new List<Task<bool>>();
        for (var i = 0; i < 10; i++) pushes.Add(stream.Push(i));
        await Task.WhenAll(pushes);

        Assert.Equal(1, maxActive);
        Assert.Equal(Enumerable.Range(0, 10), published);
    }

    [Fact]
    public async Task PublishStream_NeverRaisesReceived()
    {
        var received = 0;
        using var stream = new TrakHoundMessagePublishStream<string>((item, token) => Task.FromResult<TrakHoundMessagePublishFailure>(null));
        stream.Received += (s, item) => received++;
        stream.OnReceived = item => received++;

        await stream.Push("a");

        Assert.Equal(0, received);
    }

    [Fact]
    public async Task PublishStream_AfterDispose_ReturnsFalse()
    {
        var calls = 0;
        var stream = new TrakHoundMessagePublishStream<string>((item, token) =>
        {
            calls++;
            return Task.FromResult<TrakHoundMessagePublishFailure>(null);
        });

        stream.Dispose();

        Assert.False(await stream.Push("a"));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void FailureFromResponse_IsNullOnSuccess()
    {
        Assert.Null(TrakHoundMessagePublishFailure.FromResponse("t", new TrakHoundServiceResponse(200)));

        var failure = TrakHoundMessagePublishFailure.FromResponse("t", TrakHoundServiceResponse.BadRequest("nope"));
        Assert.Equal(400, failure.StatusCode);
        Assert.Equal("nope", failure.Message);
    }


    [Fact]
    public async Task HttpPublishStream_SendsFramesAndRaisesFailures()
    {
        await using var server = await TestWebSocketServer.Start(frame =>
        {
            TrakHoundHttpMessageResponse.TryParsePublish(frame, null, false, 0, out var message, out _);
            var content = message.GetContentUtf8String();
            return content == "bad" ? new TrakHoundMessagePublishFailure(message.Topic, 403, "Not Allowed") : null;
        });

        var client = new TrakHoundHttpClient(new TrakHoundHttpClientConfiguration("localhost", server.Port));
        var response = await client.Services.Messages.PublishStreamString("plant/line1", retain: true, qos: 1);
        Assert.True(response.Success);

        using var stream = response.Result;
        var failure = new TaskCompletionSource<TrakHoundMessagePublishFailure>();
        stream.PublishFailed += (s, f) => failure.TrySetResult(f);

        // Push connects the WebSocket and waits for it, so the first Message isn't dropped
        Assert.True(await stream.Push("good"));
        Assert.True(await stream.Push("bad"));

        var reported = await failure.Task.WaitAsync(Timeout);
        Assert.Equal("plant/line1", reported.Topic);
        Assert.Equal(403, reported.StatusCode);

        var frames = await server.WaitForFrames(2, Timeout);
        Assert.True(TrakHoundHttpMessageResponse.TryParsePublish(frames[0], null, false, 0, out var first, out _));
        Assert.Equal("plant/line1", first.Topic);
        Assert.True(first.Retain);
        Assert.Equal(1, first.Qos);
        Assert.Equal("good", first.GetContentUtf8String());
        Assert.Equal("/_services/messages/publish/stream", server.Path);
    }

    [Fact]
    public async Task HttpPublishStream_FailsWith503_WhenItCantConnect()
    {
        var port = GetFreePort();
        var stream = new TrakHoundHttpMessagePublishStreamProbe(port) { ConnectTimeout = TimeSpan.FromMilliseconds(300) };
        var failures = new List<TrakHoundMessagePublishFailure>();
        stream.PublishFailed += (s, f) => failures.Add(f);

        Assert.False(await stream.Push("a"));
        Assert.Equal(503, Assert.Single(failures).StatusCode);

        stream.Dispose();
    }

    [Fact]
    public async Task HttpClient_PublishStreamString_RejectsInvalidQos()
    {
        var client = new TrakHoundHttpClient(new TrakHoundHttpClientConfiguration("localhost", 1));

        var response = await client.Services.Messages.PublishStreamString("t", qos: 5);

        Assert.False(response.Success);
        Assert.Equal(400, response.StatusCode);
    }


    private sealed class TrakHoundHttpMessagePublishStreamProbe : TrakHound.Clients.Services.TrakHoundHttpMessagePublishStream<string>
    {
        public TrakHoundHttpMessagePublishStreamProbe(int port)
            : base(new TrakHoundHttpClientConfiguration("localhost", port), "_services/messages/publish/stream", item => TrakHoundHttpMessageResponse.CreatePublish("t", null, null, Encoding.UTF8.GetBytes(item)), item => "t") { }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current) { }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }


    /// <summary>
    /// A minimal WebSocket server that records each frame and replies with the failure (as JSON) the handler returns
    /// </summary>
    private sealed class TestWebSocketServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Func<byte[], TrakHoundMessagePublishFailure> _handler;
        private readonly ConcurrentQueue<byte[]> _frames = new ConcurrentQueue<byte[]>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private Task _loop;

        public int Port { get; private set; }

        public string Path { get; private set; }


        private TestWebSocketServer(Func<byte[], TrakHoundMessagePublishFailure> handler)
        {
            _handler = handler;
        }

        public static Task<TestWebSocketServer> Start(Func<byte[], TrakHoundMessagePublishFailure> handler)
        {
            var server = new TestWebSocketServer(handler);
            server.Port = GetFreePort();
            server._listener.Prefixes.Add($"http://localhost:{server.Port}/");
            server._listener.Start();
            server._loop = Task.Run(server.Accept);
            return Task.FromResult(server);
        }

        public async Task<byte[][]> WaitForFrames(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (_frames.Count < count && DateTime.UtcNow < deadline) await Task.Delay(20);
            return _frames.ToArray();
        }

        private async Task Accept()
        {
            try
            {
                var context = await _listener.GetContextAsync();
                Path = context.Request.Url?.AbsolutePath;

                var webSocketContext = await context.AcceptWebSocketAsync(null);
                var webSocket = webSocketContext.WebSocket;
                var buffer = new byte[65536];

                while (!_stop.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await webSocket.ReceiveAsync(buffer, _stop.Token);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var frame = message.ToArray();
                    _frames.Enqueue(frame);

                    var failure = _handler(frame);
                    if (failure != null)
                    {
                        var bytes = Encoding.UTF8.GetBytes(Json.Convert(failure));
                        await webSocket.SendAsync(bytes, WebSocketMessageType.Binary, true, _stop.Token);
                    }
                }
            }
            catch { }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            _listener.Close();
            if (_loop != null) await Task.WhenAny(_loop, Task.Delay(1000));
        }
    }
}
