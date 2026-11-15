// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Logging;

namespace TrakHound.Buffers
{
    /// <summary>
    /// Handles an event raised for a single Item processed by an Operation Buffer
    /// </summary>
    public delegate void TrakHoundOperationEventHandler<TItem>(TItem item);

    /// <summary>
    /// An Operation Buffer that queues Items and processes them in batches
    /// </summary>
    public interface ITrakHoundOperationBuffer : IDisposable
    {
        /// <summary>
        /// The unique Identifier of the Buffer
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The runtime Metrics for the Buffer
        /// </summary>
        TrakHoundBufferMetrics Metrics { get; }
    }

    /// <summary>
    /// An Operation Buffer that queues Items of the specified type and processes them in batches
    /// </summary>
    public interface ITrakHoundOperationBuffer<TItem> : ITrakHoundOperationBuffer
    {
        /// <summary>
        /// Adds an Item to the Buffer. Returns false if the Item could not be queued or written to the File Buffer
        /// </summary>
        bool Add(TItem item);

        /// <summary>
        /// Adds Items to the Buffer. Returns false if any of the Items could not be queued or written to the File Buffer
        /// </summary>
        bool Add(IEnumerable<TItem> items);
    }


    /// <summary>
    /// Base class for a Buffer that queues Items in memory and processes them in batches on an interval using the ProcessFunction,
    /// retrying on failure. Optionally overflows to a "Store and Forward" File Buffer when the Working Queue is full or the Buffer is stopped
    /// </summary>
    public abstract class TrakHoundOperationBuffer<TItem> : ITrakHoundOperationBuffer<TItem>
    {
        private const int _metricsWindowsSize = 10000; // 10 Seconds
        private const int _metricsInterval = 1000; // 1 Second
        private const int _stopTimeout = 5000; // 5 Seconds
        private const int _availabilityCacheDuration = 5000; // 5 Seconds

        private const int DefaultMaxItemsPerInterval = 5000;
        private const int DefaultQueuedItemLimit = DefaultMaxItemsPerInterval * 5;
        private const int DefaultProcessInterval = 5000;
        private const int DefaultRetryInterval = 10000;
        private const bool DefaultFileBufferEnabled = false;
        private const bool DefaultFileBufferForceEnabled = false;
        private const int DefaultFileBufferReadInterval = 100;
        private const int DefaultFileBufferPageSize = 5000000; // 5 MB
        private const int DefaultFileBufferWriteInterval = 100;

        private readonly ITrakHoundLogger _logger;
        private readonly TrakHoundBufferMetrics _metrics;
        private readonly MovingAverage _queueItemStatistics;
        private readonly MovingAverage _fileBufferItemReadStatistics;
        private readonly MovingAverage _fileBufferByteReadStatistics;
        private readonly MovingAverage _fileBufferItemWriteStatistics;
        private readonly MovingAverage _fileBufferByteWriteStatistics;
        private readonly System.Timers.Timer _metricsTimer;
        private readonly object _lock = new object();

        private readonly OperationItemQueue<TItem> _workingQueue;
        private readonly int _maxItemsPerInterval;
        private readonly int _queuedItemLimit;
        private readonly int _processInterval;
        private readonly int _processRetryInterval;
        private readonly bool _fileBufferEnabled;
        private readonly bool _fileBufferForceEnabled;
        private readonly int _fileBufferReadInterval;
        private readonly int _fileBufferWriteInterval;
        private readonly int _fileBufferPageLimit;

        private readonly string _id;
        private CancellationTokenSource _stop;
        private Task _workerTask;
        private bool _isStarted;
        private bool _disposed;

        private TrakHoundOperationReadBuffer<TItem> _readBuffer;
        private TrakHoundOperationWriteBuffer<TItem> _writeBuffer;
        private OperationFileBuffer<TItem> _fileBuffer;

        // The latest Availability check (shared by the Process Worker and the Read Buffer)
        private readonly object _availabilityLock = new object();
        private Task<bool> _availabilityCheck;
        private long _availabilityCheckTime;

        private long _queueTotalItemCount;
        private long _lastQueueTotalItemCount;
        private long _lastFileBufferTotalReadItemCount;
        private long _lastFileBufferTotalReadByteCount;
        private long _lastFileBufferTotalWriteItemCount;
        private long _lastFileBufferTotalWriteByteCount;


