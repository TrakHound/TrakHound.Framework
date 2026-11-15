// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace TrakHound.Instance.Logging
{
    /// <summary>
    /// Formats Microsoft.Extensions.Logging Console output (ex. ASP.NET Core Hosting messages)
    /// to match the TrakHound (NLog) Console layout : "2026-09-30 21:27:09.034  INFO   [Category]  Message"
    /// </summary>
    internal sealed class TrakHoundConsoleFormatter : ConsoleFormatter
    {
        public const string FormatterName = "trakhound";


        public TrakHoundConsoleFormatter() : base(FormatterName) { }


        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter)
        {
            var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
            if (string.IsNullOrEmpty(message) && logEntry.Exception == null) return;

            textWriter.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            textWriter.Write("  ");
            textWriter.Write(GetLevel(logEntry.LogLevel).PadRight(5));
            textWriter.Write("  [");
            textWriter.Write(logEntry.Category);
            textWriter.Write("]  ");
            textWriter.Write(message);

            if (logEntry.Exception != null)
            {
                textWriter.WriteLine();
                textWriter.Write(logEntry.Exception.ToString());
            }

            textWriter.WriteLine();
        }

        // Match the NLog level names used by the TrakHound layout
        private static string GetLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace: return "TRACE";
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Information: return "INFO";
                case LogLevel.Warning: return "WARN";
                case LogLevel.Error: return "ERROR";
                case LogLevel.Critical: return "FATAL";
                default: return logLevel.ToString().ToUpper();
            }
        }
    }
}
