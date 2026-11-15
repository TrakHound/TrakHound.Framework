// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.Logging;
using TrakHound.Logging;

namespace TrakHound.Hosting
{
    /// <summary>
    /// Writes a module's Log entries to an ILogger, so they reach the host's log providers (ex. the console, the Windows Event Log, Application Insights)
    /// </summary>
    public static class TrakHoundHostLogging
    {
        public static LogLevel ToLogLevel(TrakHoundLogLevel level) => level switch
        {
            TrakHoundLogLevel.Critical => LogLevel.Critical,
            TrakHoundLogLevel.Error => LogLevel.Error,
            TrakHoundLogLevel.Warning => LogLevel.Warning,
            TrakHoundLogLevel.Debug => LogLevel.Debug,
            TrakHoundLogLevel.Trace => LogLevel.Trace,
            _ => LogLevel.Information
        };

        public static void Write(ILogger logger, TrakHoundLogItem item)
        {
            if (logger == null) return;

            if (!string.IsNullOrEmpty(item.Code)) logger.Log(ToLogLevel(item.LogLevel), "{Code} : {Message}", item.Code, item.Message);
            else logger.Log(ToLogLevel(item.LogLevel), "{Message}", item.Message);
        }
    }
}
