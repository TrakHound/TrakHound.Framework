// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound
{
    /// <summary>
    /// Raises Elapsed immediately on the first Set(), then at most once per interval
    /// while Set() keeps being called. A Set() during the cooldown is coalesced into
    /// a single trailing Elapsed at the end of the interval.
    /// </summary>
    public class ThrottleEvent : IDisposable
    {
        private readonly System.Timers.Timer _timer;
        private readonly object _lock = new object();
        private bool _coolingDown;
        private bool _pending;
        private bool _disposed;


        /// <summary>
        /// Raised on the first Set, then at most once per interval while Set continues to be called
        /// </summary>
        public event EventHandler Elapsed;


        /// <summary>
        /// Creates a new ThrottleEvent with the minimum interval (in milliseconds) between Elapsed events
        /// </summary>
        public ThrottleEvent(int minimumInterval = 1000)
        {
            _timer = new System.Timers.Timer(minimumInterval);
            _timer.AutoReset = false;
            _timer.Elapsed += OnTimerElapsed;
        }

        /// <summary>
        /// Raises Elapsed immediately if not cooling down, otherwise schedules a single trailing Elapsed at the end of the interval
        /// </summary>
        public void Set()
        {
            lock (_lock)
            {
                if (_disposed) return;

                if (_coolingDown)
                {
                    _pending = true;
                    return;
                }

                _coolingDown = true;
                _timer.Start();
            }

            Elapsed?.Invoke(this, EventArgs.Empty);
        }

        private void OnTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_lock)
            {
                if (_disposed) return;

                if (!_pending)
                {
                    _coolingDown = false;
                    return;
                }

                _pending = false;
                _timer.Start();
            }

            Elapsed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Stops the timer and removes all Elapsed handlers
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _timer.Dispose();
            }

            Elapsed = null;
        }
    }
}
