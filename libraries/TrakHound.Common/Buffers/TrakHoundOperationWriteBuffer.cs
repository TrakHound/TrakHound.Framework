// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Logging;

namespace TrakHound.Buffers
{
    class TrakHoundOperationWriteBuffer<TItem>
    {
        // Recovery Pages use Sequences below _firstPageSequence so they are read first.
        // They hold the Working Queue's Items on Stop, which are always older than any Items in the File Buffer
        private const ulong _firstPageSequence = 1000;
        private const ulong _firstRecoveryPageSequence = 1;
        private const int _maxPageAttempts = 10;
        private const int _stopTimeout = 5000; // 5 Seconds

        private readonly string _bufferId;
        private readonly int _writeInterval;
        private readonly int _writeIntervalLimit;
        private readonly int _pageLimit;
        private readonly OperationItemQueue<TItem> _writeQueue;
        private readonly OperationFileBuffer<TItem> _fileBuffer;
        private readonly ITrakHoundLogger _logger;
        private readonly TrakHoundBufferMetrics _metrics;
        private readonly object _lock = new object();
        private readonly object _writeLock = new object();

        private CancellationTokenSource _stop;
        private Task _workerTask;
        private bool _isStarted;
        private ulong _lastPageSequence;


        /// <summary>
        /// Gets the Unique Identifier for the Buffer
        /// </summary>
        public string Id => _bufferId;

        /// <summary>
        /// Gets the number of Items that are currently in the Queue
        /// </summary>
        public long QueuedItemCount => _writeQueue.Count;


        /// <param name="bufferId">The Identifier of the parent Operation Buffer (used in log messages)</param>
        /// <param name="writeInterval">The interval (in milliseconds) at which queued Items are written to the File Buffer (minimum 1)</param>
        /// <param name="writeIntervalLimit">The maximum number of Items written per interval</param>
        /// <param name="queueLimit">The maximum number of Items held in the Write Queue (additional Items are written immediately)</param>
        /// <param name="pageLimit">The maximum size (in bytes) of a Page before a new Page is started</param>
        /// <param name="fileBuffer">The File Buffer to write Pages to</param>
        /// <param name="logger">The Logger to write log messages to</param>
        /// <param name="metrics">The parent Buffer's Metrics (File Buffer write Metrics are updated here)</param>
        public TrakHoundOperationWriteBuffer(
            string bufferId,
            int writeInterval,
            int writeIntervalLimit,
            int queueLimit,
            int pageLimit,
            OperationFileBuffer<TItem> fileBuffer,
            ITrakHoundLogger logger,
            TrakHoundBufferMetrics metrics
            )
        {
            _bufferId = bufferId;
            _writeInterval = Math.Max(writeInterval, 1);
            _writeIntervalLimit = Math.Max(writeIntervalLimit, 1);
            _pageLimit = pageLimit;
            _writeQueue = new OperationItemQueue<TItem>(queueLimit);
            _fileBuffer = fileBuffer;
            _logger = logger;
            _metrics = metrics;
        }


        public void Start()
        {
            lock (_lock)
            {
                if (_isStarted) return;

                _isStarted = true;

                _stop = new CancellationTokenSource();

                var cancellationToken = _stop.Token;
                _workerTask = Task.Run(() => BufferWriteWorker(cancellationToken));

                _logger.Log(TrakHoundLogLevel.Trace, $"Write Buffer {Id} : Started");
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isStarted) return;

                _logger.Log(TrakHoundLogLevel.Trace, $"Write Buffer {Id} : Stopping..");

                _stop?.Cancel();

                if (_workerTask != null)
                {
                    try
                    {
                        if (!_workerTask.Wait(_stopTimeout))
                        {
                            _logger.Log(TrakHoundLogLevel.Warning, $"Write Buffer {Id} : Worker did not stop within {_stopTimeout}ms");
                        }
                    }
                    catch (AggregateException) { }
                }

                // Flush any remaining Items to File Buffer (these are the newest, so use regular sequences)
                FlushBuffer();

                // Start a new Page next time
                _fileBuffer.SealPage(_fileBuffer.ActiveWritePage);

                _stop?.Dispose();
                _stop = null;
                _workerTask = null;

                _isStarted = false;
                _metrics.FileBuffer.IsWriteActive = false;
            }
        }


        /// <summary>
        /// Adds an Item to be written to the File Buffer. If the Write Queue is full,
        /// the Queue and the Item are written to the File Buffer immediately
        /// </summary>
        public bool Add(TItem item)
        {
            if (item == null) return false;

            if (_writeQueue.Add(item)) return true;

            return WriteOverflow(new[] { item }) == 1;
        }

