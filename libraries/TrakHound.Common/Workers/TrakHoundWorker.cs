// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Packages;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Workers
{
    /// <summary>
    /// Base class for a Worker. Override OnStart / OnStartAsync, OnStop / OnStopAsync, and OnDispose to implement the Worker's behavior
    /// </summary>
    public abstract class TrakHoundWorker : ITrakHoundWorker, IAsyncDisposable
    {
        private readonly ITrakHoundWorkerConfiguration _configuration;
        private ITrakHoundClient _client;
        private ITrakHoundVolume _volume;
        private ITrakHoundVariableCollection _variables;
        private TrakHoundWorkerStatus _status;
        private int _started;
        private int _disposed;


        /// <inheritdoc/>
        public string Id { get; set; }

        /// <inheritdoc/>
        public string InstanceId { get; set; }

        /// <summary>
        /// The Package the Worker was loaded from
        /// </summary>
        public TrakHoundPackage Package { get; set; }

        /// <inheritdoc/>
        public ITrakHoundWorkerConfiguration Configuration => _configuration;

        /// <summary>
        /// Gets the value of a Configuration Variable shared by every configuration in the Profile (decrypted, with ${env:NAME} references replaced).
        /// Returns null if it isn't defined. Parameters that reference a Variable as ${name} are already replaced by GetParameter().
        /// </summary>
        public string GetConfigurationVariable(string name)
        {
            return TrakHound.Configurations.TrakHoundConfigurationVariables.GetValue(name);
        }

        /// <inheritdoc/>
        public ITrakHoundClient Client => _client;

        /// <inheritdoc/>
        public ITrakHoundVolume Volume => _volume;

        /// <summary>
        /// The Variables available to the Worker
        /// </summary>
        public ITrakHoundVariableCollection Variables => _variables;

        /// <inheritdoc/>
        public TrakHoundWorkerStatus Status => _status;


        /// <inheritdoc/>
        public event EventHandler<TrakHoundWorkerStatusType> StatusReceived;

        /// <inheritdoc/>
        public event EventHandler<TrakHoundLogItem> LogReceived;


        /// <summary>
        /// Creates a Worker from the Configuration (the Client, Volume, and Variables are set afterwards by the Worker Engine)
        /// </summary>
        public TrakHoundWorker(ITrakHoundWorkerConfiguration configuration)
        {
            _configuration = configuration;
        }

        // BACKWARDS COMPATIBLE : 6/15/2026
        /// <summary>
        /// Creates a Worker with the Configuration, Client, and Volume (kept for backwards compatibility)
        /// </summary>
        public TrakHoundWorker(ITrakHoundWorkerConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume)
        {
            _configuration = configuration;
            _client = client;
            _volume = volume;
        }


        /// <summary>
        /// Sets the Client and calls AfterClientSet
        /// </summary>
        public void SetClient(ITrakHoundClient client)
        {
            _client = client;
            AfterClientSet();
        }

        /// <summary>
        /// Sets the Volume and calls AfterVolumeSet
        /// </summary>
        public void SetVolume(ITrakHoundVolume volume)
        {
            _volume = volume;
            AfterVolumeSet();
        }

        /// <summary>
        /// Sets the Variables and calls AfterVariablesSet
        /// </summary>
        public void SetVariables(ITrakHoundVariableCollection variables)
        {
            _variables = variables;
            AfterVariablesSet();
        }

        /// <summary>
        /// Called after the Client is set
        /// </summary>
        protected virtual void AfterClientSet() { }

        /// <summary>
        /// Called after the Volume is set
        /// </summary>
        protected virtual void AfterVolumeSet() { }

        /// <summary>
        /// Called after the Variables are set
        /// </summary>
        protected virtual void AfterVariablesSet() { }


        /// <summary>
        /// Starts the Worker (calls OnStart and then OnStartAsync)
        /// </summary>
        public async Task Start()
        {
            // Mark as Started before running OnStart so that a partially started Worker is still Stopped
            Interlocked.Exchange(ref _started, 1);

            OnStart();
            await OnStartAsync();
        }

        /// <summary>
        /// Stop the Worker. Only runs OnStop/OnStopAsync once per Start (additional calls do nothing).
        /// </summary>
        public async Task Stop()
        {
            if (Interlocked.Exchange(ref _started, 0) == 0) return;

            OnStop();
            await OnStopAsync();
        }

        /// <summary>
        /// Stops the Worker (if it is still Started) and calls OnDispose. Only runs once
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            // Only Stop if still running (ex. the Engine has already Stopped the Worker)
            if (Volatile.Read(ref _started) == 1) Stop().GetAwaiter().GetResult();

            OnDispose();
        }

        /// <summary>
        /// Stops the Worker (if it is still Started) and calls OnDispose. Only runs once
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            await Stop();

            OnDispose();
        }


        /// <summary>
        /// Called when the Worker is Started (before OnStartAsync)
        /// </summary>
        protected virtual void OnStart() { }

        /// <summary>
        /// Called when the Worker is Started (after OnStart)
        /// </summary>
        protected virtual Task OnStartAsync() => Task.CompletedTask;

        /// <summary>
        /// Called when the Worker is Stopped (before OnStopAsync)
        /// </summary>
        protected virtual void OnStop() { }

        /// <summary>
        /// Called when the Worker is Stopped (after OnStop)
        /// </summary>
        protected virtual Task OnStopAsync() => Task.CompletedTask;

        /// <summary>
        /// Called when the Worker is Disposed (after it is Stopped)
        /// </summary>
        protected virtual void OnDispose() { }


        /// <summary>
        /// Report the Worker's Status. Reporting Error while running causes the Engine to Restart the Worker.
        /// </summary>
        protected void UpdateStatus(TrakHoundWorkerStatusType status, string message = null)
        {
            _status = new TrakHoundWorkerStatus(Id, status, message);
            StatusReceived?.Invoke(this, status);

            if (message != null) Log(status == TrakHoundWorkerStatusType.Error ? TrakHoundLogLevel.Error : TrakHoundLogLevel.Information, message);
        }


        /// <summary>
        /// Writes a Log Item to the "Main" Log (raises LogReceived)
        /// </summary>
        protected void Log(TrakHoundLogLevel level, string message, string code = null)
        {
            if (LogReceived != null) LogReceived.Invoke(this, new TrakHoundLogItem("Main", level, message, code, UnixDateTime.Now));
        }

        /// <summary>
        /// Writes a Log Item to the specified Log (raises LogReceived)
        /// </summary>
        protected void Log(string logName, TrakHoundLogLevel level, string message, string code = null)
        {
            if (LogReceived != null) LogReceived.Invoke(this, new TrakHoundLogItem(logName, level, message, code, UnixDateTime.Now));
        }
    }
}
