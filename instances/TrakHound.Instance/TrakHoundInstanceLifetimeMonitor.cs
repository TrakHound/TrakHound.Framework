// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;
using System.Diagnostics;
using System.Runtime.InteropServices;
using TrakHound.Instance.Boots;
using TrakHound.Instances;

namespace TrakHound.Instance
{
    /// <summary>
    /// Logs when and why the Instance process Stops or Crashes.
    /// A Run Marker file is kept while the process is running so that terminations that can't be logged
    /// (ex. killed, power loss, Stack Overflow) are reported the next time the Instance Starts.
    /// Stop and Crash events are logged as Warning (or Fatal) so that they are written to the Log Files by the default nlog.config.
    /// Each run is also recorded in the Boot History (when it Started, how long each step took, and how it ended).
    /// </summary>
    internal static class TrakHoundInstanceLifetimeMonitor
    {
        /// <summary>
        /// A previous run of the Instance that didn't Shut Down Cleanly (read from the Run Marker it left behind)
        /// </summary>
        public class PreviousRunReport
        {
            public string ProcessId { get; init; }
            public string Started { get; init; }
            public DateTime LastModified { get; init; }
            public string Reason { get; init; }

            /// <summary>
            /// The full Exception (with Stack Trace) if the previous run Crashed with an Unhandled Exception
            /// </summary>
            public string Details { get; init; }

            /// <summary>
            /// Windows Event Log entries about the previous run's process (ex. a Stack Overflow, or IIS terminating the Worker Process)
            /// </summary>
            public IReadOnlyList<TrakHoundWindowsEventLog.Entry> Events { get; init; }

            /// <summary>
            /// The end of the previous process's IIS stdout log (when stdoutLogEnabled is set in web.config). A Stack Overflow is only written here
            /// </summary>
            public string Output { get; init; }

            public string OutputPath { get; init; }
        }


        private const string _runMarkerFilename = "instance.running";
        private static readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);

        private static readonly object _lock = new object();
        private static readonly List<PosixSignalRegistration> _signalRegistrations = new List<PosixSignalRegistration>();

        private static Logger _logger;
        private static string _runMarkerPath;
        private static Timer _heartbeatTimer;
        private static DateTime _startTime;
        private static string _shutdownReason;
        private static bool _shutdownComplete;
        private static bool _initialized;

        private static TrakHoundBootHistory _bootHistory;
        private static TrakHoundInstanceBoot _boot;


        public static string ShutdownReason => _shutdownReason;

        /// <summary>
        /// Set if the previous run of the Instance didn't Shut Down Cleanly (null otherwise)
        /// </summary>
        public static PreviousRunReport PreviousRun { get; private set; }

        /// <summary>
        /// The recent Boots of the Instance (null if there is no Configuration Profile to keep them in)
        /// </summary>
        public static TrakHoundBootHistory BootHistory => _bootHistory;

        /// <summary>
        /// The ID of this process's Boot
        /// </summary>
        public static string BootId => _boot?.Id;


