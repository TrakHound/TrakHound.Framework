// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace TrakHound.Buffers
{
    /// <summary>
    /// Persists Items to Page Files on disk (under the "buffer" directory) so they can be read back and processed later
    /// </summary>
    public class OperationFileBuffer<TItem> : IDisposable
    {
        private const int _sizeInterval = 10000;

        private const int _pageSequenceDigitCount = 20;
        private static readonly string _pageSequenceDigitFormat = CreateDigitFormat(_pageSequenceDigitCount);
        private const int _recordLengthSize = 4;
        private const byte _legacyTerminator = 13;
        private const byte _legacyEmpty = 255;

        /// <summary>
        /// Marks a Page as the length-prefixed format. Legacy Pages can't start with 255 (it was a terminator)
        /// </summary>
        public static readonly byte[] PageHeader = new byte[] { 0xFF, (byte)'T', (byte)'H', (byte)'B', 0x02 };

        /// <summary>
        /// The name of the root directory (next to the entry Assembly) that File Buffers are stored in
        /// </summary>
        public const string DirectoryBuffer = "buffer";

        /// <summary>
        /// The name of the subdirectory that Pages that repeatedly fail to read are moved to
        /// </summary>
        public const string DirectoryFailed = "failed";

        /// <summary>
        /// The name of the subdirectory used for temporary files while rewriting a Page
        /// </summary>
        public const string DirectoryTemp = "temp";

        private readonly string _id;
        private readonly Func<ReadOnlyMemory<byte>, TItem> _deserializeFunction;
        private readonly Func<Stream, TItem, int> _serializeFunction;
        private readonly int _pageLimit;
        private readonly System.Timers.Timer _sizeTimer;
        private readonly object _writePageLock = new object();

        private long _directorySize;


        /// <summary>
        /// The Identifier of the Buffer (used to name the Buffer's directory)
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// The maximum size (in bytes) a Page is written to before a new Page is started
        /// </summary>
        public int PageLimit => _pageLimit;

        /// <summary>
        /// The total size (in bytes) of the Page Files in the Buffer's directory (refreshed every 10 seconds)
        /// </summary>
        public long DirectorySize => _directorySize;

        /// <summary>
        /// Held by the Write Buffer while appending to the Active Write Page, and by the Read Buffer while sealing a Page
        /// </summary>
        public object WritePageLock => _writePageLock;

        /// <summary>
        /// The Page currently being appended to by the Write Buffer (0 = none). Only access while holding WritePageLock
        /// </summary>
        public ulong ActiveWritePage { get; set; }


        /// <summary>
        /// Creates a new File Buffer for the specified Id and starts the timer that tracks the directory size
        /// </summary>
        public OperationFileBuffer(
            string queueId,
            Func<ReadOnlyMemory<byte>, TItem> deserializeFunction,
            Func<Stream, TItem, int> serializeFunction,
            int pageLimit = 5000
            )
        {
            _id = queueId;
            _deserializeFunction = deserializeFunction;
            _serializeFunction = serializeFunction;
            _pageLimit = pageLimit;

            _sizeTimer = new System.Timers.Timer();
            _sizeTimer.Interval = _sizeInterval;
            _sizeTimer.Elapsed += SizeTimerElapsed;
            _sizeTimer.Start();

            _directorySize = GetDirectorySize(_id);
        }

        /// <summary>
        /// Stops and disposes the directory size timer
        /// </summary>
        public void Dispose()
        {
            _sizeTimer.Elapsed -= SizeTimerElapsed;
            _sizeTimer.Dispose();
        }

        private void SizeTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _directorySize = GetDirectorySize(_id);
        }

        /// <summary>
        /// Gets the Sequence of the oldest Page File in the Buffer (returns minimumSequence if there are no Pages)
        /// </summary>
        public ulong GetFirstPageSequence(ulong minimumSequence = 0)
        {
            var dirPath = GetDirectory(_id);
            var files = Directory.GetFiles(dirPath, "*", SearchOption.TopDirectoryOnly);
            if (!files.IsNullOrEmpty())
            {
                var nextPath = files.OrderBy(o => o).FirstOrDefault();
                if (!string.IsNullOrEmpty(nextPath))
                {
                    var filename = Path.GetFileNameWithoutExtension(nextPath);
                    var seqStr = filename.Substring(1);
                    return seqStr.ToULong();
                }
            }

            return minimumSequence;
        }

        /// <summary>
        /// Gets the Sequence of the newest Page File in the Buffer (returns minimumSequence if there are no Pages)
        /// </summary>
        public ulong GetLastPageSequence(ulong minimumSequence = 0)
        {
            var dirPath = GetDirectory(_id);
            var files = Directory.GetFiles(dirPath, "*", SearchOption.TopDirectoryOnly);
            if (!files.IsNullOrEmpty())
            {
                var nextPath = files.OrderByDescending(o => o).FirstOrDefault();
                if (!string.IsNullOrEmpty(nextPath))
                {
                    var filename = Path.GetFileNameWithoutExtension(nextPath);
                    var seqStr = filename.Substring(1);
                    return seqStr.ToULong();
                }
            }

            return minimumSequence;
        }


        /// <summary>
        /// Gets the Page Filename for the Sequence ("_" followed by the zero-padded 20 digit Sequence). Returns null if the Sequence is 0
        /// </summary>
        public static string GetPageFilename(ulong sequence)
        {
            if (sequence > 0)
            {
                return $"_{sequence.ToString(_pageSequenceDigitFormat)}";
            }

            return null;
        }

        /// <summary>
        /// Gets the full path of the Page File for the Sequence (null if the Sequence is 0). Optionally creates the Buffer directory
        /// </summary>
        public string GetPagePath(ulong sequence, bool createIfNotExists = false)
        {
            var filename = GetPageFilename(sequence);
            if (filename != null)
            {
                var basePath = GetDirectory(_id, createIfNotExists);
                return Path.Combine(basePath, filename);
            }

            return null;
        }


        /// <summary>
        /// Parses all Items from a Page. Supports both the current (length-prefixed) and legacy (terminator-separated) formats.
        /// Items that fail to deserialize are skipped and counted in errorCount. Returns the number of Items added
        /// </summary>
        public int ReadPage(ReadOnlyMemory<byte> page, List<TItem> items, out int errorCount)
        {
            errorCount = 0;
            var startCount = items.Count;

            if (page.Span.StartsWith(PageHeader))
            {
                var position = PageHeader.Length;

                while (position < page.Length)
                {
                    if (page.Length - position < _recordLengthSize)
                    {
                        // Truncated record header (ex. power loss during write)
                        errorCount++;
                        break;
                    }

                    var length = BinaryPrimitives.ReadInt32LittleEndian(page.Span.Slice(position));
                    position += _recordLengthSize;

                    if (length < 0 || length > page.Length - position)
                    {
                        // Corrupt or truncated record. Can't resync, so stop reading this Page
                        errorCount++;
                        break;
                    }

                    if (length > 0) TryDeserialize(page.Slice(position, length), items, ref errorCount);
                    position += length;
                }
            }
            else
            {
                // Legacy Format : Items separated by 13 (or 255)
                var position = 0;
                while (position < page.Length)
                {
                    var remaining = page.Span.Slice(position);
                    var terminatorIndex = remaining.IndexOfAny(_legacyTerminator, _legacyEmpty);
                    var lineLength = terminatorIndex < 0 ? remaining.Length : terminatorIndex;

                    // Skip empty lines (ex. an Item that serialized to 0 bytes)
                    if (lineLength > 0) TryDeserialize(page.Slice(position, lineLength), items, ref errorCount);

                    position += lineLength + 1;
                }
            }

            return items.Count - startCount;
        }

        private void TryDeserialize(ReadOnlyMemory<byte> bytes, List<TItem> items, ref int errorCount)
        {
            try
            {
                var item = _deserializeFunction(bytes);
                if (item != null) items.Add(item);
                else errorCount++;
            }
            catch (Exception)
            {
                errorCount++;
            }
        }

        /// <summary>
        /// Writes Items to the Stream (as length-prefixed records) until itemCount Items are consumed or the pageLimit is reached.
        /// The Page Header is written first if the Stream is empty.
        /// Items that fail to serialize are skipped (and counted in errorCount). IOExceptions are not caught.
        /// Returns the number of Items consumed (written + skipped)
        /// </summary>
        public int Write(Stream stream, ref int pageSize, int pageLimit, ref TItem[] itemBuffer, int itemIndex, int itemCount, out int errorCount)
        {
            int consumed = 0;
            errorCount = 0;

            if (stream.Position == 0)
            {
                stream.Write(PageHeader);
                pageSize += PageHeader.Length;
            }

            // Serialize each Item to memory first, so a failing Serializer can't leave a partial record in the Page
            using var itemStream = new MemoryStream();
            var lengthBytes = new byte[_recordLengthSize];

            for (var i = itemIndex; i < itemIndex + itemCount; i++)
            {
                itemStream.SetLength(0);

                var serialized = false;
                try
                {
                    // Serializers return 0 on failure
                    serialized = _serializeFunction(itemStream, itemBuffer[i]) > 0 && itemStream.Length > 0;
                }
                catch (Exception) { }

                consumed++;

                if (!serialized)
                {
                    errorCount++;
                    continue;
                }

                var length = (int)itemStream.Length;
                BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, length);
                stream.Write(lengthBytes);
                stream.Write(itemStream.GetBuffer(), 0, length);

                pageSize += _recordLengthSize + length;

                if (pageSize >= pageLimit)
                {
                    break;
                }
            }

            return consumed;
        }

        /// <summary>
        /// Stops the Write Buffer from appending to a Page. Called by the Read Buffer before reading a Page,
        /// so no Items can be appended after the Page is read (and then lost when it is deleted).
        /// Blocks until any append in progress completes
        /// </summary>
        public void SealPage(ulong sequence)
        {
            lock (_writePageLock)
            {
                if (ActiveWritePage == sequence) ActiveWritePage = 0;
            }
        }

        /// <summary>
        /// Gets the Sequences of all Page Files currently in the File Buffer (sorted ascending)
        /// </summary>
        public ulong[] GetPageSequences()
        {
            var dirPath = GetDirectory(_id);
            return Directory.GetFiles(dirPath, "*", SearchOption.TopDirectoryOnly)
                .Select(o => Path.GetFileNameWithoutExtension(o).Substring(1).ToULong())
                .Where(o => o > 0)
                .OrderBy(o => o)
                .ToArray();
        }

        /// <summary>
        /// Moves a Page that repeatedly fails to read into the "failed" subdirectory (so it no longer blocks the File Buffer).
        /// Returns the new path
        /// </summary>
        public string QuarantinePage(ulong sequence)
        {
            var failedDir = Path.Combine(GetDirectory(_id), DirectoryFailed);
            if (!Directory.Exists(failedDir)) Directory.CreateDirectory(failedDir);

            var failedPath = Path.Combine(failedDir, $"{GetPageFilename(sequence)}_{UnixDateTime.Now}");
            File.Move(GetPagePath(sequence), failedPath);
            return failedPath;
        }

        /// <summary>
        /// Replaces a Page's contents with the specified Items (written to a temp file, then swapped in)
        /// </summary>
        public void RewritePage(ulong sequence, TItem[] items, int itemIndex, int itemCount)
        {
            var tempDir = Path.Combine(GetDirectory(_id), DirectoryTemp);
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            var tempPath = Path.Combine(tempDir, GetPageFilename(sequence));

            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var pageSize = 0;
                Write(fileStream, ref pageSize, int.MaxValue, ref items, itemIndex, itemCount, out _);
                fileStream.Flush(true);
            }

            File.Move(tempPath, GetPagePath(sequence), true);
        }

        /// <summary>
        /// Gets the total size (in bytes) of the files in the Buffer directory for the specified Id (0 if it does not exist or on error)
        /// </summary>
        public long GetDirectorySize(string queueId)
        {
            try
            {
                if (GetDirectoryExists(queueId))
                {
                    var dir = GetDirectory(queueId);
                    var files = Directory.GetFiles(dir);
                    if (!files.IsNullOrEmpty())
                    {
                        long x = 0;

                        foreach (var file in files)
                        {
                            var fileInfo = new FileInfo(file);
                            if (fileInfo != null) x += fileInfo.Length;
                        }

                        return x;
                    }
                }
            }
            catch (Exception) { }

            return 0;
        }


        /// <summary>
        /// Gets whether the Buffer directory for the specified Id exists
        /// </summary>
        public bool GetDirectoryExists(string queueId)
        {
            string name = $"{queueId}".ToSnakeCase().ToLower();
            string dir = TrakHoundDataDirectory.GetPath(DirectoryBuffer, name);

            return Directory.Exists(dir);
        }

        /// <summary>
        /// Gets the Buffer directory for the specified Id ("buffer/{snake_case_id}" in the Data Directory), creating it if requested
        /// </summary>
        public string GetDirectory(string queueId, bool createIfNotExists = true)
        {
            string name = $"{queueId}".ToSnakeCase().ToLower();
            string dir = TrakHoundDataDirectory.GetPath(DirectoryBuffer, name);
            if (createIfNotExists && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            return dir;
        }


        private static string CreateDigitFormat(int digitCount)
        {
            if (digitCount > 0)
            {
                var builder = new StringBuilder();
                for (var i = 0; i < digitCount; i++)
                {
                    builder.Append('0');
                }
                return builder.ToString();
            }

            return null;
        }
    }
}
