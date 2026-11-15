// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using TrakHound.Logging;

namespace TrakHound.Instance.Logging.History
{
    /// <summary>
    /// The per-entry part of a <see cref="TrakHoundLogQuery"/> (time range, levels, code and search)
    /// </summary>
    public class TrakHoundLogFilter
    {
        public long From { get; }

        public long To { get; }

        public TrakHoundLogQuery Query { get; }

        /// <summary>
        /// The most records the query can read from any one Source (0 for no limit)
        /// </summary>
        public long Limit { get; set; }

        /// <summary>
        /// The IDs of the Loggers whose Names don't match the Query's Patterns, but whose entries may match by Sender
        /// (ex. "workers/**/Devices/d-1" matches the Sender "workers/{package}/{id}/Engine/Devices/d-1" of the Logger "workers/{package}/{id}/Engine").
        /// Only the entries from these Loggers whose Sender matches are included.
        /// </summary>
        public IReadOnlySet<string> SenderLoggerIds { get; set; }


        public TrakHoundLogFilter(TrakHoundLogQuery query, long from, long to)
        {
            Query = query;
            From = from;
            To = to;
        }


        public bool IsMatch(TrakHoundLogRecord record)
        {
            if (record == null) return false;
            if (record.Timestamp < From || record.Timestamp > To) return false;
            if (!Query.IsLevelMatch(record.Level)) return false;
            if (!string.IsNullOrEmpty(Query.Code) && !string.Equals(Query.Code, record.Code, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(Query.Sender) && !string.Equals(Query.Sender, record.Sender ?? record.LoggerName, StringComparison.OrdinalIgnoreCase)) return false;
            if (!IsSenderMatch(record)) return false;

            if (!string.IsNullOrEmpty(Query.Search))
            {
                var found = (record.Message != null && record.Message.Contains(Query.Search, StringComparison.OrdinalIgnoreCase)) ||
                            (record.Exception != null && record.Exception.Contains(Query.Search, StringComparison.OrdinalIgnoreCase));
                if (!found) return false;
            }

            return true;
        }

        /// <summary>
        /// Returns false for an entry from a Logger in <see cref="SenderLoggerIds"/> whose Sender doesn't match the Query's Patterns
        /// </summary>
        public bool IsSenderMatch(TrakHoundLogRecord record)
        {
            if (record == null) return false;
            if (SenderLoggerIds == null || record.LoggerId == null || !SenderLoggerIds.Contains(record.LoggerId)) return true;

            return TrakHoundLoggerPattern.IsMatch(Query.Patterns, record.Sender ?? record.LoggerName);
        }
    }


    /// <summary>
    /// A source of Log History records that are (approximately) in time order
    /// </summary>
    public interface ITrakHoundLogSource
    {
        /// <summary>
        /// No record in the source is earlier than this (Unix nanoseconds)
        /// </summary>
        long MinTimestamp { get; }

        /// <summary>
        /// No record in the source is later than this (Unix nanoseconds)
        /// </summary>
        long MaxTimestamp { get; }

        /// <summary>
        /// Read the records that match the Filter, in time order (newest first when descending)
        /// </summary>
        IEnumerable<TrakHoundLogRecord> Read(TrakHoundLogFilter filter, bool descending);
    }


    /// <summary>
    /// A single Log History file (JSON Lines or legacy NLog format, optionally gzip compressed)
    /// </summary>
    public class TrakHoundLogFileSource : ITrakHoundLogSource
    {
        private const int _reverseChunkSize = 64 * 1024;

        public TrakHoundLogFileInfo File { get; }

        public string LoggerId { get; }

        public string LoggerName { get; }

        public long MinTimestamp => File.MinTimestamp;

        public long MaxTimestamp => File.MaxTimestamp;


        public TrakHoundLogFileSource(TrakHoundLogFileInfo file, string loggerId, string loggerName)
        {
            File = file;
            LoggerId = loggerId;
            LoggerName = loggerName;
        }


