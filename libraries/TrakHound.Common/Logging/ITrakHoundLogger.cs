// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Logging
{
    /// <summary>
    /// A named Logger that writes Log Entries (received by its Log Provider)
    /// </summary>
    public interface ITrakHoundLogger
    {
        /// <summary>
        /// The Unique Identifier of the Logger
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The Name of the Logger
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Event for when a new Log Entry is received
        /// </summary>
        event TrakHoundLogEventHandler LogEntryReceived;


        /// <summary>
        /// Returns true if an entry at the specified Log Level would be raised. Check it before building
        /// an expensive Message (ex. an interpolated string) for a Debug or Trace entry
        /// </summary>
        bool IsEnabled(TrakHoundLogLevel logLevel);


        /// <summary>
        /// Logs the specified Log Entry (the Entry's Sender is appended to the Logger Name)
        /// </summary>
        void Log(TrakHoundLogItem logEntry);

        /// <summary>
        /// Logs a Message at the specified Log Level
        /// </summary>
        void Log(TrakHoundLogLevel logLevel, string message);
        /// <summary>
        /// Logs a Message at the specified Log Level using a sub-name appended to the Logger Name as the Sender
        /// </summary>
        void Log(TrakHoundLogLevel logLevel, string name, string message);

        /// <summary>
        /// Logs a Message at the Critical Log Level
        /// </summary>
        void LogCritical(string message);
        /// <summary>
        /// Logs a Message at the Critical Log Level using a sub-name appended to the Logger Name as the Sender
        /// </summary>
        void LogCritical(string name, string message);

        /// <summary>
        /// Logs a Message at the Error Log Level
        /// </summary>
        void LogError(string message);
        /// <summary>
        /// Logs a Message at the Error Log Level using a sub-name appended to the Logger Name as the Sender
        /// </summary>
        void LogError(string name, string message);

        /// <summary>
        /// Logs a Message at the Warning Log Level
        /// </summary>
        void LogWarning(string message);
        /// <summary>
        /// Logs a Message at the Warning Log Level using a sub-name appended to the Logger Name as the Sender
        /// </summary>
        void LogWarning(string name, string message);

        /// <summary>
        /// Logs a Message at the Information Log Level
        /// </summary>
        void LogInformation(string message);
        /// <summary>
        /// Logs a Message at the Information Log Level using a sub-name appended to the Logger Name as the Sender
        /// </summary>
        void LogInformation(string name, string message);

        /// <summary>
        /// Logs a Message at the Debug Log Level
        /// </summary>
        void LogDebug(string message);
        /// <summary>
        /// Logs a Message at the Debug Log Level using a sub-name appended to the Logger Name as the Sender
        /// </summary>
        void LogDebug(string name, string message);

        /// <summary>
        /// Logs a Message at the Trace Log Level
        /// </summary>
        void LogTrace(string message);
        /// <summary>
        /// Logs a Message at the Trace Log Level using a sub-name appended to the Logger Name as the Sender
        /// </summary>
        void LogTrace(string name, string message);
    }
}
