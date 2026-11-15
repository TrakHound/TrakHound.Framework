// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Logging;

namespace TrakHound.Buffers
{
    class TrakHoundOperationReadBuffer<TItem>
    {
        private const int _maxPageFailures = 5;
        private const int _stopTimeout = 5000; // 5 Seconds

        private readonly string _bufferId;
        private readonly int _readInterval;
        private readonly int _batchSize;
        private readonly int _queueLimit;
        private readonly OperationFileBuffer<TItem> _fileBuffer;
        private readonly Func<int> _getQueueCount;
        private readonly Func<Task<bool>> _getServiceAvailability;
        private readonly Func<bool> _hasPendingWrites;
        private readonly ITrakHoundLogger _logger;
        private readonly TrakHoundBufferMetrics _metrics;
        private readonly object _lock = new object();

        // Pages listed from disk, read in order. Refreshed once exhausted
        private readonly Queue<ulong> _pageSequences = new Queue<ulong>();
        // Pages that were fully read but could not be deleted (skipped until deleted)
        private readonly HashSet<ulong> _undeletedPages = new HashSet<ulong>();
        private readonly Dictionary<ulong, int> _pageFailures = new Dictionary<ulong, int>();

        // The current Page's Items (delivered over one or more passes as Working Queue room allows)
        private TItem[] _pageItems;
        private int _pageItemIndex;
        private ulong _pageSequence;

        private bool _isStarted;
        private volatile bool _active;
        private CancellationTokenSource _stop;
        private Task _workerTask;


        public event EventHandler<TItem[]> ItemsRead;


        public bool Active => _active;


        /// <param name="bufferId">The Identifier of the parent Operation Buffer (used in log messages)</param>
        /// <param name="readInterval">The interval (in milliseconds) at which the File Buffer is read (minimum 1)</param>
        /// <param name="readIntervalLimit">The maximum number of Items delivered per read</param>
        /// <param name="queueLimit">The maximum number of Items in the Working Queue (Items are only delivered when there is room)</param>
        /// <param name="fileBuffer">The File Buffer to read Pages from</param>
        /// <param name="getQueueCount">Returns the current number of Items in the Working Queue</param>
        /// <param name="getServiceAvailability">Returns whether the Service is available (Items are only delivered while it is)</param>
        /// <param name="hasPendingWrites">Whether the Write Buffer has Items not yet written to Pages (the Read Buffer stays Active until they are)</param>
        /// <param name="logger">The Logger to write log messages to</param>
        /// <param name="metrics">The parent Buffer's Metrics (File Buffer read Metrics are updated here)</param>
        public TrakHoundOperationReadBuffer(
            string bufferId,
            int readInterval,
            int readIntervalLimit,
            int queueLimit,
            OperationFileBuffer<TItem> fileBuffer,
            Func<int> getQueueCount,
            Func<Task<bool>> getServiceAvailability,
            Func<bool> hasPendingWrites,
            ITrakHoundLogger logger,
            TrakHoundBufferMetrics metrics
            )
        {
            _bufferId = bufferId;
            _readInterval = Math.Max(readInterval, 1);
            _queueLimit = queueLimit;

            // A batch must fit in the Working Queue, otherwise the File Buffer would never be read
            _batchSize = Math.Max(1, Math.Min(readIntervalLimit, queueLimit));

            _fileBuffer = fileBuffer;
            _getQueueCount = getQueueCount;
            _getServiceAvailability = getServiceAvailability;
            _hasPendingWrites = hasPendingWrites;
            _logger = logger;
            _metrics = metrics;
        }


        public void Start()
        {
            lock (_lock)
            {
                if (_isStarted) return;

                _logger.Log(TrakHoundLogLevel.Trace, $"Read Buffer {_bufferId} : Starting..");

                // Default to Active until the File Buffer is found to be empty.
                // Prevents Items being added to the Working Queue ahead of older buffered Items
                SetActive(true);

                // Re-list Pages (Recovery Pages may have been written since the last Stop)
                _pageSequences.Clear();

                _isStarted = true;
                _stop = new CancellationTokenSource();

                var cancellationToken = _stop.Token;
                _workerTask = Task.Run(() => BufferReadWorker(cancellationToken));
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isStarted) return;

                _logger.Log(TrakHoundLogLevel.Trace, $"Read Buffer {_bufferId} : Stopping..");

                _stop?.Cancel();

                var workerStopped = true;
                if (_workerTask != null)
                {
                    try
                    {
                        workerStopped = _workerTask.Wait(_stopTimeout);
                        if (!workerStopped)
                        {
                            _logger.Log(TrakHoundLogLevel.Warning, $"Read Buffer {_bufferId} : Worker did not stop within {_stopTimeout}ms");
                        }
                    }
                    catch (AggregateException) { }
                }

                // Save the undelivered remainder of a partially delivered Page
                if (workerStopped) SavePartialPage();

                _stop?.Dispose();
                _stop = null;
                _workerTask = null;
                _isStarted = false;

                SetActive(false);

                _logger.Log(TrakHoundLogLevel.Trace, $"Read Buffer {_bufferId} : Stopped");
            }
        }


