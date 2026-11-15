// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Debug;
using TrakHound.Security;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Testing.Tests
{
    public class TestContextTests : IDisposable
    {
        private readonly TrakHoundTestContext _context = new TrakHoundTestContext();


        public void Dispose() => _context.Dispose();


        [Fact]
        public async Task Function_UsesKeyValuesAndDefaults()
        {
            var first = await _context.RunFunction<CounterFunction>(new Dictionary<string, string> { ["key"] = "a" });
            var second = await _context.RunFunction<CounterFunction>(new Dictionary<string, string> { ["key"] = "a", ["by"] = "5" });

            Assert.Equal(200, second.StatusCode);
            Assert.Equal("1", first.GetParameter("message"));
            Assert.Equal("6", second.GetParameter("message"));

            var stored = await _context.Client.Services.KeyValues.Read("counts", "a");
            Assert.Equal("6", stored.Result);
            Assert.Contains(_context.Logs, o => o.Message == "a = 6");
        }

        [Fact]
        public async Task Function_InvalidInputIsRefused()
        {
            var missing = await _context.RunFunction<CounterFunction>();
            var notInteger = await _context.RunFunction<CounterFunction>(new Dictionary<string, string> { ["key"] = "a", ["by"] = "x" });

            Assert.Equal(400, missing.StatusCode);
            Assert.Equal(400, notInteger.StatusCode);
            Assert.Empty(_context.Logs);
        }

        [Fact]
        public async Task Function_ReadsConfigurationParameters()
        {
            _context.Parameters["collection"] = "other";

            await _context.RunFunction<CounterFunction>(new Dictionary<string, string> { ["key"] = "a" });

            Assert.Equal("1", (await _context.Client.Services.KeyValues.Read("other", "a")).Result);
            Assert.Equal(404, (await _context.Client.Services.KeyValues.Read("counts", "a")).StatusCode);
        }

        [Fact]
        public async Task Function_CallsRegisteredFunction()
        {
            _context.RegisterFunction<CounterFunction>("counter");

            var response = await _context.RunFunction<TwiceFunction>();

            Assert.Equal(200, response.StatusCode);
            Assert.Equal("2", response.GetParameter("message"));
            Assert.Equal(2, _context.Client.Functions.Runs.Count(o => o.FunctionId == "counter"));
        }

        [Fact]
        public async Task Function_UnregisteredFunctionIsNotFound()
        {
            var response = await _context.RunFunction<TwiceFunction>();

            Assert.Equal(500, response.StatusCode);
            Assert.Single(_context.Client.Functions.Runs);
        }

        [Fact]
        public async Task Function_UsesYourService()
        {
            _context.Client.Services.Use(_context.CreateService<FixedKeyValueService>());

            var response = await _context.RunFunction<CounterFunction>(new Dictionary<string, string> { ["key"] = "a" });

            Assert.Equal("42", response.GetParameter("message"));
        }

        [Fact]
        public async Task TimeSeries_IsInMemoryByDefault()
        {
            var timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var points = new[] { new TrakHoundTimeSeriesPoint { Measurement = "spindle", Timestamp = timestamp, Fields = new() { ["speed"] = 1.0 } } };

            Assert.True((await _context.Client.Services.TimeSeries.Write(points)).Success);

            Assert.Equal(new[] { "spindle" }, (await _context.Client.Services.TimeSeries.ListMeasurements()).Result);
        }

        [Fact]
        public async Task Worker_WritesToVolume()
        {
            _context.Parameters["message"] = "beat";
            var worker = _context.CreateWorker<HeartbeatWorker>();

            await worker.Start();
            await worker.Stop();

            Assert.Equal("beat", await _context.Volume.ReadString("heartbeat.txt"));
            Assert.Single(_context.GetLogs(TrakHoundLogLevel.Warning));
            Assert.Empty(_context.GetLogs(TrakHoundLogLevel.Error));
        }

        [Fact]
        public async Task ApiController_IsInitialized()
        {
            _context.Parameters["greeting"] = "Hi";
            var controller = await _context.CreateApiController<GreetingController>();

            var response = await controller.Greet("Ada");

            Assert.Equal(200, response.StatusCode);
            Assert.Equal("Hi, Ada", response.GetContentUtf8String());
            Assert.Equal(200, (await _context.Client.Services.Blobs.Read("greetings", "Ada")).StatusCode);
            Assert.Contains(_context.Logs, o => o.Message == "Greeting Ada");
        }

        [Fact]
        public async Task IdentityProvider_Authenticates()
        {
            _context.Parameters["token"] = "secret";
            var provider = _context.CreateIdentityProvider<HeaderIdentityProvider>();

            var request = new TrakHoundDebugIdentityRequest();
            request.Headers["X-Test-Token"] = "secret";
            var result = await provider.Authenticate(_context.CreateIdentityContext(request));

            Assert.Equal(TrakHoundIdentityResultStatus.Success, result.Status);
            Assert.Equal("tester", result.Identity.UserId);
        }

        [Fact]
        public async Task Client_UnfakedPartThrowsNotSupported()
        {
            var exception = await Assert.ThrowsAsync<NotSupportedException>(() => _context.Client.Instances.GetHostInformation());

            Assert.Contains("TrakHoundTestClient.Instances", exception.Message);
        }

        [Fact]
        public async Task Dispose_DeletesTheDirectory()
        {
            var context = new TrakHoundTestContext();
            await context.Volume.WriteString("file.txt", "x");
            Assert.True(Directory.Exists(context.Directory));

            context.Dispose();

            Assert.False(Directory.Exists(context.Directory));
        }
    }
}
