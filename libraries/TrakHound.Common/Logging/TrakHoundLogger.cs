// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text;

namespace TrakHound.Logging
{
    /// <summary>
    /// A Logger associated with the Type <typeparamref name="T"/>
    /// </summary>
    /// <typeparam name="T">The Type the Logger is associated with</typeparam>
    public class TrakHoundLogger<T> : TrakHoundLogger
    {
        /// <summary>
        /// The Type the Logger is associated with
        /// </summary>
        public Type LoggerType => typeof(T);


        /// <summary>
        /// Creates a new Logger named after the Type <typeparamref name="T"/>
        /// </summary>
        public TrakHoundLogger() : base(typeof(T).Name) { }

        /// <summary>
        /// Creates a new Logger with the specified Name
        /// </summary>
        /// <param name="name">The Name of the Logger</param>
        public TrakHoundLogger(string name) : base(name) { }
    }

    /// <summary>
    /// A Logger that raises the <see cref="LogEntryReceived"/> event for each Log Entry
    /// </summary>
    public class TrakHoundLogger : ITrakHoundLogger
    {
        private readonly string _name;
        private string _id;


        /// <summary>
        /// The Unique Identifier of the Logger
        /// </summary>
        public string Id => _id ??= GetLoggerId(_name);

        /// <summary>
        /// The Name of the Logger
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// The least severe Log Level that is raised. Entries less severe than this are ignored.
        /// Defaults to <see cref="TrakHoundLogLevel.Trace"/> (all entries are raised).
        /// </summary>
        public TrakHoundLogLevel MinimumLevel { get; set; } = TrakHoundLogLevel.Trace;

        /// <summary>
        /// Event for when a new Log Entry is received
        /// </summary>
        public event TrakHoundLogEventHandler LogEntryReceived;


        /// <summary>
        /// Creates a new Logger with the specified Name
        /// </summary>
        /// <param name="name">The Name of the Logger</param>
        public TrakHoundLogger(string name)
        {
            _name = name;
        }


        /// <summary>
        /// Generates the Unique Identifier for a Logger from its Name
        /// </summary>
        /// <param name="name">The Name of the Logger</param>
        /// <returns>The MD5 hash of the Name, or null if the Name is null</returns>
        public virtual string GetLoggerId(string name) => name?.ToMD5Hash();

        /// <summary>
        /// Returns true if an entry at the specified Log Level would be raised
        /// </summary>
        /// <param name="logLevel">The Log Level to check</param>
        public virtual bool IsEnabled(TrakHoundLogLevel logLevel) => logLevel <= MinimumLevel;

        /// <summary>
        /// True if anything is subscribed to <see cref="LogEntryReceived"/>
        /// </summary>
        protected bool HasLogEntryReceivedHandlers => LogEntryReceived != null;


        // The Log* helpers below all route through the virtual Log overloads, so they are intentionally non-virtual.
        // Override Log(...) or OnLogEntryReceived(...) to customize behavior.

        /// <summary>
        /// Logs the specified Log Entry. The Entry's Sender is appended to the Logger Name.
        /// The Entry's Code and Timestamp are preserved.
        /// </summary>
        /// <param name="logEntry">The Log Entry to log</param>
        public virtual void Log(TrakHoundLogItem logEntry)
        {
            OnLogEntryReceived(new TrakHoundLogItem(GetSender(logEntry.Sender), logEntry.LogLevel, logEntry.Message, logEntry.Code, logEntry.Timestamp));
        }

        /// <summary>
        /// Logs a Message at the specified Log Level using the Logger Name as the Sender
        /// </summary>
        /// <param name="logLevel">The Log Level of the Message</param>
        /// <param name="message">The Message to log</param>
        public virtual void Log(TrakHoundLogLevel logLevel, string message) => Log(logLevel, null, message);

        /// <summary>
        /// Logs a Message at the specified Log Level. The Sender is the Logger Name combined with <paramref name="name"/>.
        /// </summary>
        /// <param name="logLevel">The Log Level of the Message</param>
        /// <param name="name">The sub-name appended to the Logger Name (ignored if null or empty)</param>
        /// <param name="message">The Message to log</param>
        public virtual void Log(TrakHoundLogLevel logLevel, string name, string message)
        {
            if (!IsEnabled(logLevel)) return;

            OnLogEntryReceived(new TrakHoundLogItem(GetSender(name), logLevel, message));
        }

        /// <summary>
        /// Raises the <see cref="LogEntryReceived"/> event if the entry's Log Level is enabled.
        /// Exceptions thrown by subscribers are caught so that logging never fails the caller.
        /// </summary>
        /// <param name="logEntry">The Log Entry to raise</param>
        protected virtual void OnLogEntryReceived(TrakHoundLogItem logEntry)
        {
            if (!IsEnabled(logEntry.LogLevel)) return;

            var handler = LogEntryReceived;
            if (handler == null) return;

            try
            {
                handler.Invoke(this, logEntry);
            }
            catch { }
        }

        private string GetSender(string name) => !string.IsNullOrEmpty(name) ? Url.Combine(_name, name) : _name;


        /// <summary>
        /// Logs a Message at the Critical Log Level
        /// </summary>
        /// <param name="message">The Message to log</param>
        public void LogCritical(string message) => Log(TrakHoundLogLevel.Critical, message);

        /// <summary>
        /// Logs a Message at the Critical Log Level using a sub-name appended to the Logger Name
        /// </summary>
        /// <param name="name">The sub-name appended to the Logger Name</param>
        /// <param name="message">The Message to log</param>
        public void LogCritical(string name, string message) => Log(TrakHoundLogLevel.Critical, name, message);