        private async Task BufferReadWorker(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(_readInterval, cancellationToken);

                    try
                    {
                        RetryUndeletedPages();

                        await ReadPages(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.Log(TrakHoundLogLevel.Error, $"Read Buffer {_bufferId} : Worker Error : {ex.Message}");
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                _logger.Log(TrakHoundLogLevel.Error, $"Read Buffer {_bufferId} : Worker Loop Error : {ex.Message}");
            }
        }

        private async Task ReadPages(CancellationToken cancellationToken)
        {
            // Service Availability is only checked once there are Items to deliver (so an empty File Buffer doesn't call the Service)
            var availabilityChecked = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (_pageItems == null)
                {
                    var result = LoadNextPage();
                    if (result == PageLoadResult.Empty)
                    {
                        // Only go Idle once nothing is waiting to be written either (preserves Item order)
                        if (_hasPendingWrites == null || !_hasPendingWrites()) SetActive(false);
                        return;
                    }
                    if (result == PageLoadResult.Retry) return;
                    if (_pageItems == null) continue; // Page skipped, try the next one
                }

                SetActive(true);

                // Deliver as many Items as the Working Queue has room for (in batches)
                var remaining = _pageItems.Length - _pageItemIndex;
                if (remaining > 0)
                {
                    var room = _queueLimit - _getQueueCount();
                    var count = Math.Min(Math.Min(_batchSize, remaining), room);
                    if (count < Math.Min(_batchSize, remaining)) return; // Wait for room in the Working Queue

                    var handler = ItemsRead;
                    if (handler == null) return; // Nothing to deliver to, so keep the Items

                    // No need to deliver Items if they will be put right back in (the Page is kept until fully delivered)
                    if (!availabilityChecked)
                    {
                        if (_getServiceAvailability != null && !await _getServiceAvailability()) return;
                        availabilityChecked = true;
                    }

                    var items = new TItem[count];
                    Array.Copy(_pageItems, _pageItemIndex, items, 0, count);

                    handler.Invoke(this, items);

                    _pageItemIndex += count;
                    _metrics.FileBuffer.TotalReadCount += count;
                    _metrics.FileBuffer.LastUpdated = UnixDateTime.Now;

                    _logger.Log(TrakHoundLogLevel.Debug, $"Read Buffer {_bufferId} : {count} Items Read From File Buffer");
                }

                if (_pageItemIndex >= _pageItems.Length)
                {
                    // Page fully delivered
                    DeletePage(_pageSequence);
                    _pageItems = null;
                    _pageItemIndex = 0;
                }
            }
        }


        private enum PageLoadResult
        {
            Loaded,
            Empty,
            Retry
        }