        /// <summary>
        /// Gets the Unique Identifier for the Buffer
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// The runtime Metrics for the Buffer (updated every second)
        /// </summary>
        public TrakHoundBufferMetrics Metrics => _metrics;

        /// <summary>
        /// Returns whether the target Service is available. Items are only processed (or read from the File Buffer) while it returns true.
        /// If null, the Service is always considered available
        /// </summary>
        protected Func<Task<bool>> AvailabilityFunction { get; set; }

        /// <summary>
        /// Processes a batch of Items. Returns true on success (the Items are removed), false to retry after the ProcessRetryInterval.
        /// Must be set before Start is called
        /// </summary>
        protected Func<ReadOnlyMemory<TItem>, Task<bool>> ProcessFunction { get; set; }

        /// <summary>
        /// Deserializes an Item read from the File Buffer (required, with SerializeFunction, for the File Buffer to be used)
        /// </summary>
        protected Func<ReadOnlyMemory<byte>, TItem> DeserializeFunction { get; set; }

        /// <summary>
        /// Serializes an Item to the Stream for the File Buffer and returns the number of bytes written (0 on failure)
        /// </summary>
        protected Func<Stream, TItem, int> SerializeFunction { get; set; }

        /// <summary>
        /// The interval at which the queue is processed
        /// </summary>
        public int ProcessInterval => _processInterval;

        /// <summary>
        /// The interval at which the queue is processed after a failure
        /// </summary>
        public int ProcessRetryInterval => _processRetryInterval;

        /// <summary>
        /// The maximum number of items to read from the queue at a time
        /// </summary>
        public int MaxItemsPerInterval => _maxItemsPerInterval;

        /// <summary>
        /// The maximum number of items to keep in the Queue at any point in time
        /// </summary>
        public int QueuedItemLimit => _queuedItemLimit;

        /// <summary>
        /// Gets the number of Items that are currently in the Queue
        /// </summary>
        public long QueuedItemCount => _workingQueue.Count;

        /// <summary>
        /// Determines whether the "Store and Forward" File Buffer is enabled
        /// </summary>
        public bool FileBufferEnabled => _fileBufferEnabled;

        /// <summary>
        /// Determines whether new Items are forced to the File Buffer instead of the Working Queue
        /// </summary>
        public bool FileBufferForceEnabled => _fileBufferForceEnabled;

        /// <summary>
        /// The size (in bytes) of the Maximum Page Size to use for the File Buffer
        /// </summary>
        public int FileBufferPageSize => _fileBufferPageLimit;

        /// <summary>
        /// The Interval at which the File Buffer is read
        /// </summary>
        public int FileBufferReadInterval => _fileBufferReadInterval;

        /// <summary>
        /// The Interval at which the File Buffer is written
        /// </summary>
        public int FileBufferWriteInterval => _fileBufferWriteInterval;


        /// <summary>
        /// Raised for each Item after its batch is successfully processed
        /// </summary>
        public event TrakHoundOperationEventHandler<TItem> ItemProcessed;