        public IEnumerable<TrakHoundLogRecord> Read(TrakHoundLogFilter filter, bool descending)
        {
            if (!System.IO.File.Exists(File.Path)) return Enumerable.Empty<TrakHoundLogRecord>();

            if (descending && !File.IsCompressed && !File.IsLegacy)
            {
                // Read from the end of the file so that the newest entries are found without reading the whole file
                return Filter(ReadLinesReverse(File.Path).Select(ParseLine), filter);
            }

            var records = Filter(ReadRecords(), filter);
            return descending ? ReadNewest(records, filter?.Limit ?? 0) : records;
        }

        /// <summary>
        /// Compressed and legacy files can only be read forwards, so keep only the newest records the query can use (newest first).
        /// Deferred so that the file is only read once the merge reaches it.
        /// </summary>
        private static IEnumerable<TrakHoundLogRecord> ReadNewest(IEnumerable<TrakHoundLogRecord> records, long limit)
        {
            var buffer = new Queue<TrakHoundLogRecord>();
            foreach (var record in records)
            {
                buffer.Enqueue(record);
                if (limit > 0 && buffer.Count > limit) buffer.Dequeue();
            }

            var newest = buffer.ToArray();
            for (var i = newest.Length - 1; i >= 0; i--)
            {
                yield return newest[i];
            }
        }

        private IEnumerable<TrakHoundLogRecord> ReadRecords()
        {
            var lines = ReadLines(File.Path, File.IsCompressed);
            if (File.IsLegacy) return TrakHoundLogRecordFormat.ReadLegacyLines(lines, LoggerId, LoggerName, File.LegacyLevel);
            return lines.Select(ParseLine);
        }

        private TrakHoundLogRecord ParseLine(string line)
        {
            var record = TrakHoundLogRecordFormat.ParseJsonLine(line);
            if (record != null)
            {
                // Fill in the Logger from the Logger Index for records that don't include it
                record.LoggerId ??= LoggerId;
                record.LoggerName ??= LoggerName;
                record.Sender ??= record.LoggerName;
            }
            return record;
        }

        private static IEnumerable<TrakHoundLogRecord> Filter(IEnumerable<TrakHoundLogRecord> records, TrakHoundLogFilter filter)
        {
            foreach (var record in records)
            {
                if (filter == null || filter.IsMatch(record)) yield return record;
            }
        }

        private static IEnumerable<string> ReadLines(string path, bool compressed)
        {
            using (var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var stream = compressed ? (Stream)new GZipStream(fileStream, CompressionMode.Decompress) : fileStream)
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    yield return line;
                }
            }
        }

        /// <summary>
        /// Read the lines of a (UTF-8) file from the end to the beginning
        /// </summary>
        private static IEnumerable<string> ReadLinesReverse(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var position = stream.Length;
                var leftover = Array.Empty<byte>();

                while (position > 0)
                {
                    var size = (int)Math.Min(_reverseChunkSize, position);
                    position -= size;

                    var buffer = new byte[size + leftover.Length];
                    stream.Position = position;
                    var read = 0;
                    while (read < size)
                    {
                        var n = stream.Read(buffer, read, size - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    Buffer.BlockCopy(leftover, 0, buffer, size, leftover.Length);

                    var end = buffer.Length;
                    for (var i = buffer.Length - 1; i >= 0; i--)
                    {
                        if (buffer[i] == (byte)'\n')
                        {
                            if (end - i - 1 > 0) yield return DecodeLine(buffer, i + 1, end - i - 1);
                            end = i;
                        }
                    }

                    leftover = buffer.AsSpan(0, end).ToArray();
                }

                if (leftover.Length > 0) yield return DecodeLine(leftover, 0, leftover.Length);
            }
        }

        private static string DecodeLine(byte[] buffer, int offset, int count)
        {
            if (count > 0 && buffer[offset + count - 1] == (byte)'\r') count--;
            return Encoding.UTF8.GetString(buffer, offset, count);
        }
    }


