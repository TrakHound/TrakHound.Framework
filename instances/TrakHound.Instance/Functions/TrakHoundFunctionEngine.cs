// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Logging;

namespace TrakHound.Functions
{
    public class TrakHoundFunctionEngine : IDisposable
    {
        private const string _engineLogName = "Engine";
        private const int _maxFunctionLogs = 10000; // Maximum number of Function Log Items to retain in the Response
        private const int _cancelledStatusCode = 499;

        private static readonly ITrakHoundLogger _logger = new TrakHoundLogger<TrakHoundFunctionEngine>();

        private readonly string _runId;
        private readonly string _engineId;
        private readonly string _packageId;
        private readonly string _packageVersion;
        private readonly ITrakHoundFunction _function;
        private readonly ITrakHoundClient _client;
        private readonly IReadOnlyDictionary<string, string> _parameters;
        private readonly string _parametersError; // Why the Parameters don't fit the Function's described Parameters (the Run is refused)
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly TrakHoundCpuMonitor _cpuMonitor; // Null when CPU Monitoring is disabled
        private int _disposed;


        public string RunId => _runId;

        public string EngineId => _engineId;

        public string PackageId => _packageId;

        public string PackageVersion => _packageVersion;


        public ITrakHoundFunction Function => _function;

        public ITrakHoundClient Client => _client;

        /// <summary>
        /// The CPU time consumed by the Function's code (including in-process API and Service calls it makes) during Run
        /// </summary>
        public TimeSpan TotalProcessorTime => _cpuMonitor?.TotalProcessorTime ?? TimeSpan.Zero;


        internal event FunctionStatusDelegate StatusReceived;

        internal event FunctionLogDelegate LogReceived;