        /// <summary>
        /// Adds Items to be written to the File Buffer. Items that don't fit in the Write Queue
        /// are written to the File Buffer immediately (after the Items already queued)
        /// </summary>
        public bool Add(IEnumerable<TItem> items)
        {
            if (items.IsNullOrEmpty()) return false;

            var itemList = items as IReadOnlyList<TItem> ?? items.ToArray();

            var added = _writeQueue.AddRange(itemList);
            if (added >= itemList.Count) return true;

            var overflow = itemList.Skip(added).Where(o => o != null).ToArray();
            return WriteOverflow(overflow) == overflow.Length;
        }


        private async Task BufferWriteWorker(CancellationToken cancellationToken)
        {
            var itemBuffer = new TItem[_writeIntervalLimit];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Delay for the set Write Interval
                    await Task.Delay(_writeInterval, cancellationToken);

                    try
                    {
                        var written = WriteQueued(itemBuffer);
                        _metrics.FileBuffer.IsWriteActive = written > 0;
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(TrakHoundLogLevel.Error, $"Write Buffer {Id} : Worker Error : {ex.Message}");
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                _logger.Log(TrakHoundLogLevel.Error, $"Write Buffer {Id} : Worker Loop Error : {ex.Message}");
            }

            _logger.Log(TrakHoundLogLevel.Trace, $"Write Buffer {Id} : Stopped");
        }

        /// <summary>
        /// Writes up to one batch from the Write Queue. Items are only removed from the Queue once written,
        /// so a failed write is retried on the next interval
        /// </summary>
        private int WriteQueued(TItem[] itemBuffer)
        {
            lock (_writeLock)
            {
                var count = _writeQueue.Get(ref itemBuffer, 0, itemBuffer.Length);
                if (count < 1) return 0;

                var written = Write(itemBuffer, 0, count, false, false);
                _writeQueue.Remove(written);

                Array.Clear(itemBuffer, 0, count);

                return written;
            }
        }

        /// <summary>
        /// Drains the Write Queue (to preserve order) and then writes the overflow Items
        /// </summary>
        private int WriteOverflow(TItem[] items)
        {
            if (items.Length < 1) return 0;

            lock (_writeLock)
            {
                var itemBuffer = new TItem[_writeIntervalLimit];
                while (_writeQueue.Count > 0)
                {
                    if (WriteQueued(itemBuffer) < 1) break;
                }

                var written = Write(items, 0, items.Length, false, false);
                if (written < items.Length)
                {
                    _logger.Log(TrakHoundLogLevel.Error, $"Write Buffer {Id} : Write Queue Full : {items.Length - written} Items could not be written to File Buffer");
                }

                return written;
            }
        }


        /// <summary>
        /// Writes Items to the File Buffer. Regular Items are appended to the Active Write Page until it reaches the Page Limit
        /// (or the Read Buffer seals it). Recovery Items are always written to new Recovery Pages.
        /// Returns the number of Items consumed. Must be called while holding _writeLock
        /// </summary>
        private int Write(TItem[] items, int itemIndex, int itemCount, bool recovery, bool flushToDisk)
        {
            if (_fileBuffer == null || items == null || itemCount < 1) return 0;

            var end = itemIndex + itemCount;
            var position = itemIndex;
            var attempts = 0;
            ulong recoverySequence = 0;

            while (position < end && attempts < _maxPageAttempts)
            {
                // Held for the whole append, so the Read Buffer can't seal (and read) the Page part way through
                lock (_fileBuffer.WritePageLock)
                {
                    ulong pageSequence;
                    if (recovery)
                    {
                        pageSequence = GetNextRecoveryPageSequence(ref recoverySequence);
                    }
                    else
                    {
                        if (_fileBuffer.ActiveWritePage == 0) _fileBuffer.ActiveWritePage = GetNextPageSequence();
                        pageSequence = _fileBuffer.ActiveWritePage;
                    }

                    var filePath = _fileBuffer.GetPagePath(pageSequence, true);
                    long originalLength = -1;

                    try
                    {
                        _logger.Log(TrakHoundLogLevel.Trace, $"Write Buffer {Id} : Writing to Page Sequence = {pageSequence}");

                        int pageSize;
                        int consumed;
                        int errorCount;

                        var fileMode = recovery ? FileMode.CreateNew : FileMode.OpenOrCreate;
                        using (var fileStream = new FileStream(filePath, fileMode, FileAccess.Write, FileShare.None))
                        {
                            originalLength = fileStream.Length;
                            if (originalLength >= _pageLimit)
                            {
                                // Page already full, start a new one
                                _fileBuffer.ActiveWritePage = 0;
                                continue;
                            }

                            fileStream.Seek(0, SeekOrigin.End);
                            pageSize = (int)originalLength;

                            consumed = _fileBuffer.Write(fileStream, ref pageSize, _pageLimit, ref items, position, end - position, out errorCount);

                            // Flush inside the try so a failure (ex. disk full) is caught before the Items are counted as written
                            fileStream.Flush(flushToDisk);
                        }

                        position += consumed;

                        // Page full, the next write starts a new Page
                        if (!recovery && pageSize >= _pageLimit) _fileBuffer.ActiveWritePage = 0;

                        if (errorCount > 0)
                        {
                            _logger.Log(TrakHoundLogLevel.Error, $"Write Buffer {Id} : {errorCount} Items could not be serialized and were skipped");
                        }

                        _metrics.FileBuffer.TotalWriteCount += consumed - errorCount;
                        _metrics.FileBuffer.TotalBytesWritten += pageSize - originalLength;
                        _metrics.FileBuffer.WritePageSequence = pageSequence;
                        _metrics.FileBuffer.LastUpdated = UnixDateTime.Now;
                    }
                    catch (Exception ex)
                    {
                        attempts++;

                        // IOException / UnauthorizedAccessException are usually another process blocking the file.
                        var logLevel = ex is IOException || ex is UnauthorizedAccessException ? TrakHoundLogLevel.Debug : TrakHoundLogLevel.Error;
                        _logger.Log(logLevel, $"Write Buffer {Id} : Page Sequence ({pageSequence}) Write Failed : {ex.Message}");

                        // Remove what this attempt wrote (if the Page was opened) so its Items aren't read twice when retried
                        if (originalLength >= 0) RollbackPage(filePath, originalLength);

                        // Don't append to this Page again
                        if (!recovery && _fileBuffer.ActiveWritePage == pageSequence) _fileBuffer.ActiveWritePage = 0;
                    }
                }
            }

            if (position < end)
            {
                _logger.Log(TrakHoundLogLevel.Error, $"Write Buffer {Id} : {end - position} Items not written after {attempts} attempts");
            }

            return position - itemIndex;
        }