    /// <summary>
    /// Merges Log Sources into a single time ordered, filtered and paged result
    /// </summary>
    public static class TrakHoundLogQueryEngine
    {
        public static long GetFrom(TrakHoundLogQuery query) => query.From == default ? 0 : ToKind(query.From).ToUnixTime();

        public static long GetTo(TrakHoundLogQuery query) => query.To == default ? long.MaxValue : ToKind(query.To).ToUnixTime();


        /// <param name="senderLoggerIds">The Loggers whose entries are only included when their Sender matches (see <see cref="TrakHoundLogFilter.SenderLoggerIds"/>)</param>
        public static TrakHoundLogQueryResult Query(IEnumerable<ITrakHoundLogSource> sources, TrakHoundLogQuery query, IReadOnlySet<string> senderLoggerIds = null)
        {
            var descending = query.SortOrder == TrakHoundSortOrder.Descending;
            var from = GetFrom(query);
            var to = GetTo(query);
            var take = Math.Max(0, query.Take);
            var skip = Math.Max(0, query.Skip);

            // Cursor : "{timestamp}:{count}" = continue after the first {count} entries at {timestamp} (in the same order)
            var hasCursor = TryParseCursor(query.Cursor, out var cursorTimestamp, out var cursorCount);
            if (hasCursor)
            {
                if (descending) to = Math.Min(to, cursorTimestamp);
                else from = Math.Max(from, cursorTimestamp);
            }

            // The loop below reads at most the Cursor entries, Skip, Take and one more (to set HasMore)
            var filter = new TrakHoundLogFilter(query, from, to);
            filter.SenderLoggerIds = senderLoggerIds;
            filter.Limit = (hasCursor ? Math.Max(0, cursorCount) : 0) + (long)skip + take + 1;

            var items = new List<TrakHoundLogItem>();
            var hasMore = false;
            string nextCursor = null;

            var currentTimestamp = hasCursor ? cursorTimestamp : long.MinValue;
            var currentCount = 0;

            foreach (var record in Merge(sources, filter, descending))
            {
                // Skip the entries at the Cursor timestamp that were already returned
                if (hasCursor && record.Timestamp == cursorTimestamp && cursorCount > 0)
                {
                    cursorCount--;
                    currentCount++;
                    continue;
                }

                if (items.Count >= take)
                {
                    hasMore = true;
                    break;
                }

                if (record.Timestamp == currentTimestamp) currentCount++;
                else
                {
                    currentTimestamp = record.Timestamp;
                    currentCount = 1;
                }

                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                items.Add(record.ToItem());
                nextCursor = $"{currentTimestamp}:{currentCount}";
            }

            return new TrakHoundLogQueryResult
            {
                Items = items,
                HasMore = hasMore,
                NextCursor = hasMore ? nextCursor : null
            };
        }

        public static TrakHoundLoggerLogCount[] Count(IEnumerable<ITrakHoundLogSource> sources, TrakHoundLogQuery query, IReadOnlySet<string> senderLoggerIds = null)
        {
            var filter = new TrakHoundLogFilter(query, GetFrom(query), GetTo(query));
            filter.SenderLoggerIds = senderLoggerIds;
            var counts = new Dictionary<string, TrakHoundLoggerLogCount>();

            foreach (var source in sources)
            {
                if (source.MaxTimestamp < filter.From || source.MinTimestamp > filter.To) continue;

                foreach (var record in source.Read(filter, false))
                {
                    if (!counts.TryGetValue(record.LoggerId, out var count))
                    {
                        count = new TrakHoundLoggerLogCount { LoggerId = record.LoggerId, LoggerName = record.LoggerName };
                        counts.Add(record.LoggerId, count);
                    }

                    count.Counts[record.Level] = count.Counts.GetValueOrDefault(record.Level) + 1;
                }
            }

            return counts.Values.OrderBy(o => o.LoggerName ?? o.LoggerId).ToArray();
        }