        /// <summary>
        /// Creates a new Buffer (using the defaults for any values not set in the Buffer Configuration) and registers it in the TrakHoundBufferRegistry
        /// </summary>
        public TrakHoundOperationBuffer(
            string bufferId,
            string driverId,
            ITrakHoundLogger logger,
            string name = null,
            string operationType = null,
            TrakHoundBufferConfiguration bufferConfiguration = null
            )
        {
            _id = bufferId;

            _logger = logger;

            // Set configuration properties to defaults
            var processInterval = DefaultProcessInterval;
            var processRetryInterval = DefaultRetryInterval;
            _maxItemsPerInterval = DefaultMaxItemsPerInterval;
            _queuedItemLimit = DefaultQueuedItemLimit;

            _fileBufferEnabled = DefaultFileBufferEnabled;
            _fileBufferForceEnabled = DefaultFileBufferForceEnabled;
            _fileBufferPageLimit = DefaultFileBufferPageSize;
            _fileBufferReadInterval = DefaultFileBufferReadInterval;
            _fileBufferWriteInterval = DefaultFileBufferWriteInterval;

            // Set configuration properties based on BufferConfiguration
            if (bufferConfiguration != null)
            {
                if (bufferConfiguration.Interval >= 0) processInterval = bufferConfiguration.Interval;
                if (bufferConfiguration.RetryInterval >= 0) processRetryInterval = bufferConfiguration.RetryInterval;
                if (bufferConfiguration.MaxItemsPerInterval > 0) _maxItemsPerInterval = bufferConfiguration.MaxItemsPerInterval;
                if (bufferConfiguration.QueuedItemLimit > 0) _queuedItemLimit = bufferConfiguration.QueuedItemLimit;

                _fileBufferEnabled = bufferConfiguration.FileBufferEnabled;
                _fileBufferForceEnabled = bufferConfiguration.FileBufferForceEnabled;
                if (bufferConfiguration.FileBufferPageSize > 0) _fileBufferPageLimit = bufferConfiguration.FileBufferPageSize;
                if (bufferConfiguration.FileBufferReadInterval > 0) _fileBufferReadInterval = bufferConfiguration.FileBufferReadInterval;
                if (bufferConfiguration.FileBufferWriteInterval > 0) _fileBufferWriteInterval = bufferConfiguration.FileBufferWriteInterval;
            }

            // Intervals must be at least 1ms (an Interval of 0 would spin the Worker)
            _processInterval = Math.Max(1, processInterval);
            _processRetryInterval = Math.Max(1, processRetryInterval);

            // Initialize Queues
            _workingQueue = new OperationItemQueue<TItem>(_queuedItemLimit);

            _queueItemStatistics = new MovingAverage(_metricsWindowsSize / _metricsInterval);
            _fileBufferItemReadStatistics = new MovingAverage(_metricsWindowsSize / _metricsInterval);
            _fileBufferByteReadStatistics = new MovingAverage(_metricsWindowsSize / _metricsInterval);
            _fileBufferItemWriteStatistics = new MovingAverage(_metricsWindowsSize / _metricsInterval);
            _fileBufferByteWriteStatistics = new MovingAverage(_metricsWindowsSize / _metricsInterval);

            _metrics = new TrakHoundBufferMetrics();
            _metrics.BufferId = _id;
            _metrics.DriverId = driverId;
            _metrics.Name = name;
            _metrics.OperationType = operationType;
            _metrics.Queue.ItemLimit = _queuedItemLimit;

            _metricsTimer = new System.Timers.Timer();
            _metricsTimer.Interval = _metricsInterval;
            _metricsTimer.Elapsed += MetricsTimerElapsed;
            _metricsTimer.Start();

            // Subscribe to ProcessExit (to Flush Queue on process exit)
            AppDomain.CurrentDomain.ProcessExit += OnExit;

            // Make Metrics available (ex. via the Instance HTTP API)
            TrakHoundBufferRegistry.Register(this);
        }

        private void OnExit(object sender, EventArgs args)
        {
            Stop();
        }


        /// <summary>
        /// Starts processing the Working Queue (and starts the File Buffer if enabled). Does nothing if already started, disposed, or the ProcessFunction is not set
        /// </summary>
        protected void Start()
        {
            lock (_lock)
            {
                if (_isStarted || _disposed || ProcessFunction == null) return;

                var now = UnixDateTime.Now;

                _metrics.StartTime = now;
                _metrics.LastUpdated = now;

                _isStarted = true;

                _logger.Log(TrakHoundLogLevel.Trace, $"Buffer {Id} : Starting..");

                _stop = new CancellationTokenSource();

                var cancellationToken = _stop.Token;
                _workerTask = Task.Run(() => ProcessWorker(cancellationToken));

                if (_fileBufferEnabled && DeserializeFunction != null && SerializeFunction != null)
                {
                    _fileBuffer = new OperationFileBuffer<TItem>(Id, DeserializeFunction, SerializeFunction, _fileBufferPageLimit);

                    _metrics.FileBuffer.IsEnabled = true;
                    _metrics.FileBuffer.PageSize = _fileBufferPageLimit;

                    _readBuffer = new TrakHoundOperationReadBuffer<TItem>(Id, _fileBufferReadInterval, _maxItemsPerInterval, _queuedItemLimit, _fileBuffer, GetQueueCount, IsServiceAvailable, HasPendingWrites, _logger, _metrics);
                    _readBuffer.ItemsRead += BufferItemsRead;
                    _readBuffer.Start();

                    _writeBuffer = new TrakHoundOperationWriteBuffer<TItem>(Id, _fileBufferWriteInterval, _maxItemsPerInterval, _queuedItemLimit, _fileBufferPageLimit, _fileBuffer, _logger, _metrics);
                    _writeBuffer.Start();
                }

                _logger.Log(TrakHoundLogLevel.Trace, $"Buffer {Id} : Started");
            }
        }