        private PageLoadResult LoadNextPage()
        {
            // List Pages once and work through them (new Pages always have higher Sequences)
            if (_pageSequences.Count < 1)
            {
                foreach (var sequence in _fileBuffer.GetPageSequences())
                {
                    if (!_undeletedPages.Contains(sequence)) _pageSequences.Enqueue(sequence);
                }

                _metrics.FileBuffer.PageCount = _pageSequences.Count;
                if (_pageSequences.Count < 1) return PageLoadResult.Empty;
            }

            var pageSequence = _pageSequences.Peek();
            var pagePath = _fileBuffer.GetPagePath(pageSequence);

            // Stop the Write Buffer appending to this Page before reading it
            _fileBuffer.SealPage(pageSequence);

            byte[] pageBuffer = null;
            try
            {
                _logger.Log(TrakHoundLogLevel.Trace, $"Read Buffer {_bufferId} : Reading Page Sequence = {pageSequence}");

                var items = new List<TItem>();
                int pageSize;
                int errorCount;

                using (var readStream = new FileStream(pagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    // Read the whole Page at once
                    pageSize = (int)readStream.Length;
                    pageBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(pageSize, 1));
                    readStream.ReadExactly(pageBuffer, 0, pageSize);
                }

                _fileBuffer.ReadPage(new ReadOnlyMemory<byte>(pageBuffer, 0, pageSize), items, out errorCount);

                if (errorCount > 0)
                {
                    _logger.Log(TrakHoundLogLevel.Error, $"Read Buffer {_bufferId} : Page Sequence ({pageSequence}) : {errorCount} Items could not be read and were skipped");
                }

                _pageSequences.Dequeue();
                _pageFailures.Remove(pageSequence);

                _pageItems = items.ToArray();
                _pageItemIndex = 0;
                _pageSequence = pageSequence;

                _metrics.FileBuffer.TotalBytesRead += pageSize;
                _metrics.FileBuffer.ReadPageSequence = pageSequence;
                _metrics.FileBuffer.PageCount = _pageSequences.Count + 1;

                return PageLoadResult.Loaded;
            }
            catch (FileNotFoundException)
            {
                // Page was removed since it was listed. Skip it
                _pageSequences.Dequeue();
                return PageLoadResult.Loaded;
            }
            catch (IOException ex)
            {
                // Usually the Page is in use by the Write Buffer. Try again on the next interval
                _logger.Log(TrakHoundLogLevel.Debug, $"Read Buffer {_bufferId} : Page Sequence ({pageSequence}) Currently in Use : Retrying.. : {ex.Message}");
                return PageLoadResult.Retry;
            }
            catch (Exception ex)
            {
                _pageFailures.TryGetValue(pageSequence, out var failures);
                failures++;
                _pageFailures[pageSequence] = failures;

                if (failures >= _maxPageFailures)
                {
                    // Move the Page aside so it doesn't block the rest of the File Buffer
                    _pageSequences.Dequeue();
                    _pageFailures.Remove(pageSequence);

                    try
                    {
                        var failedPath = _fileBuffer.QuarantinePage(pageSequence);
                        _logger.Log(TrakHoundLogLevel.Error, $"Read Buffer {_bufferId} : Page Sequence ({pageSequence}) failed {failures} times and was moved to {failedPath} : {ex.Message}");
                    }
                    catch (Exception moveEx)
                    {
                        _undeletedPages.Add(pageSequence);
                        _logger.Log(TrakHoundLogLevel.Error, $"Read Buffer {_bufferId} : Page Sequence ({pageSequence}) failed {failures} times and could not be moved : {moveEx.Message}");
                    }
                }
                else if (failures == 1)
                {
                    _logger.Log(TrakHoundLogLevel.Error, $"Read Buffer {_bufferId} : Page Sequence ({pageSequence}) Read Error : {ex.Message}");
                }

                return PageLoadResult.Retry;
            }
            finally
            {
                if (pageBuffer != null) ArrayPool<byte>.Shared.Return(pageBuffer);
            }
        }

        private void DeletePage(ulong pageSequence)
        {
            try
            {
                File.Delete(_fileBuffer.GetPagePath(pageSequence));
                _metrics.FileBuffer.PageCount = _pageSequences.Count;
            }
            catch (Exception ex)
            {
                // Don't read the Page again (its Items were already delivered). Retry the delete later
                _undeletedPages.Add(pageSequence);
                _logger.Log(TrakHoundLogLevel.Debug, $"Read Buffer {_bufferId} : Page Sequence ({pageSequence}) could not be deleted : Retrying.. : {ex.Message}");
            }
        }

        private void RetryUndeletedPages()
        {
            if (_undeletedPages.Count < 1) return;

            foreach (var pageSequence in new List<ulong>(_undeletedPages))
            {
                try
                {
                    File.Delete(_fileBuffer.GetPagePath(pageSequence));
                    _undeletedPages.Remove(pageSequence);
                }
                catch (Exception) { }
            }
        }

        private void SavePartialPage()
        {
            if (_pageItems == null || _pageItemIndex < 1 || _pageItemIndex >= _pageItems.Length) return;

            var remaining = _pageItems.Length - _pageItemIndex;

            try
            {
                // Delivered Items are flushed to Recovery Pages by the Operation Buffer,
                // so only keep the undelivered Items in this Page
                _fileBuffer.RewritePage(_pageSequence, _pageItems, _pageItemIndex, remaining);

                _logger.Log(TrakHoundLogLevel.Trace, $"Read Buffer {_bufferId} : Page Sequence ({_pageSequence}) : {remaining} undelivered Items saved");
            }
            catch (Exception ex)
            {
                // The original Page is left in place, so delivered Items will be read again (duplicates rather than loss)
                _logger.Log(TrakHoundLogLevel.Warning, $"Read Buffer {_bufferId} : Page Sequence ({_pageSequence}) could not be rewritten : {_pageItemIndex} Items may be read again : {ex.Message}");
            }

            _pageItems = null;
            _pageItemIndex = 0;
        }

        private void SetActive(bool active)
        {
            _active = active;
            _metrics.FileBuffer.IsReadActive = active;
        }
    }
}