        public static void AddCounts(Dictionary<string, TrakHoundLoggerLogCount> target, IEnumerable<TrakHoundLoggerLogCount> counts)
        {
            if (counts == null) return;

            foreach (var count in counts)
            {
                if (!target.TryGetValue(count.LoggerId, out var existing))
                {
                    existing = new TrakHoundLoggerLogCount { LoggerId = count.LoggerId, LoggerName = count.LoggerName };
                    target.Add(count.LoggerId, existing);
                }

                foreach (var entry in count.Counts)
                {
                    existing.Counts[entry.Key] = existing.Counts.GetValueOrDefault(entry.Key) + entry.Value;
                }
            }
        }


        /// <summary>
        /// K-way merge of the Sources. Sources are only opened once the merge reaches their time range,
        /// so a query over many days only has a few files open at a time.
        /// </summary>
        private static IEnumerable<TrakHoundLogRecord> Merge(IEnumerable<ITrakHoundLogSource> sources, TrakHoundLogFilter filter, bool descending)
        {
            // Pending Sources in the order they become active
            var pending = new Queue<(ITrakHoundLogSource Source, int Order)>(
                sources
                    .Where(o => o.MaxTimestamp >= filter.From && o.MinTimestamp <= filter.To)
                    .Select((o, i) => (o, i))
                    .OrderBy(o => descending ? -o.o.MaxTimestamp : o.o.MinTimestamp)
                    .ThenBy(o => o.i)
                    .Select(o => (o.o, o.i)));

            var comparer = Comparer<(long Timestamp, int Order)>.Create((a, b) =>
            {
                var c = descending ? b.Timestamp.CompareTo(a.Timestamp) : a.Timestamp.CompareTo(b.Timestamp);
                return c != 0 ? c : a.Order.CompareTo(b.Order);
            });

            var heap = new PriorityQueue<(IEnumerator<TrakHoundLogRecord> Enumerator, int Order), (long Timestamp, int Order)>(comparer);
            var open = new List<IEnumerator<TrakHoundLogRecord>>();

            try
            {
                while (true)
                {
                    // Activate the Sources whose range could contain the next entry
                    while (pending.Count > 0)
                    {
                        var next = pending.Peek();
                        if (heap.Count > 0)
                        {
                            heap.TryPeek(out _, out var top);
                            var reachable = descending ? next.Source.MaxTimestamp >= top.Timestamp : next.Source.MinTimestamp <= top.Timestamp;
                            if (!reachable) break;
                        }

                        pending.Dequeue();

                        var enumerator = next.Source.Read(filter, descending).GetEnumerator();
                        open.Add(enumerator);
                        if (enumerator.MoveNext()) heap.Enqueue((enumerator, next.Order), (enumerator.Current.Timestamp, next.Order));
                    }

                    if (heap.Count == 0) yield break;

                    var item = heap.Dequeue();
                    yield return item.Enumerator.Current;

                    if (item.Enumerator.MoveNext()) heap.Enqueue(item, (item.Enumerator.Current.Timestamp, item.Order));
                }
            }
            finally
            {
                foreach (var enumerator in open)
                {
                    try { enumerator.Dispose(); } catch { }
                }
            }
        }

        private static bool TryParseCursor(string cursor, out long timestamp, out int count)
        {
            timestamp = 0;
            count = 0;

            if (string.IsNullOrEmpty(cursor)) return false;

            var parts = cursor.Split(':');
            return parts.Length == 2 &&
                   long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out timestamp) &&
                   int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
        }

        // Query ranges without a Kind (ex. from a date picker) are Local times
        private static DateTime ToKind(DateTime value)
        {
            return value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Local) : value;
        }
    }
}
