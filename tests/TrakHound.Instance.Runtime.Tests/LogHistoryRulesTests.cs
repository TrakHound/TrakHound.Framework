// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;
using NLog.Config;
using NLog.Targets;
using TrakHound.Instance.Logging;
using TrakHound.Instance.Logging.History;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// Log History's "*" Rule must not enable framework Loggers below frameworkMinimumLevel, or ASP.NET Core
    /// creates entries for every Request (ex. "Request starting") that are then dropped
    /// </summary>
    public class LogHistoryRulesTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"trakhound-log-rules-tests-{Guid.NewGuid()}");
        private readonly TrakHoundFileLogStore _store;


        public LogHistoryRulesTests()
        {
            _store = new TrakHoundFileLogStore(_root);
        }

        public void Dispose()
        {
            _store.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        private static LogFactory Apply(TrakHoundFileLogStore store, string minimumLevel, string frameworkMinimumLevel, Action<LoggingConfiguration> configure = null)
        {
            var factory = new LogFactory();
            var configuration = new LoggingConfiguration(factory);
            configure?.Invoke(configuration);

            var logConfiguration = new TrakHoundLogConfiguration { MinimumLevel = minimumLevel, FrameworkMinimumLevel = frameworkMinimumLevel };
            TrakHoundLogHistoryTarget.Apply(configuration, store, logConfiguration);
            factory.Configuration = configuration;
            return factory;
        }

        [Fact]
        public void FrameworkLoggers_BelowFrameworkMinimumLevel_AreDisabled()
        {
            using var factory = Apply(_store, "Information", "Warning");

            var hosting = factory.GetLogger("Microsoft.AspNetCore.Hosting.Diagnostics");
            Assert.False(hosting.IsInfoEnabled);
            Assert.True(hosting.IsWarnEnabled);
            Assert.False(factory.GetLogger("System.Net.Http.HttpClient").IsInfoEnabled);

            // Other Loggers still write from minimumLevel
            Assert.True(factory.GetLogger("TrakHound.Instance").IsInfoEnabled);
        }

        [Fact]
        public void FrameworkLoggers_WrittenByAnotherRule_StayEnabled()
        {
            using var factory = Apply(_store, "Information", "Warning", configuration =>
            {
                var target = new MemoryTarget("memory");
                configuration.AddTarget(target);
                configuration.LoggingRules.Add(new LoggingRule("Microsoft.*", LogLevel.Info, LogLevel.Fatal, target));
            });

            Assert.True(factory.GetLogger("Microsoft.AspNetCore.Hosting.Diagnostics").IsInfoEnabled);
            Assert.False(factory.GetLogger("System.Net.Http.HttpClient").IsInfoEnabled);
        }

        [Fact]
        public void FrameworkMinimumLevel_Trace_AddsNoRule()
        {
            using var factory = Apply(_store, "Information", "Trace");

            Assert.True(factory.GetLogger("Microsoft.AspNetCore.Hosting.Diagnostics").IsTraceEnabled);
        }
    }
}
