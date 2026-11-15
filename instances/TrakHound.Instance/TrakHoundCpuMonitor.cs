// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TrakHound
{
    /// <summary>
    /// Records the CPU time consumed by code running under a tagged ExecutionContext.
    /// Code started after Enter() is called (including awaits, Task.Run, Timers, and new Threads) is attributed to this monitor.
    /// </summary>
    /// <remarks>
    /// CPU time is measured when a thread enters and leaves the tagged context. Time spent in a synchronous
    /// section that never leaves the context (ex. a dedicated Thread running a blocking loop) is not recorded until it does.
    /// </remarks>
    public class TrakHoundCpuMonitor
    {
        private const int _defaultSampleInterval = 1000;

        private static readonly AsyncLocal<TrakHoundCpuMonitor> _current = new AsyncLocal<TrakHoundCpuMonitor>(ContextChanged);
        private static readonly bool _threadTimeSupported = CheckThreadTimeSupported();

        // The Thread time when the current thread entered a monitored context. A separate flag is used since a new
        // thread's time can be exactly zero (and would otherwise look like the thread was never entered)
        [ThreadStatic]
        private static long _enteredThreadTime;

        [ThreadStatic]
        private static bool _entered;

        private readonly object _lock = new object();
        private readonly TrakHoundCpuMonitor _parent;
        private long _totalTicks;
        private long _lastSampleTicks;
        private long _lastSampleTimestamp = Stopwatch.GetTimestamp();
        private double _usage;


        /// <summary>
        /// Creates a Monitor. CPU time recorded by it is also added to the Parent (and its Parents), so a Module's time is included in its Module Type's total.
        /// </summary>
        public TrakHoundCpuMonitor(TrakHoundCpuMonitor parent = null)
        {
            _parent = parent;
        }


        /// <summary>
        /// The Monitor this Monitor's CPU time is also added to (null if none)
        /// </summary>
        public TrakHoundCpuMonitor Parent => _parent;

        /// <summary>
        /// The total CPU time consumed
        /// </summary>
        public TimeSpan TotalProcessorTime => TimeSpan.FromTicks(Interlocked.Read(ref _totalTicks));

        /// <summary>
        /// The CPU usage (percent of total machine capacity, 0 - 100) calculated at the last Sample()
        /// </summary>
        public double Usage => _usage;

        /// <summary>
        /// Whether per-thread CPU time is supported on this platform. If not, the time spent holding a thread is recorded instead.
        /// </summary>
        public static bool ThreadTimeSupported => _threadTimeSupported;


        /// <summary>
        /// Attribute the current ExecutionContext (and anything that flows from it) to this monitor.
        /// Prefer <see cref="Run(Func{Task})"/>, which stops attributing once the function returns.
        /// </summary>
        public void Enter()
        {
            _current.Value = this;
        }

        // Enter() is called inside these async methods, so the context flows to the function but is restored
        // (and the caller is no longer attributed) once they return

        /// <summary>
        /// Run the function attributed to this monitor. The caller's code is not attributed once the function returns.
        /// </summary>
        public async Task Run(Func<Task> function)
        {
            Enter();
            await function();
        }

        /// <summary>
        /// Run the function attributed to this monitor. The caller's code is not attributed once the function returns.
        /// </summary>
        public async Task<T> Run<T>(Func<Task<T>> function)
        {
            Enter();
            return await function();
        }

        /// <summary>
        /// Recalculate Usage if at least the specified interval has elapsed since the last Sample
        /// </summary>
        /// <returns>True if Usage was recalculated</returns>
        public bool Sample(int minimumInterval = _defaultSampleInterval)
        {
            lock (_lock)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsed = Stopwatch.GetElapsedTime(_lastSampleTimestamp, now);
                if (elapsed.TotalMilliseconds >= minimumInterval)
                {
                    var totalTicks = Interlocked.Read(ref _totalTicks);
                    var cpuTicks = totalTicks - _lastSampleTicks;

                    _usage = Math.Clamp(cpuTicks / (double)(elapsed.Ticks * Environment.ProcessorCount) * 100, 0, 100);
                    _lastSampleTicks = totalTicks;
                    _lastSampleTimestamp = now;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reset the Usage to zero (ex. when the monitored item is stopped)
        /// </summary>
        public void ResetUsage()
        {
            lock (_lock)
            {
                _usage = 0;
                _lastSampleTicks = Interlocked.Read(ref _totalTicks);
                _lastSampleTimestamp = Stopwatch.GetTimestamp();
            }
        }


        // NOTE: This is called by the runtime on every ExecutionContext switch and must never throw
        private static void ContextChanged(AsyncLocalValueChangedArgs<TrakHoundCpuMonitor> args)
        {
            if (args.PreviousValue == args.CurrentValue) return;

            var now = GetThreadTime();

            if (args.PreviousValue != null && _entered && now > _enteredThreadTime)
            {
                var ticks = now - _enteredThreadTime;
                for (var monitor = args.PreviousValue; monitor != null; monitor = monitor._parent)
                {
                    Interlocked.Add(ref monitor._totalTicks, ticks);
                }
            }

            _entered = args.CurrentValue != null;
            _enteredThreadTime = _entered ? now : 0;
        }


        #region "Thread Time"

        // Returns the CPU time (in TimeSpan ticks) for the current thread
        private static long GetThreadTime()
        {
            if (_threadTimeSupported)
            {
                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        if (GetThreadTimes(GetCurrentThread(), out _, out _, out var kernelTime, out var userTime))
                        {
                            return kernelTime + userTime; // FILETIME is in 100ns units (same as TimeSpan ticks)
                        }
                    }
                    else
                    {
                        if (clock_gettime(OperatingSystem.IsLinux() ? CLOCK_THREAD_CPUTIME_ID_LINUX : CLOCK_THREAD_CPUTIME_ID_MACOS, out var ts) == 0)
                        {
                            return ((long)ts.tv_sec * TimeSpan.TicksPerSecond) + ((long)ts.tv_nsec / 100);
                        }
                    }
                }
                catch { }
            }

            // Fallback to Wall Time
            return Stopwatch.GetElapsedTime(0).Ticks;
        }

        private static bool CheckThreadTimeSupported()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    return GetThreadTimes(GetCurrentThread(), out _, out _, out _, out _);
                }
                else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    return clock_gettime(OperatingSystem.IsLinux() ? CLOCK_THREAD_CPUTIME_ID_LINUX : CLOCK_THREAD_CPUTIME_ID_MACOS, out _) == 0;
                }
            }
            catch { }

            return false;
        }

        private const int CLOCK_THREAD_CPUTIME_ID_LINUX = 3;
        private const int CLOCK_THREAD_CPUTIME_ID_MACOS = 16;

        [StructLayout(LayoutKind.Sequential)]
        private struct Timespec
        {
            public nint tv_sec;
            public nint tv_nsec;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetThreadTimes(IntPtr hThread, out long lpCreationTime, out long lpExitTime, out long lpKernelTime, out long lpUserTime);

        [DllImport("libc", SetLastError = true)]
        private static extern int clock_gettime(int clockId, out Timespec tp);

        #endregion
    }
}