        /// <summary>
        /// Logs an Exception (including Inner Exceptions and Stack Traces) at the Critical Log Level
        /// </summary>
        /// <param name="ex">The Exception to log</param>
        public void LogCritical(Exception ex) => LogException(TrakHoundLogLevel.Critical, null, ex);

        /// <summary>
        /// Logs an Exception (including Inner Exceptions and Stack Traces) at the Critical Log Level using a sub-name appended to the Logger Name
        /// </summary>
        /// <param name="name">The sub-name appended to the Logger Name</param>
        /// <param name="ex">The Exception to log</param>
        public void LogCritical(string name, Exception ex) => LogException(TrakHoundLogLevel.Critical, name, ex);

        /// <summary>
        /// Logs a Message at the Error Log Level
        /// </summary>
        /// <param name="message">The Message to log</param>
        public void LogError(string message) => Log(TrakHoundLogLevel.Error, message);

        /// <summary>
        /// Logs a Message at the Error Log Level using a sub-name appended to the Logger Name
        /// </summary>
        /// <param name="name">The sub-name appended to the Logger Name</param>
        /// <param name="message">The Message to log</param>
        public void LogError(string name, string message) => Log(TrakHoundLogLevel.Error, name, message);

        /// <summary>
        /// Logs an Exception (including Inner Exceptions and Stack Traces) at the Error Log Level
        /// </summary>
        /// <param name="ex">The Exception to log</param>
        public void LogError(Exception ex) => LogException(TrakHoundLogLevel.Error, null, ex);

        /// <summary>
        /// Logs an Exception (including Inner Exceptions and Stack Traces) at the Error Log Level using a sub-name appended to the Logger Name
        /// </summary>
        /// <param name="name">The sub-name appended to the Logger Name</param>
        /// <param name="ex">The Exception to log</param>
        public void LogError(string name, Exception ex) => LogException(TrakHoundLogLevel.Error, name, ex);

        /// <summary>
        /// Logs a Message at the Warning Log Level
        /// </summary>
        /// <param name="message">The Message to log</param>
        public void LogWarning(string message) => Log(TrakHoundLogLevel.Warning, message);

        /// <summary>
        /// Logs a Message at the Warning Log Level using a sub-name appended to the Logger Name
        /// </summary>
        /// <param name="name">The sub-name appended to the Logger Name</param>
        /// <param name="message">The Message to log</param>
        public void LogWarning(string name, string message) => Log(TrakHoundLogLevel.Warning, name, message);

        /// <summary>
        /// Logs a Message at the Information Log Level
        /// </summary>
        /// <param name="message">The Message to log</param>
        public void LogInformation(string message) => Log(TrakHoundLogLevel.Information, message);

        /// <summary>
        /// Logs a Message at the Information Log Level using a sub-name appended to the Logger Name
        /// </summary>
        /// <param name="name">The sub-name appended to the Logger Name</param>
        /// <param name="message">The Message to log</param>
        public void LogInformation(string name, string message) => Log(TrakHoundLogLevel.Information, name, message);

        /// <summary>
        /// Logs a Message at the Debug Log Level
        /// </summary>
        /// <param name="message">The Message to log</param>
        public void LogDebug(string message) => Log(TrakHoundLogLevel.Debug, message);

        /// <summary>
        /// Logs a Message at the Debug Log Level using a sub-name appended to the Logger Name
        /// </summary>
        /// <param name="name">The sub-name appended to the Logger Name</param>
        /// <param name="message">The Message to log</param>
        public void LogDebug(string name, string message) => Log(TrakHoundLogLevel.Debug, name, message);

        /// <summary>
        /// Logs a Message at the Trace Log Level
        /// </summary>
        /// <param name="message">The Message to log</param>
        public void LogTrace(string message) => Log(TrakHoundLogLevel.Trace, message);

        /// <summary>
        /// Logs a Message at the Trace Log Level using a sub-name appended to the Logger Name
        /// </summary>
        /// <param name="name">The sub-name appended to the Logger Name</param>
        /// <param name="message">The Message to log</param>
        public void LogTrace(string name, string message) => Log(TrakHoundLogLevel.Trace, name, message);

        private void LogException(TrakHoundLogLevel logLevel, string name, Exception ex)
        {
            // Check the level first to avoid building the stack trace string when it would be discarded
            if (ex == null || !IsEnabled(logLevel)) return;

            Log(logLevel, name, GetExceptionStackTrace(ex));
        }


        /// <summary>
        /// Builds a string containing the Message and Stack Trace of an Exception and all of its Inner Exceptions
        /// (including every Inner Exception of an <see cref="AggregateException"/>)
        /// </summary>
        /// <param name="ex">The Exception to format</param>
        /// <returns>The Message and Stack Trace of each Exception, separated by new lines, or null if <paramref name="ex"/> is null</returns>
        public static string GetExceptionStackTrace(Exception ex)
        {
            if (ex == null) return null;

            var sb = new StringBuilder();
            AppendException(sb, ex);
            return sb.ToString().TrimEnd();
        }

        private static void AppendException(StringBuilder sb, Exception ex)
        {
            sb.AppendLine(ex.Message);
            if (ex.StackTrace != null) sb.AppendLine(ex.StackTrace);

            if (ex is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.InnerExceptions)
                {
                    AppendException(sb, innerException);
                }
            }
            else if (ex.InnerException != null)
            {
                AppendException(sb, ex.InnerException);
            }
        }
    }
}
