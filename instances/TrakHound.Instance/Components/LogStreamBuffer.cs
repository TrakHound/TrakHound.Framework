// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using TrakHound.Logging;

namespace TrakHound.Instance.Components
{
    /// <summary>
    /// The lines received from a Log Stream, kept apart from the LogStream component that shows them so that they
    /// (and the subscription) outlive it (ex. while LogView shows its History tab, the Live tab's LogStream isn't rendered)
    /// </summary>
    public class LogStreamBuffer : IDisposable
    {
        public const int DefaultMaxLines = 1000;

        private readonly object _lock = new object();
        private CircularBuffer<LogRow> _buffer = new CircularBuffer<LogRow>(DefaultMaxLines);
        private long _sequence; // Each received line gets a unique Key (the same TrakHoundLogItem can be received more than once)
        private ITrakHoundStream<IEnumerable<TrakHoundLogItem>> _consumer;
        private string _previousConsumerId;


        /// <summary>
        /// Raised when lines are added or cleared (from the Stream's thread)
        /// </summary>
        public event EventHandler Updated;

        /// <summary>
        /// Raised when the lines are cleared because a different Stream was attached
        /// </summary>
        public event EventHandler Cleared;


        /// <summary>
        /// The lines kept, newest first
        /// </summary>
        public LogRow[] GetRows()
        {
            lock (_lock) return _buffer.Get();
        }

        public void SetMaxLines(int maxLines)
        {
            if (maxLines < 1) maxLines = DefaultMaxLines;

            lock (_lock)
            {
                if (maxLines == _buffer.Limit) return;

                // Keep the newest lines that fit in the new buffer (Get() returns newest first)
                var existing = _buffer.Get();
                _buffer = new CircularBuffer<LogRow>(maxLines);
                for (var i = Math.Min(existing.Length, maxLines) - 1; i >= 0; i--) _buffer.Add(existing[i]);
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Receive lines from this Stream (null keeps the existing lines, ex. paused). A different Stream clears the lines
        /// </summary>
        public void SetConsumer(ITrakHoundStream<IEnumerable<TrakHoundLogItem>> consumer)
        {
            var cleared = false;

            lock (_lock)
            {
                if (ReferenceEquals(consumer, _consumer)) return;

                // Always detach from the previous Stream so two streams are never merged
                Detach();

                if (consumer != null)
                {
                    if (consumer.Id != _previousConsumerId)
                    {
                        _buffer.Clear();
                        cleared = true;
                    }

                    _previousConsumerId = consumer.Id;
                    _consumer = consumer;
                    _consumer.Received += LogReceived;
                    _consumer.Disposed += ConsumerDisposed;

                    // Start after the handlers are attached so no lines are missed
                    _consumer.Start();
                }
            }

            if (cleared)
            {
                Cleared?.Invoke(this, EventArgs.Empty);
                Updated?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            lock (_lock) Detach();
        }


        // Must be called while holding _lock
        private void Detach()
        {
            if (_consumer != null)
            {
                _consumer.Received -= LogReceived;
                _consumer.Disposed -= ConsumerDisposed;
                _consumer = null;
            }
        }

        // Only the current Stream is ever attached, so these events always come from it.
        // (Don't compare 'sender' : chained Streams forward the sender of their input Stream)
        private void ConsumerDisposed(object sender, string id)
        {
            lock (_lock) Detach();
        }

        private void LogReceived(object sender, IEnumerable<TrakHoundLogItem> items)
        {
            if (items == null) return;

            lock (_lock)
            {
                foreach (var item in items) _buffer.Add(LogRow.Create(item, ++_sequence));
            }

            Updated?.Invoke(this, EventArgs.Empty);
        }
    }
}
