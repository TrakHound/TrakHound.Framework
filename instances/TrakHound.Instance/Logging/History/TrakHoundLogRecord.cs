// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TrakHound.Logging;

namespace TrakHound.Instance.Logging.History
{
    /// <summary>
    /// A Log History entry, as stored
    /// </summary>
    public class TrakHoundLogRecord
    {
        /// <summary>
        /// Unix Time (nanoseconds, UTC)
        /// </summary>
        public long Timestamp { get; set; }

        public TrakHoundLogLevel Level { get; set; }

        public string LoggerId { get; set; }

        /// <summary>
        /// The readable Logger Name (ex. "api/TrakHound.Packages.Api")
        /// </summary>
        public string LoggerName { get; set; }

        /// <summary>
        /// The Logger Name plus an optional sub path (ex. "api/TrakHound.Packages.Api/requests")
        /// </summary>
        public string Sender { get; set; }

        public string Code { get; set; }

        public string Message { get; set; }

        public string Exception { get; set; }


        public TrakHoundLogItem ToItem()
        {
            var message = Message;
            if (!string.IsNullOrEmpty(Exception)) message = string.IsNullOrEmpty(message) ? Exception : $"{message}{Environment.NewLine}{Exception}";

            return new TrakHoundLogItem(Sender ?? LoggerName ?? LoggerId, Level, message, Code, Timestamp);
        }
    }


    /// <summary>
    /// Reads and writes Log History records as JSON Lines, and parses Log History file names
    /// </summary>
    public static class TrakHoundLogRecordFormat
    {
        // Legacy (NLog) layout : "${longdate}|${message} ${exception}" (Local time)
        private static readonly Regex _legacyLineStart = new Regex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{4})\|", RegexOptions.Compiled);
        private static readonly Regex _fileNameRegex = new Regex(@"^(?<date>\d{4}-\d{2}-\d{2})(\.(?<level>trace|debug|info|warn|error|fatal))?(\.(?<part>\d+))?\.log(?<gz>\.gz)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly JsonWriterOptions _writerOptions = new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };


        /// <summary>
        /// Write the record as a single JSON line (newlines within values are escaped), including the trailing newline
        /// </summary>
        public static void WriteJsonLine(IBufferWriter<byte> output, TrakHoundLogRecord record)
        {
            using (var writer = new Utf8JsonWriter(output, _writerOptions))
            {
                writer.WriteStartObject();
                writer.WriteString("ts", record.Timestamp.ToDateTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));
                writer.WriteString("lvl", record.Level.ToString());
                writer.WriteString("id", record.LoggerId);
                if (record.LoggerName != null) writer.WriteString("name", record.LoggerName);
                if (record.Sender != null && record.Sender != record.LoggerName) writer.WriteString("sender", record.Sender);
                if (!string.IsNullOrEmpty(record.Code)) writer.WriteString("code", record.Code);
                writer.WriteString("msg", record.Message);
                if (!string.IsNullOrEmpty(record.Exception)) writer.WriteString("ex", record.Exception);
                writer.WriteEndObject();
            }

            var newline = output.GetSpan(1);
            newline[0] = (byte)'\n';
            output.Advance(1);
        }

