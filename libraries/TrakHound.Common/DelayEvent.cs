// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound
{
    /// <summary>
    /// Raises an Elapsed event after a period of inactivity. Each call to Set restarts the delay (debounce)
    /// </summary>
    public class DelayEvent : IDisposable
    {
        private readonly System.Timers.Timer _timer;
        private readonly object _lock = new object();
        private bool _disposed;


        /// <summary>
        /// Raised once the Interval has passed since the last call to Set
        /// </summary>
        public event EventHandler Elapsed;


        /// <summary>
        /// Creates a new DelayEvent with the delay Interval in milliseconds (must be greater than zero)
        /// </summary>
        public DelayEvent(int interval = 1000)
        {
            if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be greater than zero");

            _timer = new System.Timers.Timer();
            _timer.Interval = interval;
            _timer.AutoReset = false;
            _timer.Elapsed += OnElapsed;
        }

        private void OnElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                lock (_lock)
                {
                    if (_disposed) return;

                    _timer.Stop();
                }

                Elapsed?.Invoke(this, e);
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Starts (or restarts) the delay timer
        /// </summary>
        public void Set()
        {
            try
            {
                lock (_lock)
                {
                    if (_disposed) return;

                    _timer.Stop();
                    _timer.Start();
                }
            }
            catch (ObjectDisposedException) { }
        }

        /// <summary>
        /// Stops and disposes the timer. Pending Elapsed events are not raised
        /// </summary>
        public void Dispose()
        {
            try
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    _disposed = true;

                    _timer.Stop();
                    _timer.Dispose();
                }
            }
            catch (ObjectDisposedException) { }
        }
    }
}