        /// <param name="parentCpuMonitor">The Function's Monitor in TrakHoundModuleCpuMonitors (so the Run's time is included in the Instance's CPU by Module)</param>
        /// <param name="parameterDefinitions">The Function's described Parameters (FunctionParameter attributes): their Defaults are applied and the Run is refused when they don't fit</param>
        public TrakHoundFunctionEngine(ITrakHoundFunction function, IReadOnlyDictionary<string, string> parameters, string packageId, string packageVersion, string runId, bool monitorCpu = true, TrakHoundCpuMonitor parentCpuMonitor = null, IReadOnlyList<TrakHoundFunctionParameterInformation> parameterDefinitions = null)
        {
            if (monitorCpu) _cpuMonitor = new TrakHoundCpuMonitor(parentCpuMonitor);

            _function = function;
            _runId = !string.IsNullOrEmpty(runId) ? runId : Guid.NewGuid().ToString();
            _packageId = packageId;
            _packageVersion = packageVersion;
            _parameters = parameters ?? new Dictionary<string, string>();

            if (parameterDefinitions != null && parameterDefinitions.Count > 0)
            {
                _parameters = TrakHoundFunctionParameterInformation.ApplyDefaults(parameterDefinitions, _parameters);
                _parametersError = TrakHoundFunctionParameterInformation.Validate(parameterDefinitions, _parameters);
            }

            if (function != null)
            {
                _engineId = function.Id;
                _client = function.Client;

                function.LogReceived += FunctionLogReceived;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            _cancellationTokenSource.Cancel();

            if (_function != null)
            {
                _function.LogReceived -= FunctionLogReceived;
                _function.OnLogReceived = null;

                try
                {
                    _function.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Function Dispose Error : {_packageId} : {_runId} : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
                }
            }

            _cancellationTokenSource.Dispose();
        }


        /// <summary>
        /// Request that the Function stop. Functions observe this through the CancellationToken passed to Run.
        /// </summary>
        public void Cancel()
        {
            if (Volatile.Read(ref _disposed) == 0)
            {
                _cancellationTokenSource.Cancel();
            }
        }

        public async Task<TrakHoundFunctionResponse> Run(long timestamp = 0)
        {
            TrakHoundFunctionResponse response;
            var runLogs = new RunLogCollection(_maxFunctionLogs);

            long started = timestamp > 0 ? timestamp : UnixDateTime.Now;
            long completed;

            if (_function == null)
            {
                completed = UnixDateTime.Now;
                response = new TrakHoundFunctionResponse(500, "Function Not Found");
                AddEngineLog(runLogs, TrakHoundLogLevel.Error, "Function Not Found", completed);
            }
            else if (_client == null)
            {
                completed = UnixDateTime.Now;
                response = new TrakHoundFunctionResponse(503, "Client Not Found");
                NotifyStatus(TrakHoundFunctionStatusType.Error, completed);
                AddEngineLog(runLogs, TrakHoundLogLevel.Error, "Client Not Found", completed);
            }
            else if (_parametersError != null)
            {
                // Refused before the Function's code runs (ex. a missing Required Parameter)
                completed = UnixDateTime.Now;
                response = new TrakHoundFunctionResponse(400, $"Invalid Parameters : {_parametersError}");
                NotifyStatus(TrakHoundFunctionStatusType.Error, completed);
                AddEngineLog(runLogs, TrakHoundLogLevel.Error, $"Invalid Parameters : {_parametersError}", completed);
            }
            else
            {
                _function.OnLogReceived = runLogs.AddFunctionLog;

                NotifyStatus(TrakHoundFunctionStatusType.Started, started);
                AddEngineLog(runLogs, TrakHoundLogLevel.Information, "Function Started", started);

                try
                {
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    // The Function's code (and anything it starts) is attributed to the CPU Monitor, the Engine's is not
                    response = _cpuMonitor != null
                        ? await _cpuMonitor.Run(() => _function.Run(_parameters, _cancellationTokenSource.Token))
                        : await _function.Run(_parameters, _cancellationTokenSource.Token);
                    completed = UnixDateTime.Now;

                    NotifyStatus(TrakHoundFunctionStatusType.Completed, completed);

                    var responseLogLevel = response.Success ? TrakHoundLogLevel.Information : TrakHoundLogLevel.Error;
                    AddEngineLog(runLogs, responseLogLevel, $"Response : {response.StatusCode} : {response.GetParameter("message")}", completed);
                    AddEngineLog(runLogs, TrakHoundLogLevel.Information, "Function Completed", completed);
                }
                catch (OperationCanceledException)
                {
                    completed = UnixDateTime.Now;
                    response = new TrakHoundFunctionResponse(_cancelledStatusCode, "Function Cancelled");

                    NotifyStatus(TrakHoundFunctionStatusType.Stopped, completed);
                    AddEngineLog(runLogs, TrakHoundLogLevel.Warning, "Function Cancelled", completed);
                }
                catch (Exception ex)
                {
                    completed = UnixDateTime.Now;
                    response = new TrakHoundFunctionResponse(500, ex.Message);

                    NotifyStatus(TrakHoundFunctionStatusType.Error, completed);
                    AddEngineLog(runLogs, TrakHoundLogLevel.Critical, TrakHoundLogger.GetExceptionStackTrace(ex), completed, ex.HResult.ToString());
                }
                finally
                {
                    _function.OnLogReceived = null;
                }
            }

            var droppedLogs = runLogs.DroppedCount;
            if (droppedLogs > 0)
            {
                AddEngineLog(runLogs, TrakHoundLogLevel.Warning, $"Function Logs Truncated : {droppedLogs} Log Items were not retained (Maximum = {_maxFunctionLogs})", completed);
            }

            response.Id = _runId;
            response.PackageId = _packageId;
            response.PackageVersion = _packageVersion;
            response.EngineId = _engineId;
            response.Started = started;
            response.Completed = completed;
            response.CpuTime = Math.Round(TotalProcessorTime.TotalMilliseconds);
            response.Logs = runLogs.ToList();

            return response;
        }


        private void AddEngineLog(RunLogCollection runLogs, TrakHoundLogLevel level, string message, long timestamp, string code = null)
        {
            var logItem = new TrakHoundLogItem(_engineLogName, level, message, code, timestamp);
            runLogs.AddEngineLog(logItem);
            NotifyLog(logItem);
        }

        private void NotifyStatus(TrakHoundFunctionStatusType statusType, long timestamp)
        {
            // Subscriber exceptions should not affect the result of the Function
            try
            {
                StatusReceived?.Invoke(_function, _runId, new TrakHoundStatusItem(statusType, timestamp));
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Function Status Handler Error : {_packageId} : {_runId} : {ex.Message}");
            }
        }

        private void NotifyLog(TrakHoundLogItem logItem)
        {
            // Subscriber exceptions should not affect the result of the Function
            try
            {
                LogReceived?.Invoke(_function, _runId, logItem);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Function Log Handler Error : {_packageId} : {_runId} : {ex.Message}");
            }
        }

        private void FunctionLogReceived(object sender, TrakHoundLogItem item)
        {
            NotifyLog(item);
        }


        /// <summary>
        /// Thread safe collection of the Log Items for a single Run. Functions may log from multiple threads.
        /// </summary>
        class RunLogCollection
        {
            private readonly List<TrakHoundLogItem> _items = new List<TrakHoundLogItem>();
            private readonly int _maxFunctionLogs;
            private readonly object _lock = new object();
            private int _functionLogCount;
            private int _droppedCount;


            public int DroppedCount
            {
                get { lock (_lock) return _droppedCount; }
            }


            public RunLogCollection(int maxFunctionLogs)
            {
                _maxFunctionLogs = maxFunctionLogs;
            }

            public void AddFunctionLog(TrakHoundLogItem item)
            {
                lock (_lock)
                {
                    if (_functionLogCount < _maxFunctionLogs)
                    {
                        _items.Add(item);
                        _functionLogCount++;
                    }
                    else
                    {
                        _droppedCount++;
                    }
                }
            }

            public void AddEngineLog(TrakHoundLogItem item)
            {
                lock (_lock) _items.Add(item);
            }

            public List<TrakHoundLogItem> ToList()
            {
                lock (_lock) return new List<TrakHoundLogItem>(_items);
            }
        }
    }
}
