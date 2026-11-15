// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TrakHound.Hosting.Tests
{
    public class HostTests
    {
        [Fact]
        public void Settings_AreReadFromConfiguration()
        {
            using var host = CreateHost(new Dictionary<string, string>
            {
                ["TrakHound:ConfigurationId"] = "barcodes",
                ["TrakHound:Parameters:interval"] = "250"
            });

            var context = host.Services.GetRequiredService<TrakHoundHostContext>();
            Assert.Equal("barcodes", context.Options.ConfigurationId);
            Assert.Equal("250", context.GetParameters()["interval"]);
            Assert.False(context.IsInstanceConfigured);
        }

        [Fact]
        public async Task Client_WithoutInstance_FailsWithReason()
        {
            using var host = CreateHost();
            var context = host.Services.GetRequiredService<TrakHoundHostContext>();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.Client.Instances.GetHostInformation());
            Assert.Contains("InstanceUrl", exception.Message);
        }

        [Fact]
        public async Task Worker_RunsForTheHostsLifetime()
        {
            var logs = new TestLoggerProvider();
            using var host = CreateHost(new Dictionary<string, string> { ["TrakHound:Parameters:interval"] = "250" }, services => services.AddTrakHoundWorker<SampleWorker>(), logs);

            await host.StartAsync();
            Assert.True(SampleWorker.Running);
            Assert.Equal(250, SampleWorker.Interval);
            Assert.Contains(logs.Entries, o => o.Category == typeof(SampleWorker).FullName && o.Message.Contains("Worker Started"));

            await host.StopAsync();
            Assert.False(SampleWorker.Running);
        }

        [Fact]
        public async Task Service_IsASingletonDisposedWithTheHost()
        {
            var host = CreateHost(new Dictionary<string, string> { ["TrakHound:Parameters:name"] = "sample" }, services => services.AddTrakHoundService<SampleService>());

            var service = host.Services.GetRequiredService<SampleService>();
            Assert.Same(service, host.Services.GetRequiredService<SampleService>());
            Assert.Equal("sample", service.GetName());

            await host.StartAsync();
            await host.StopAsync();
            host.Dispose();
            Assert.True(service.Disposed);
        }

        [Fact]
        public async Task HealthCheck_WithoutInstance_IsHealthy()
        {
            using var host = CreateHost(configureServices: services => services.AddHealthChecks().AddTrakHoundInstance());
            var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            Assert.Equal(HealthStatus.Healthy, report.Status);
        }

        [Fact]
        public async Task HealthCheck_UnreachableInstance_IsDegraded()
        {
            // Nothing listens on port 1, so the connection is refused at once
            using var host = CreateHost(new Dictionary<string, string> { ["TrakHound:InstanceUrl"] = "http://127.0.0.1:1" }, services => services.AddHealthChecks().AddTrakHoundInstance());
            var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
            Assert.Equal(HealthStatus.Degraded, report.Status);
        }

        [Fact]
        public void PackageInformation_UsesVersionPrefix()
        {
            var directory = Directory.CreateTempSubdirectory().FullName;
            try
            {
                var path = Path.Combine(directory, "trakhound.package.json");
                File.WriteAllText(path, "{ \"id\": \"Test.Api\", \"category\": \"api\" }");
                Assert.True(string.IsNullOrEmpty(TrakHoundPackageInformation.Read(path).Version));

                // The version is the prefix in trakhound.package.json (the CLI adds the git height when it builds the Package)
                File.WriteAllText(path, "{ \"id\": \"Test.Api\", \"version\": \"0.3\" }");
                var package = TrakHoundPackageInformation.Read(path);
                Assert.Equal("Test.Api", package.Id);
                Assert.Equal("0.3", package.Version);

                // trakhound.package.publish.json only holds Publish Targets, so it doesn't change the version
                File.WriteAllText(Path.Combine(directory, "trakhound.package.publish.json"), "{ \"defaultTarget\": \"development\", \"targets\": { } }");
                Assert.Equal("0.3", TrakHoundPackageInformation.Read(path).Version);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }


        private static IHost CreateHost(Dictionary<string, string> settings = null, Action<IServiceCollection> configureServices = null, ILoggerProvider logs = null)
        {
            var builder = Host.CreateApplicationBuilder();
            builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string>());
            if (logs != null) builder.Logging.AddProvider(logs);

            builder.Services.AddTrakHound(builder.Configuration.GetSection(TrakHoundHostOptions.SectionName));
            configureServices?.Invoke(builder.Services);
            return builder.Build();
        }
    }


    /// <summary>
    /// Keeps the Log entries written through ILogger
    /// </summary>
    public class TestLoggerProvider : ILoggerProvider
    {
        public List<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new TestLogger(this, categoryName);

        public void Dispose() { }


        private class TestLogger : ILogger
        {
            private readonly TestLoggerProvider _provider;
            private readonly string _category;

            public TestLogger(TestLoggerProvider provider, string category)
            {
                _provider = provider;
                _category = category;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                lock (_provider.Entries) _provider.Entries.Add((_category, logLevel, formatter(state, exception)));
            }
        }
    }
}
