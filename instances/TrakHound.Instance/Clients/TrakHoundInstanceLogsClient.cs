// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Logging;

namespace TrakHound.Clients
{
    public class TrakHoundInstanceLogsClient : ITrakHoundLogsClient, IDisposable
    {
        private readonly ITrakHoundLogProvider _logProvider;
        private readonly Dictionary<string, TrakHoundStream<IEnumerable<TrakHoundLogItem>>> _logStreams = new Dictionary<string, TrakHoundStream<IEnumerable<TrakHoundLogItem>>>(); // StreamId => Stream
        private readonly List<string> _streamIds = new List<string>();
        private readonly Dictionary<string, string> _patterns = new Dictionary<string, string>(); // ConsumerId => Pattern
        private readonly Dictionary<string, TrakHoundLogLevel> _minimumLogLevels = new Dictionary<string, TrakHoundLogLevel>(); // ConsumerId => Minimum Log Level
        private readonly Dictionary<string, IDisposable> _receivers = new Dictionary<string, IDisposable>(); // ConsumerId => Log Provider Receiver Registration
        private readonly object _lock = new object();


        public TrakHoundInstanceLogsClient(ITrakHoundLogProvider logProvider)
        {
            _logProvider = logProvider;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_logStreams.Count > 0) _logProvider.LogEntryReceived -= LogUpdated;
                foreach (var receiver in _receivers.Values) receiver?.Dispose();
                _receivers.Clear();
                _logStreams.Clear();
                _streamIds.Clear();
            }
        }


        public async Task<IEnumerable<string>> List(string pattern)
        {
            var loggerIds = _logProvider.QueryLoggerIds(pattern);
            if (!loggerIds.IsNullOrEmpty())
            {
                for (var i = 0; i < loggerIds.Length; i++)
                {
                    loggerIds[i] = GetSender(loggerIds[i]);
                }

                return loggerIds;
            }

            return null;
        }

        public async Task<IEnumerable<string>> ListDirectories(string pattern)
        {
            var directories = _logProvider.QueryDirectories(pattern);
            if (!directories.IsNullOrEmpty())
            {
                return directories;
            }

            return null;
        }

        public async Task<IEnumerable<TrakHoundLogItem>> Query(string pattern, DateTime from, DateTime to, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            var query = new TrakHoundLogQuery(pattern, from, to) { MinimumLevel = minimumLogLevel, Skip = skip, Take = take, SortOrder = sortOrder };
            var result = await Query(query);
            return result?.Items;
        }

        // Queries read SQLite and archive files synchronously, so they run on the Thread Pool instead of the caller (ex. a Blazor circuit)
        public Task<TrakHoundLogQueryResult> Query(TrakHoundLogQuery query)
        {
            return Task.Run(() => _logProvider.Query(query));
        }

        public Task<IEnumerable<TrakHoundLoggerLogCount>> QueryCounts(TrakHoundLogQuery query)
        {
            return Task.Run<IEnumerable<TrakHoundLoggerLogCount>>(() => _logProvider.QueryCounts(query));
        }

        public async Task<IEnumerable<TrakHoundLoggerInformation>> QueryLoggers(string pattern)
        {
            return _logProvider.QueryLoggers(pattern);
        }

        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> Subscribe(string pattern, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Information)
        {
            if (!string.IsNullOrEmpty(pattern))
            {
                var stream = new TrakHoundStream<IEnumerable<TrakHoundLogItem>>();

                lock (_lock)
                {
                    // Only subscribed to while there are Streams, so a Client that isn't disposed (ex. one created per Request) isn't kept alive by the Log Provider
                    if (_logStreams.Count == 0) _logProvider.LogEntryReceived += LogUpdated;
                    _logStreams.Add(stream.Id, stream);
                    _patterns.Add(stream.Id, pattern);
                    _minimumLogLevels.Add(stream.Id, minimumLogLevel);
                    _streamIds.Add(stream.Id);

                    // So the Log Provider's Loggers raise the Levels this Stream receives
                    _receivers.Add(stream.Id, _logProvider.RegisterReceiver(minimumLogLevel));
                }

                stream.OnDisposed = () =>
                {
                    lock (_lock)
                    {
                        if (_logStreams.Remove(stream.Id) && _logStreams.Count == 0) _logProvider.LogEntryReceived -= LogUpdated;
                        _patterns.Remove(stream.Id);
                        _minimumLogLevels.Remove(stream.Id);
                        _streamIds.Remove(stream.Id);
                        if (_receivers.Remove(stream.Id, out var receiver)) receiver?.Dispose();
                    }
                };

                return stream;
            }

            return default;
        }

        private async void LogUpdated(object sender, TrakHoundLogItem item)
        {
            var streams = new List<TrakHoundStream<IEnumerable<TrakHoundLogItem>>>();
            var items = new TrakHoundLogItem[] { item };

            // Match the Logger Name (and the Sender) the same way as Log Queries
            var loggerName = (sender as ITrakHoundLogger)?.Name ?? item.Sender;

            lock (_lock)
            {
                foreach (var streamId in _streamIds)
                {
                    string pattern;
                    TrakHoundLogLevel minimumLogLevel;
                    TrakHoundStream<IEnumerable<TrakHoundLogItem>> stream;

                    pattern = _patterns.GetValueOrDefault(streamId);
                    minimumLogLevel = _minimumLogLevels.GetValueOrDefault(streamId);
                    stream = _logStreams.GetValueOrDefault(streamId);

                    if (stream != null && item.LogLevel <= minimumLogLevel && TrakHoundLoggerPattern.IsMatch(new[] { pattern }, loggerName, item.Sender))
                    {
                        streams.Add(stream);
                    }
                }
            }

            if (!streams.IsNullOrEmpty())
            {
                foreach (var stream in streams)
                {
                    // async void : an exception would end the process (and isn't logged, since logging it would call this again)
                    try
                    {
                        await stream.Push(items);
                    }
                    catch { }
                }
            }
        }

        private static string GetSender(string input)
        {
            if (!string.IsNullOrEmpty(input))
            {
                // Remove Instance Logger ID

                //var i = input.IndexOf('/');
                //if (i > 0 && i < input.Length - 1)
                //{
                //    return input.Substring(i + 1);
                //}
                //else
                //{
                //    return input;
                //}

                return input;
            }

            return null;
        }
    }
}