        public static void Initialize(Logger logger, string markerDirectory)
        {
            lock (_lock)
            {
                if (_initialized) return;
                _initialized = true;
            }

            _logger = logger;
            _startTime = DateTime.Now;

            var process = Process.GetCurrentProcess();
            _logger?.Info($"Instance Process Started : PID = {process.Id} : User = {Environment.UserName} : Host = {Environment.MachineName} : OS = {RuntimeInformation.OSDescription} : Runtime = {RuntimeInformation.FrameworkDescription}");

            // Check for (and replace) the Run Marker left by a previous run that didn't shut down cleanly
            if (!string.IsNullOrEmpty(markerDirectory))
            {
                _runMarkerPath = Path.Combine(markerDirectory, _runMarkerFilename);
                CheckPreviousRun();
                WriteRunMarker(null, null);

                _bootHistory = new TrakHoundBootHistory(Path.Combine(markerDirectory, TrakHoundBootHistory.Filename));
                StartBoot(process);

                // Touch the Run Marker periodically so its Last Modified time shows when an unclean run was last alive
                _heartbeatTimer = new Timer(_ => TouchRunMarker(), null, _heartbeatInterval, _heartbeatInterval);
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            // Record Termination Signals (Ctrl+C, Ctrl+Break, SIGTERM, Console Close, Logoff, System Shutdown)
            // Registrations don't Cancel the Signal so the Host's normal handling continues
            RegisterSignal(PosixSignal.SIGINT, "SIGINT (Ctrl+C)");
            RegisterSignal(PosixSignal.SIGQUIT, "SIGQUIT (Ctrl+Break)");
            RegisterSignal(PosixSignal.SIGTERM, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "SIGTERM (Console Closed, User Logoff or System Shutdown)" : "SIGTERM (Termination Requested)");
            RegisterSignal(PosixSignal.SIGHUP, "SIGHUP (Terminal Closed)");
        }

        /// <summary>
        /// Called from the Host's ApplicationStopping notification (runs synchronously on the thread that requested the Stop)
        /// </summary>
        public static void OnApplicationStopping()
        {
            var reason = _shutdownReason ?? GetStopRequestSource(new StackTrace(1, false));
            SetReason(reason);

            _logger?.Warn($"Instance Shutdown Requested : {reason}");
            Flush();
        }

        /// <summary>
        /// Called when the Instance fails to Start or Run (the exception is logged by the caller), with the Boot steps and Log entries so far
        /// </summary>
        public static void OnRunException(Exception exception, List<TrakHoundInstanceBootStep> steps = null, TrakHoundInstanceBootLog log = null)
        {
            var reason = $"Exception : {exception?.GetType().Name} : {exception?.Message}";
            SetReason(reason);

            UpdateBoot(boot =>
            {
                // Before Ready, the process couldn't Start. After, the Exception ended it
                boot.Status = boot.Ready == null ? TrakHoundInstanceBootStatus.Failed : TrakHoundInstanceBootStatus.Crashed;
                boot.Reason = reason;
                boot.Details = exception?.ToString();
                if (steps != null) boot.Steps = steps;
                if (log != null) boot.Log = log;
            });
        }

        /// <summary>
        /// Called once the HTTP Server and the first Runtime Start have finished
        /// </summary>
        public static void OnBootReady(List<TrakHoundInstanceBootStep> steps, TrakHoundInstanceBootLog log)
        {
            UpdateBoot(boot =>
            {
                boot.Status = TrakHoundInstanceBootStatus.Running;
                boot.Ready = DateTime.UtcNow;
                boot.Steps = steps;
                boot.Log = log;
            });

            var duration = _boot?.BootDuration;
            if (duration != null) _logger?.Info($"Instance Boot Complete : {FormatUptime(duration.Value)}");
        }

        /// <summary>
        /// Called each time the Instance Runtime finishes Starting (or fails to)
        /// </summary>
        public static void OnRuntimeStart(TrakHoundInstanceRuntimeStart start)
        {
            if (start == null) return;

            UpdateBoot(boot =>
            {
                boot.Starts ??= new List<TrakHoundInstanceRuntimeStart>();
                boot.Starts.Add(start);
            });
        }

        /// <summary>
        /// Called each time the Instance Runtime is Stopped
        /// </summary>
        public static void OnRuntimeStopped()
        {
            UpdateBoot(boot =>
            {
                var start = boot.Starts?.LastOrDefault();
                if (start != null && start.Stopped == null) start.Stopped = DateTime.UtcNow;
            });
        }

        /// <summary>
        /// Called once the Instance has been Stopped and the process is about to exit normally
        /// </summary>
        public static void OnShutdownComplete()
        {
            var reason = _shutdownReason ?? "Host Stopped (Unknown Reason)";
            var uptime = DateTime.Now - _startTime;

            _logger?.Warn($"Instance Shutdown Complete : Reason = {reason} : Uptime = {FormatUptime(uptime)} : Exit Code = {Environment.ExitCode}");

            EndBoot(reason);

            lock (_lock) _shutdownComplete = true;
            DeleteRunMarker();

            foreach (var registration in _signalRegistrations)
            {
                try { registration.Dispose(); } catch { }
            }
        }


        private static void RegisterSignal(PosixSignal signal, string description)
        {
            try
            {
                _signalRegistrations.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    SetReason($"Signal Received : {description}");

                    _logger?.Warn($"Shutdown Signal Received : {description}");
                    Flush();
                }));
            }
            catch
            {
                // Signal not supported on this Platform
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            // An exception rethrown after Shutdown already Completed (ex. Startup failure) has already been logged
            lock (_lock) { if (_shutdownComplete) return; }

            var exception = args.ExceptionObject as Exception;
            var reason = $"Crashed : Unhandled Exception : {exception?.GetType().Name ?? args.ExceptionObject?.GetType().Name} : {exception?.Message}";

            lock (_lock) _shutdownReason = reason;

            if (exception != null) _logger?.Fatal(exception, $"Instance Crashed : Unhandled Exception (IsTerminating = {args.IsTerminating}) : Uptime = {FormatUptime(DateTime.Now - _startTime)}");
            else _logger?.Fatal($"Instance Crashed : Unhandled Exception (IsTerminating = {args.IsTerminating}) : {args.ExceptionObject} : Uptime = {FormatUptime(DateTime.Now - _startTime)}");

            // Record in the Run Marker so that the crash is also reported on the next Start
            WriteRunMarker(reason, exception?.ToString() ?? args.ExceptionObject?.ToString());

            UpdateBoot(boot =>
            {
                boot.Status = TrakHoundInstanceBootStatus.Crashed;
                boot.Ended = DateTime.UtcNow;
                boot.Reason = reason;
                boot.Details = exception?.ToString() ?? args.ExceptionObject?.ToString();
            });

            Flush();
        }

        private static void OnProcessExit(object sender, EventArgs args)
        {
            bool shutdownComplete;
            lock (_lock) shutdownComplete = _shutdownComplete;

            if (!shutdownComplete)
            {
                // The process is exiting without going through the normal Host Shutdown (ex. Environment.Exit() called)
                var reason = _shutdownReason ?? $"Process Exit requested by {GetStopRequestSource(new StackTrace(1, false))}";

                _logger?.Warn($"Instance Process Exiting before Shutdown Completed : Reason = {reason} : Uptime = {FormatUptime(DateTime.Now - _startTime)} : Exit Code = {Environment.ExitCode}");

                EndBoot(reason);
                DeleteRunMarker();
                Flush();
            }
        }

        private static void SetReason(string reason)
        {
            lock (_lock)
            {
                if (_shutdownReason == null) _shutdownReason = reason;
            }
        }

        /// <summary>
        /// Determine what requested the Stop from the calling Stack
        /// </summary>
        private static string GetStopRequestSource(StackTrace stackTrace)
        {
            var frames = stackTrace?.GetFrames();
            if (frames != null)
            {
                string firstCaller = null;

                foreach (var frame in frames)
                {
                    var method = frame.GetMethod();
                    var typeName = method?.DeclaringType?.FullName;
                    if (typeName == null) continue;

                    if (typeName.Contains("WindowsServiceLifetime"))
                    {
                        if (method.Name == "OnShutdown") return "System Shutdown (Windows Service)";
                        return "Windows Service Stop requested (Service Control Manager)";
                    }
                    if (typeName.Contains("ConsoleLifetime")) return "Console Termination Signal";
                    if (typeName.Contains("Microsoft.AspNetCore.Server.IIS")) return "IIS Application Shutdown";
                    if (typeName == "Microsoft.Extensions.Hosting.Internal.Host" || typeName.StartsWith("Microsoft.Extensions.Hosting.Internal.Host+")) return "Host Stopped (ex. a Background Service Failed)";

                    if (firstCaller == null &&
                        !typeName.StartsWith("System.") &&
                        !typeName.StartsWith("Microsoft.") &&
                        !typeName.StartsWith(typeof(TrakHoundInstanceLifetimeMonitor).FullName))
                    {
                        firstCaller = $"{typeName}.{method.Name}()";
                    }
                }

                if (firstCaller != null) return $"Stop requested by {firstCaller}";
            }

            return "Unknown";
        }

        private static void CheckPreviousRun()
        {
            try
            {
                if (File.Exists(_runMarkerPath))
                {
                    var lines = File.ReadAllLines(_runMarkerPath);
                    var pid = lines.Length > 0 ? lines[0] : null;
                    var started = lines.Length > 1 ? lines[1] : null;
                    var reason = lines.Length > 2 && !string.IsNullOrEmpty(lines[2]) ? lines[2] : null;
                    var details = lines.Length > 3 ? string.Join(Environment.NewLine, lines.Skip(3)) : null;
                    var lastModified = File.GetLastWriteTime(_runMarkerPath);

                    // IIS starts the new Worker Process before the old one has stopped (Overlapped Recycle), so the Run Marker can belong to a process that is still running
                    if (IsProcessRunning(pid, started))
                    {
                        _logger?.Info($"Previous Instance Process is still Running (ex. an IIS Overlapped Recycle) : PID = {pid} : Started = {started}");
                        return;
                    }

                    var events = ReadEventLog(pid, lastModified);
                    var output = ReadStdoutLog(pid, out var outputPath);

                    reason ??= GetEventLogCause(events);
                    if (reason == null && output != null && output.Contains("Stack overflow.")) reason = "Crashed : Stack Overflow (see the stdout log)";
                    reason ??= "Unknown (Process Killed, Power Loss, or Fatal Runtime Error)";

                    PreviousRun = new PreviousRunReport { ProcessId = pid, Started = started, LastModified = lastModified, Reason = reason, Details = details, Events = events, Output = output, OutputPath = outputPath };

                    _logger?.Warn($"Previous Instance Run did not Shut Down Cleanly : PID = {pid} : Started = {started} : Last Modified = {lastModified:yyyy-MM-dd HH:mm:ss} : Reason = {reason}");

                    if (events != null)
                    {
                        foreach (var entry in events)
                        {
                            _logger?.Warn($"Previous Instance Run : Windows Event Log : {entry.Timestamp:yyyy-MM-dd HH:mm:ss} : {entry.Log} : {entry.Source} (Event {entry.EventId}, {entry.Level}){Environment.NewLine}{entry.Message}");
                        }
                    }

                    if (output != null)
                    {
                        _logger?.Warn($"Previous Instance Run : stdout log ({outputPath}){Environment.NewLine}{output}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Error Reading Run Marker ({_runMarkerPath}) : {ex.Message}");
            }
        }

        private static bool IsProcessRunning(string pid, string started)
        {
            if (!int.TryParse(pid, out var processId)) return false;
            if (!DateTime.TryParse(started, out var startTime)) return false;

            return IsProcessRunning(processId, startTime);
        }

        /// <param name="startTime">When the process Started (local time)</param>
        private static bool IsProcessRunning(int processId, DateTime startTime)
        {
            if (processId == Environment.ProcessId) return false;

            try
            {
                using var process = Process.GetProcessById(processId);

                // The Start Time guards against the PID having been reused by another process
                return !process.HasExited && (process.StartTime - startTime).Duration() < TimeSpan.FromMinutes(5);
            }
            catch
            {
                return false; // Not running (or not accessible)
            }
        }

        /// <summary>
        /// Read the Windows Event Log entries about the previous process (ex. a Stack Overflow or Access Violation, which can't be logged by the process itself)
        /// </summary>
        private static List<TrakHoundWindowsEventLog.Entry> ReadEventLog(string pid, DateTime lastAlive)
        {
            if (!OperatingSystem.IsWindows() || !int.TryParse(pid, out var processId)) return null;

            try
            {
                var events = TrakHoundWindowsEventLog.Read(processId, lastAlive, lastAlive - TimeSpan.FromMinutes(1), DateTime.Now, out var errors);

                foreach (var error in errors)
                {
                    _logger?.Warn($"Previous Instance Run : Windows Event Log could not be read : {error}");
                }
                if (events.Count < 1) _logger?.Info("Previous Instance Run : No Windows Event Log entries found for the previous process");

                return events;
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Previous Instance Run : Windows Event Log could not be read : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Read the end of the previous process's IIS stdout log. The ASP.NET Core Module names the file with the process ID (ex. stdout_20261028040240_1234.log)
        /// </summary>
        private static string ReadStdoutLog(string pid, out string path)
        {
            const int maxLength = 64 * 1024;
            path = null;

            if (!int.TryParse(pid, out _)) return null;

            try
            {
                var prefix = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, GetStdoutLogFile()));
                var directory = Path.GetDirectoryName(prefix);
                if (!Directory.Exists(directory)) return null;

                path = Directory.GetFiles(directory, $"{Path.GetFileName(prefix)}_*_{pid}.log").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
                if (path == null) return null;

                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > maxLength) stream.Seek(-maxLength, SeekOrigin.End);

                using var reader = new StreamReader(stream);
                var output = reader.ReadToEnd().Trim();
                return output.Length > 0 ? output : null;
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Error Reading the stdout log for the previous run : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// The stdoutLogFile set in web.config (relative to the Instance directory)
        /// </summary>
        private static string GetStdoutLogFile()
        {
            const string defaultFile = @"logs\stdout";

            try
            {
                var webConfigPath = Path.Combine(AppContext.BaseDirectory, "web.config");
                if (!File.Exists(webConfigPath)) return defaultFile;

                var aspNetCore = System.Xml.Linq.XDocument.Load(webConfigPath).Descendants("aspNetCore").FirstOrDefault();
                var file = aspNetCore?.Attribute("stdoutLogFile")?.Value;
                return !string.IsNullOrEmpty(file) ? file : defaultFile;
            }
            catch
            {
                return defaultFile;
            }
        }

        private static string GetEventLogCause(List<TrakHoundWindowsEventLog.Entry> events)
        {
            if (!OperatingSystem.IsWindows()) return null;

            var cause = TrakHoundWindowsEventLog.GetCause(events);
            return cause != null ? $"{cause.Source} (Event {cause.EventId}) : {cause.Summary}" : null;
        }

        private static void WriteRunMarker(string reason, string details)
        {
            if (string.IsNullOrEmpty(_runMarkerPath)) return;

            try
            {
                var directory = Path.GetDirectoryName(_runMarkerPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                // Line 1 : PID, Line 2 : Started, Line 3 : Reason, the rest : Details (ex. the Exception with its Stack Trace)
                var lines = new List<string>
                {
                    Environment.ProcessId.ToString(),
                    _startTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    reason?.ReplaceLineEndings(" ") ?? string.Empty
                };
                if (!string.IsNullOrEmpty(details)) lines.Add(details);

                File.WriteAllLines(_runMarkerPath, lines);
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Error Writing Run Marker ({_runMarkerPath}) : {ex.Message}");
            }
        }

        /// <summary>
        /// The Run Marker is replaced by the next process when IIS overlaps a Recycle, so only this process's Run Marker is touched or deleted
        /// </summary>
        private static bool IsOwnRunMarker()
        {
            try
            {
                if (!File.Exists(_runMarkerPath)) return false;

                using var reader = new StreamReader(_runMarkerPath);
                return reader.ReadLine() == Environment.ProcessId.ToString();
            }
            catch
            {
                return false;
            }
        }

        private static void TouchRunMarker()
        {
            lock (_lock) { if (_shutdownComplete) return; }

            try
            {
                if (IsOwnRunMarker()) File.SetLastWriteTime(_runMarkerPath, DateTime.Now);
            }
            catch { }
        }

        private static void DeleteRunMarker()
        {
            if (string.IsNullOrEmpty(_runMarkerPath)) return;

            try { _heartbeatTimer?.Dispose(); } catch { }

            try
            {
                if (IsOwnRunMarker()) File.Delete(_runMarkerPath);
            }
            catch { }
        }

        /// <summary>
        /// Adds this process's Boot, and marks the Boots of earlier processes that never recorded how they ended as Unclean
        /// </summary>
        private static void StartBoot(Process process)
        {
            DateTime started;
            try { started = process.StartTime.ToUniversalTime(); }
            catch { started = _startTime.ToUniversalTime(); }

            var boot = new TrakHoundInstanceBoot
            {
                Id = $"{started:yyyyMMddHHmmssfff}-{process.Id}",
                Status = TrakHoundInstanceBootStatus.Starting,
                ProcessId = process.Id,
                Version = GetVersion(),
                MachineName = Environment.MachineName,
                User = Environment.UserName,
                OperatingSystem = RuntimeInformation.OSDescription,
                Runtime = RuntimeInformation.FrameworkDescription,
                Started = started
            };

            lock (_lock)
            {
                // Kept in memory even if it can't be written, so later changes still try to Save it
                _boot = boot;

                try
                {
                    _bootHistory.Update(boots =>
                    {
                        foreach (var previous in boots) MarkUnclean(previous);
                        boots.Add(boot);
                    });
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"Error Writing Boot History ({_bootHistory.Path}) : {ex.Message}");
                }
            }
        }

        private static void MarkUnclean(TrakHoundInstanceBoot boot)
        {
            if (boot.Status != TrakHoundInstanceBootStatus.Starting && boot.Status != TrakHoundInstanceBootStatus.Running) return;
            if (boot.ProcessId == Environment.ProcessId) return;

            var previousRun = PreviousRun;
            if (previousRun != null && previousRun.ProcessId == boot.ProcessId.ToString())
            {
                boot.Status = TrakHoundInstanceBootStatus.Unclean;
                boot.Ended = previousRun.LastModified.ToUniversalTime();
                boot.Reason = previousRun.Reason;
                boot.Details ??= previousRun.Details;
            }
            else if (!IsProcessRunning(boot.ProcessId, boot.Started.ToLocalTime()))
            {
                // No Run Marker was left to say when it ended (ex. it was deleted, or another process replaced it)
                boot.Status = TrakHoundInstanceBootStatus.Unclean;
                boot.Reason ??= "Unknown (Process Killed, Power Loss, or Fatal Runtime Error)";
            }
            // Otherwise it is still running (ex. an IIS Overlapped Recycle) and records its own end
        }

        private static void EndBoot(string reason)
        {
            UpdateBoot(boot =>
            {
                boot.Ended ??= DateTime.UtcNow;
                boot.Reason ??= reason;
                boot.ExitCode = Environment.ExitCode;

                if (boot.Status == TrakHoundInstanceBootStatus.Starting || boot.Status == TrakHoundInstanceBootStatus.Running)
                {
                    boot.Status = boot.Ready != null ? TrakHoundInstanceBootStatus.Stopped : TrakHoundInstanceBootStatus.Failed;
                }

                // A Runtime that wasn't Stopped ended with the process
                var start = boot.Starts?.LastOrDefault();
                if (start != null && start.Success && start.Stopped == null) start.Stopped = boot.Ended;
            });
        }

        /// <summary>
        /// Changes this process's Boot and Saves it to the Boot History
        /// </summary>
        private static void UpdateBoot(Action<TrakHoundInstanceBoot> update)
        {
            if (_bootHistory == null) return;

            try
            {
                lock (_lock)
                {
                    if (_boot == null) return;

                    update(_boot);
                    _bootHistory.Save(_boot);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Error Writing Boot History ({_bootHistory.Path}) : {ex.Message}");
            }
        }

        private static string GetVersion()
        {
            try { return FileVersionInfo.GetVersionInfo(typeof(TrakHoundInstanceLifetimeMonitor).Assembly.Location).ProductVersion; }
            catch { return null; }
        }

        private static void Flush()
        {
            try { LogManager.Flush(TimeSpan.FromSeconds(2)); } catch { }
        }

        private static string FormatUptime(TimeSpan uptime)
        {
            if (uptime.TotalDays >= 1) return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
            if (uptime.TotalHours >= 1) return $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
            if (uptime.TotalMinutes >= 1) return $"{uptime.Minutes}m {uptime.Seconds}s";
            return $"{uptime.TotalSeconds:0.0}s";
        }
    }
}