        /// <summary>
        /// Stops processing and, if the File Buffer is enabled, flushes the Working Queue to the File Buffer. Also called on process exit
        /// </summary>
        protected void Stop()
        {
            lock (_lock)
            {
                if (!_isStarted) return;

                var now = UnixDateTime.Now;

                _logger.Log(TrakHoundLogLevel.Trace, $"Buffer {Id} : Stopping..");

                _stop?.Cancel();

                // Wait for the Worker to finish any in-flight batch before flushing,
                // otherwise the same Items could be both processed and written to the File Buffer
                if (_workerTask != null)
                {
                    try
                    {
                        if (!_workerTask.Wait(_stopTimeout))
                        {
                            _logger.Log(TrakHoundLogLevel.Warning, $"Buffer {Id} : Worker did not stop within {_stopTimeout}ms");
                        }
                    }
                    catch (AggregateException) { }
                }

                // Flush the File Buffer
                if (_fileBuffer != null)
                {
                    _logger.Log(TrakHoundLogLevel.Trace, $"Buffer {Id} : Flushing Queue to File Buffer..");

                    _readBuffer.Stop();
                    _readBuffer.ItemsRead -= BufferItemsRead;

                    FlushBuffer();

                    _writeBuffer.Stop();

                    _fileBuffer.Dispose();
                }

                _stop?.Dispose();
                _stop = null;
                _workerTask = null;

                _logger.Log(TrakHoundLogLevel.Trace, $"Buffer {Id} : Stopped");

                _isStarted = false;
                _metrics.StopTime = now;
                _metrics.LastUpdated = now;
            }
        }

        /// <summary>
        /// Unregisters the Buffer, stops it (flushing to the File Buffer if enabled) and releases its resources
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            AppDomain.CurrentDomain.ProcessExit -= OnExit;
            TrakHoundBufferRegistry.Unregister(this);

            // Stop (and Flush) before disposing the Queue
            Stop();

            _metricsTimer.Elapsed -= MetricsTimerElapsed;
            _metricsTimer.Dispose();

            _workingQueue.Dispose();

            GC.SuppressFinalize(this);
        }


        private void MetricsTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            // Queue Metrics
            _metrics.Queue.ItemCount = _workingQueue.Count;
            _metrics.Queue.TotalItemCount = Interlocked.Read(ref _queueTotalItemCount);
            _metrics.Queue.ItemRate = UpdateRate(_queueItemStatistics, _metrics.Queue.TotalItemCount, ref _lastQueueTotalItemCount);