        private ulong GetNextRecoveryPageSequence(ref ulong recoverySequence)
        {
            if (recoverySequence == 0)
            {
                // Continue after any Recovery Pages that haven't been read yet (from a previous Stop)
                var lastRecovery = _fileBuffer.GetPageSequences().Where(o => o < _firstPageSequence).DefaultIfEmpty(_firstRecoveryPageSequence - 1).Max();
                recoverySequence = lastRecovery;
            }

            recoverySequence++;

            if (recoverySequence >= _firstPageSequence)
            {
                // Recovery range exhausted, fall back to a regular Page so it doesn't collide
                _logger.Log(TrakHoundLogLevel.Warning, $"Write Buffer {Id} : Recovery Page Sequences exhausted : Writing to regular Page Sequence");
                return GetNextPageSequence();
            }

            return recoverySequence;
        }

        private ulong GetNextPageSequence()
        {
            if (_lastPageSequence < _firstPageSequence)
            {
                // Read the last Page Sequence from disk once (only this class writes Pages)
                _lastPageSequence = Math.Max(_fileBuffer.GetLastPageSequence(), _firstPageSequence - 1);
            }

            return ++_lastPageSequence;
        }

        private void RollbackPage(string filePath, long length)
        {
            try
            {
                if (length < 1)
                {
                    File.Delete(filePath);
                }
                else
                {
                    using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                    {
                        fileStream.SetLength(length);
                    }
                }
            }
            catch (Exception ex)
            {
                // Page may end with a partial record (skipped on read) and Items that will be written again
                _logger.Log(TrakHoundLogLevel.Warning, $"Write Buffer {Id} : Page could not be rolled back ({filePath}) : {ex.Message}");
            }
        }

        /// <summary>
        /// Writes Items directly to Recovery Pages (flushed to disk), so they are read before all other Pages.
        /// Used on Stop for the Working Queue's Items. Returns the number of Items written
        /// </summary>
        public int WriteRecovery(TItem[] items, int itemCount)
        {
            lock (_writeLock)
            {
                var written = Write(items, 0, itemCount, true, true);
                if (written < itemCount)
                {
                    _logger.Log(TrakHoundLogLevel.Error, $"Write Buffer {Id} : Recovery : {itemCount - written} Items could not be written to File Buffer");
                }

                return written;
            }
        }

        private void FlushBuffer()
        {
            if (_fileBuffer == null) return;

            lock (_writeLock)
            {
                var queueCount = _writeQueue.Count;
                if (queueCount < 1) return;

                _logger.Log(TrakHoundLogLevel.Trace, $"Write Buffer {Id} : Flushing {queueCount} Items to File Buffer");

                var items = new TItem[queueCount];
                var count = _writeQueue.Get(ref items, 0, queueCount);

                // Flush to disk, as the process is likely about to exit
                var written = Write(items, 0, count, false, true);
                _writeQueue.Remove(written);

                if (written < count)
                {
                    _logger.Log(TrakHoundLogLevel.Error, $"Write Buffer {Id} : Flush : {count - written} Items could not be written to File Buffer");
                }
            }
        }
    }
}
