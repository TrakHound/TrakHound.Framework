// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using TrakHound.Instances;

namespace TrakHound.Instance.Boots
{
    /// <summary>
    /// Times a sequence of steps (each Begin ends the previous step)
    /// </summary>
    public class TrakHoundBootStepTimer
    {
        private readonly List<TrakHoundInstanceBootStep> _steps = new List<TrakHoundInstanceBootStep>();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private string _name;
        private DateTime _started;


        public List<TrakHoundInstanceBootStep> Steps => _steps;


        public void Begin(string name)
        {
            End();

            _name = name;
            _started = DateTime.UtcNow;
            _stopwatch.Restart();
        }

        /// <summary>
        /// Ends the current step (no effect if there isn't one). An Error marks the step as the one that failed
        /// </summary>
        public void End(string error = null)
        {
            if (_name == null) return;

            _stopwatch.Stop();
            _steps.Add(new TrakHoundInstanceBootStep
            {
                Name = _name,
                Started = _started,
                DurationMs = Math.Round(_stopwatch.Elapsed.TotalMilliseconds, 1),
                Error = error
            });

            _name = null;
        }
    }
}