        public static TrakHoundLogRecord ParseJsonLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{') return null;

            try
            {
                var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(line));
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;

                var record = new TrakHoundLogRecord();
                string ts = null;

                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    var property = reader.GetString();
                    if (!reader.Read()) break;

                    var value = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
                    switch (property)
                    {
                        case "ts": ts = value; break;
                        case "lvl": record.Level = Enum.TryParse<TrakHoundLogLevel>(value, true, out var level) ? level : TrakHoundLogLevel.Information; break;
                        case "id": record.LoggerId = value; break;
                        case "name": record.LoggerName = value; break;
                        case "sender": record.Sender = value; break;
                        case "code": record.Code = value; break;
                        case "msg": record.Message = value; break;
                        case "ex": record.Exception = value; break;
                        default: if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray) reader.Skip(); break;
                    }
                }

                if (ts == null || !DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var timestamp)) return null;

                record.Timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc).ToUnixTime();
                record.Sender ??= record.LoggerName;
                return record;
            }
            catch
            {
                // Partially written or corrupt line
                return null;
            }
        }

        /// <summary>
        /// Read records from a legacy (NLog "${longdate}|${message}") file. Lines that don't start with a timestamp
        /// (ex. Exception stack traces) are appended to the previous entry.
        /// </summary>
        public static IEnumerable<TrakHoundLogRecord> ReadLegacyLines(IEnumerable<string> lines, string loggerId, string loggerName, TrakHoundLogLevel level)
        {
            TrakHoundLogRecord current = null;
            StringBuilder message = null;

            foreach (var line in lines)
            {
                var match = _legacyLineStart.Match(line);
                if (match.Success)
                {
                    if (current != null)
                    {
                        current.Message = message.ToString().TrimEnd();
                        yield return current;
                    }

                    current = null;
                    if (DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp))
                    {
                        current = new TrakHoundLogRecord
                        {
                            Timestamp = timestamp.ToUnixTime(),
                            Level = level,
                            LoggerId = loggerId,
                            LoggerName = loggerName,
                            Sender = loggerName
                        };
                        message = new StringBuilder(line.Substring(match.Length));
                    }
                }
                else if (current != null)
                {
                    message.Append(Environment.NewLine).Append(line);
                }
            }

            if (current != null)
            {
                current.Message = message.ToString().TrimEnd();
                yield return current;
            }
        }


        /// <summary>
        /// Parse a Log History file name : "{yyyy-MM-dd}[.{level}][.{part}].log[.gz]"
        /// (a level in the name indicates a legacy NLog file, whose date is a Local date rather than a UTC date)
        /// </summary>
        public static TrakHoundLogFileInfo ParseFileName(string path)
        {
            var name = System.IO.Path.GetFileName(path);
            var match = _fileNameRegex.Match(name ?? "");
            if (!match.Success) return null;

            if (!DateTime.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;

            var info = new TrakHoundLogFileInfo();
            info.Path = path;
            info.Date = date;
            info.IsCompressed = match.Groups["gz"].Success;
            info.IsLegacy = match.Groups["level"].Success;
            info.LegacyLevel = info.IsLegacy ? GetLegacyLevel(match.Groups["level"].Value) : TrakHoundLogLevel.Information;

            if (info.IsLegacy)
            {
                // Legacy files are named by Local date
                var start = DateTime.SpecifyKind(date, DateTimeKind.Local);
                info.MinTimestamp = start.ToUnixTime();
                info.MaxTimestamp = start.AddDays(1).ToUnixTime() - 1;
            }
            else
            {
                var start = DateTime.SpecifyKind(date, DateTimeKind.Utc);
                info.MinTimestamp = start.ToUnixTime();
                info.MaxTimestamp = start.AddDays(1).ToUnixTime() - 1;
            }

            return info;
        }

        public static string GetFileName(DateTime utcDate, int part = 0, bool compressed = false)
        {
            var name = utcDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (part > 0) name += $".{part}";
            name += ".log";
            if (compressed) name += ".gz";
            return name;
        }

        private static TrakHoundLogLevel GetLegacyLevel(string level)
        {
            switch (level.ToLowerInvariant())
            {
                case "fatal": return TrakHoundLogLevel.Critical;
                case "error": return TrakHoundLogLevel.Error;
                case "warn": return TrakHoundLogLevel.Warning;
                case "debug": return TrakHoundLogLevel.Debug;
                case "trace": return TrakHoundLogLevel.Trace;
                default: return TrakHoundLogLevel.Information;
            }
        }
    }

    public class TrakHoundLogFileInfo
    {
        public string Path { get; set; }

        /// <summary>
        /// The date in the file name (UTC for current files, Local for legacy files)
        /// </summary>
        public DateTime Date { get; set; }

        public long MinTimestamp { get; set; }

        public long MaxTimestamp { get; set; }

        public bool IsCompressed { get; set; }

        public bool IsLegacy { get; set; }

        public TrakHoundLogLevel LegacyLevel { get; set; }
    }
}