            // FileBuffer Metrics
            _metrics.FileBuffer.ItemReadRate = UpdateRate(_fileBufferItemReadStatistics, _metrics.FileBuffer.TotalReadCount, ref _lastFileBufferTotalReadItemCount);
            _metrics.FileBuffer.ByteReadRate = UpdateRate(_fileBufferByteReadStatistics, _metrics.FileBuffer.TotalBytesRead, ref _lastFileBufferTotalReadByteCount);
            _metrics.FileBuffer.ItemWriteRate = UpdateRate(_fileBufferItemWriteStatistics, _metrics.FileBuffer.TotalWriteCount, ref _lastFileBufferTotalWriteItemCount);
            _metrics.FileBuffer.ByteWriteRate = UpdateRate(_fileBufferByteWriteStatistics, _metrics.FileBuffer.TotalBytesWritten, ref _lastFileBufferTotalWriteByteCount);
        }

        private static double UpdateRate(MovingAverage statistics, long total, ref long lastTotal)
        {
            var diff = total - lastTotal;
            lastTotal = total;
            statistics.Push(diff);
            return Math.Round(Math.Max(0, statistics.Mean), 4);
        }

        private void OnQueueItemsAdded(int count)
        {
            if (count > 0)
            {
                Interlocked.Add(ref _queueTotalItemCount, count);
                _metrics.Queue.ItemCount = _workingQueue.Count;
                _metrics.Queue.LastUpdated = UnixDateTime.Now;
            }
        }


        /// <summary>
        /// Adds an Item to the Working Queue, or to the File Buffer if the Working Queue is full, the File Buffer is being read, or File Buffer Force is enabled.
        /// Returns false if the Item could not be added
        /// </summary>
        public bool Add(TItem item)
        {
            if (_readBuffer == null || (!_readBuffer.Active && !_fileBufferForceEnabled))
            {
                // Add to Working Queue
                if (_workingQueue.Add(item))
                {
                    OnQueueItemsAdded(1);
                    return true;
                }

                // Working Queue is full, overflow to File Buffer
                if (_fileBuffer != null)
                {
                    _metrics.FileBuffer.LastUpdated = UnixDateTime.Now;
                    return BufferWrite(item);
                }

                return false;
            }
            else
            {
                return BufferWrite(item);
            }
        }

        /// <summary>
        /// Adds Items to the Working Queue, overflowing to the File Buffer as for Add(TItem).
        /// Returns false if the Items are empty or any of them could not be added
        /// </summary>
        public bool Add(IEnumerable<TItem> items)
        {
            if (items.IsNullOrEmpty()) return false;

            var itemList = items as IReadOnlyList<TItem> ?? items.ToArray();

            if (_readBuffer == null || (!_readBuffer.Active && !_fileBufferForceEnabled))
            {
                // Add as many Items as will fit to the Working Queue (single lock)
                var added = _workingQueue.AddRange(itemList);
                OnQueueItemsAdded(added);

                if (added >= itemList.Count) return true;

                // Working Queue is full, overflow remaining Items to File Buffer
                if (_fileBuffer != null)
                {
                    _metrics.FileBuffer.LastUpdated = UnixDateTime.Now;
                    return BufferWrite(itemList.Skip(added));
                }

                return false;
            }
            else
            {
                return BufferWrite(itemList);
            }
        }


        private void BufferItemsRead(object sender, TItem[] items)
        {
            var added = _workingQueue.AddRange(items);
            OnQueueItemsAdded(added);

            if (added < items.Length)
            {
                // Working Queue filled up after the Read Buffer checked its capacity.
                // The page file has already been deleted, so write the remaining Items back
                var remaining = items.Length - added;
                _logger.Log(TrakHoundLogLevel.Debug, $"Buffer {Id} : Working Queue Full : {remaining} Items Returned to File Buffer");

                BufferWrite(new ArraySegment<TItem>(items, added, remaining));
            }
        }

        private int GetQueueCount()
        {
            return _workingQueue.Count;
        }

        private bool HasPendingWrites()
        {
            return _writeBuffer != null && _writeBuffer.QueuedItemCount > 0;
        }

        /// <summary>
        /// Whether the Service is Available (using the AvailabilityFunction). The result is shared for a short duration
        /// so that the Process Worker and the Read Buffer don't each call the Service on every interval
        /// </summary>
        private Task<bool> IsServiceAvailable()
        {
            var availabilityFunction = AvailabilityFunction;
            if (availabilityFunction == null) return Task.FromResult(true);

            lock (_availabilityLock)
            {
                // Reuse a check that is in progress, or one that completed recently (a failed check is not reused)
                var check = _availabilityCheck;
                if (check != null && (!check.IsCompleted || (check.IsCompletedSuccessfully && Environment.TickCount64 - _availabilityCheckTime < _availabilityCacheDuration)))
                {
                    return check;
                }

                _availabilityCheckTime = Environment.TickCount64;
                _availabilityCheck = RunAvailabilityFunction(availabilityFunction);
                return _availabilityCheck;
            }
        }

        private static async Task<bool> RunAvailabilityFunction(Func<Task<bool>> availabilityFunction)
        {
            // Return to the caller first, so that the Function (which may block before its first await) runs outside the lock
            await Task.Yield();

            return await availabilityFunction();
        }


        private async Task ProcessWorker(CancellationToken cancellationToken)
        {
            var processFunction = ProcessFunction;
            var clearOnReturn = RuntimeHelpers.IsReferenceOrContainsReferences<TItem>();
            var workerStopwatch = new System.Diagnostics.Stopwatch();
            var processStopwatch = new System.Diagnostics.Stopwatch();

            TItem[] itemBuffer = null;

            try
            {
                string itemLabel = "";
                int count = 0;
                int totalCount;

                do
                {
                    var now = UnixDateTime.Now;

                    int interval = _processInterval;
                    workerStopwatch.Restart();

                    try
                    {
                        // Check if service is "Available" (only when there are Items to process, so an idle Buffer doesn't call the Service)
                        var idle = count < 1 && _workingQueue.Count < 1;
                        if (idle || await IsServiceAvailable())
                        {
                            totalCount = _workingQueue.Count;

                            // Get (n) Items from working queue (if not previously failed)
                            if (count < 1 && !idle)
                            {
                                if (itemBuffer == null) itemBuffer = ArrayPool<TItem>.Shared.Rent(_maxItemsPerInterval);

                                count = _workingQueue.Get(ref itemBuffer, 0, _maxItemsPerInterval);

                                if (count > 0)
                                {
                                    itemLabel = count > 1 ? "Items" : "Item";
                                    _logger.Log(TrakHoundLogLevel.Trace, $"Buffer {Id} : {count} / {totalCount} Items Read From Queue");
                                }
                            }

                            if (count > 0)
                            {
                                _metrics.Queue.IsActive = true;

                                processStopwatch.Restart();

                                // Process the Items from Working Queue
                                var response = await processFunction(new ReadOnlyMemory<TItem>(itemBuffer, 0, count));

                                processStopwatch.Stop();
                                var duration = processStopwatch.Elapsed.TotalMilliseconds;

                                if (response)
                                {
                                    // Remove Items from Working Queue (before raising events, so a failing
                                    // event handler can't cause the batch to be processed again)
                                    _workingQueue.Remove(count);

                                    _metrics.Queue.ItemCount = _workingQueue.Count;
                                    _metrics.Queue.LastUpdated = now;

                                    RaiseItemProcessed(itemBuffer, count);

                                    // Reset the Count (to read new items from Working Queue)
                                    count = 0;
                                }
                                else
                                {
                                    _logger.Log(TrakHoundLogLevel.Debug, $"Buffer {Id} : Error Processing {count} {itemLabel} in {duration}ms");

                                    interval = _processRetryInterval;
                                }
                            }
                            else
                            {
                                _metrics.Queue.IsActive = false;

                                // Return Item Buffer while idle
                                if (itemBuffer != null)
                                {
                                    ArrayPool<TItem>.Shared.Return(itemBuffer, clearOnReturn);
                                    itemBuffer = null;
                                }
                            }
                        }
                        else
                        {
                            _metrics.Queue.IsActive = false;
                        }
                    }
                    catch (TaskCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.Log(TrakHoundLogLevel.Error, $"Buffer {Id} : Worker Error : {ex.Message}");

                        interval = _processRetryInterval;
                    }

                    workerStopwatch.Stop();

                    if (workerStopwatch.ElapsedMilliseconds < interval)
                    {
                        var waitInterval = interval - (int)workerStopwatch.ElapsedMilliseconds;

                        await Task.Delay(waitInterval, cancellationToken);
                    }

                } while (!cancellationToken.IsCancellationRequested);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                _logger.Log(TrakHoundLogLevel.Error, $"Buffer {Id} : Worker Loop Error : {ex.Message}");
            }
            finally
            {
                if (itemBuffer != null) ArrayPool<TItem>.Shared.Return(itemBuffer, clearOnReturn);
            }

            _logger.Log(TrakHoundLogLevel.Trace, $"Buffer {Id} Stopped");
        }

        private void RaiseItemProcessed(TItem[] items, int count)
        {
            var handler = ItemProcessed;
            if (handler == null) return;

            for (var i = 0; i < count; i++)
            {
                try
                {
                    handler.Invoke(items[i]);
                }
                catch (Exception ex)
                {
                    _logger.Log(TrakHoundLogLevel.Error, $"Buffer {Id} : ItemProcessed Handler Error : {ex.Message}");
                }
            }
        }


        private bool BufferWrite(TItem item)
        {
            return _writeBuffer != null && _writeBuffer.Add(item);
        }

        private bool BufferWrite(IEnumerable<TItem> items)
        {
            return _writeBuffer != null && _writeBuffer.Add(items);
        }

        private void FlushBuffer()
        {
            if (_writeBuffer != null)
            {
                var queueCount = _workingQueue.Count;
                if (queueCount > 0)
                {
                    var itemBuffer = new TItem[queueCount];
                    var count = _workingQueue.Take(ref itemBuffer, 0, queueCount);
                    if (count > 0)
                    {
                        // Working Queue Items are older than anything in the File Buffer, so write them to Recovery Pages (read first)
                        _writeBuffer.WriteRecovery(itemBuffer, count);
                    }

                    _metrics.Queue.ItemCount = _workingQueue.Count;
                }
            }
        }
    }
}
