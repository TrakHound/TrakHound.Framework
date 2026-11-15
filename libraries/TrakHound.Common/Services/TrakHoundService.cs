// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Logging;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Services
{
    /// <summary>
    /// Base class for Services. Provides the Configuration, Volume and Variables set by the Service Provider,
    /// Logging, and helpers to create Service Responses
    /// </summary>
    public abstract class TrakHoundService : ITrakHoundService
    {
        private readonly ITrakHoundServiceConfiguration _configuration;
        private ITrakHoundVolume _volume;
        private ITrakHoundVariableCollection _variables;


        private string _id;
        /// <summary>
        /// A unique Identifier for the Service instance (a hash of the Configuration ID and the Service type)
        /// </summary>
        public string Id
        {
            get
            {
                if (_id == null) _id = $"{Configuration?.Id}:{GetType().AssemblyQualifiedName}".ToMD5Hash();
                return _id;
            }
        }

        private string _name;
        /// <summary>
        /// The name of the Service (the name of the Service's class)
        /// </summary>
        public string Name
        {
            get
            {
                if (_name == null) _name = GetType().Name;
                return _name;
            }
        }

        /// <summary>
        /// The ID of the Instance that loaded the Service
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// The Configuration the Service was created from
        /// </summary>
        public ITrakHoundServiceConfiguration Configuration => _configuration;

        /// <summary>
        /// The Volume the Service can use to store files (null until SetVolume is called)
        /// </summary>
        public ITrakHoundVolume Volume => _volume;

        /// <summary>
        /// The Variables available to the Service (null until SetVariables is called)
        /// </summary>
        public ITrakHoundVariableCollection Variables => _variables;


        /// <summary>
        /// Raised when the Service writes a Log entry
        /// </summary>
        public event EventHandler<TrakHoundLogItem> LogReceived;

        /// <summary>
        /// Whether a Log Level is received by whatever handles the Service's Log (set by the host). Every Level is enabled when not set
        /// </summary>
        public Func<TrakHoundLogLevel, bool> LogLevelEnabled { get; set; }


        /// <summary>
        /// Creates a Service using the specified Configuration
        /// </summary>
        public TrakHoundService(ITrakHoundServiceConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Disposes the Service (calls OnDisposed)
        /// </summary>
        public void Dispose()
        {
            OnDisposed();
        }

        /// <summary>
        /// Called when the Service is disposed. Override to release resources
        /// </summary>
        protected virtual void OnDisposed() { }


        /// <summary>
        /// Sets the Volume used by the Service and calls AfterVolumeSet
        /// </summary>
        public void SetVolume(ITrakHoundVolume volume)
        {
            _volume = volume;
            AfterVolumeSet();
        }

        /// <summary>
        /// Sets the Variables used by the Service and calls AfterVariablesSet
        /// </summary>
        public void SetVariables(ITrakHoundVariableCollection variables)
        {
            _variables = variables;
            AfterVariablesSet();
        }

        /// <summary>
        /// Called after the Volume is set
        /// </summary>
        protected virtual void AfterVolumeSet() { }

        /// <summary>
        /// Called after the Variables are set
        /// </summary>
        protected virtual void AfterVariablesSet() { }


        /// <summary>
        /// Checks whether the infrastructure the Service connects to is currently available.
        /// Returns available by default (override to implement)
        /// </summary>
        public virtual Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            var response = new TrakHoundServiceAvailabilityResponse();
            response.IsAvailable = true;
            response.Message = "Availability Is Not Implemented for this Service";

            return Task.FromResult(TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.Ok(response));
        }


        /// <summary>
        /// Raises LogReceived with the specified Log Item
        /// </summary>
        /// <summary>
        /// Whether a Log entry at the Level would be received. Check it before building expensive Debug or Trace messages
        /// </summary>
        protected bool IsLogEnabled(TrakHoundLogLevel level)
        {
            if (LogReceived == null) return false;

            var logLevelEnabled = LogLevelEnabled;
            return logLevelEnabled == null || logLevelEnabled(level);
        }

        protected void Log(TrakHoundLogItem logItem)
        {
            if (LogReceived != null) LogReceived.Invoke(this, logItem);
        }

        /// <summary>
        /// Raises LogReceived with a new Log Item in the "Main" Log
        /// </summary>
        protected void Log(TrakHoundLogLevel level, string message, string code = null)
        {
            if (LogReceived != null) LogReceived.Invoke(this, new TrakHoundLogItem("Main", level, message, code, UnixDateTime.Now));
        }

        /// <summary>
        /// Raises LogReceived with a new Log Item in the specified Log
        /// </summary>
        protected void Log(string logName, TrakHoundLogLevel level, string message, string code = null)
        {
            if (LogReceived != null) LogReceived.Invoke(this, new TrakHoundLogItem(logName, level, message, code, UnixDateTime.Now));
        }


        /// <summary>
        /// Creates a successful Response (200) with the specified Result
        /// </summary>
        protected static TrakHoundServiceResponse<TResult> Ok<TResult>(TResult result)
        {
            return TrakHoundServiceResponse<TResult>.Ok(result);
        }

        /// <summary>
        /// Creates a successful Response (200)
        /// </summary>
        protected static TrakHoundServiceResponse Ok()
        {
            return TrakHoundServiceResponse.Ok();
        }

        /// <summary>
        /// Creates a Bad Request Response (400)
        /// </summary>
        protected static TrakHoundServiceResponse BadRequest(string message = null)
        {
            return TrakHoundServiceResponse.BadRequest(message);
        }

        /// <summary>
        /// Creates a Bad Request Response (400)
        /// </summary>
        protected static TrakHoundServiceResponse<TResult> BadRequest<TResult>(string message = null)
        {
            return TrakHoundServiceResponse<TResult>.BadRequest(message);
        }

        /// <summary>
        /// Creates a Not Found Response (404)
        /// </summary>
        protected static TrakHoundServiceResponse NotFound(string message = null)
        {
            return TrakHoundServiceResponse.NotFound(message);
        }

        /// <summary>
        /// Creates a Not Found Response (404)
        /// </summary>
        protected static TrakHoundServiceResponse<TResult> NotFound<TResult>(string message = null)
        {
            return TrakHoundServiceResponse<TResult>.NotFound(message);
        }

        /// <summary>
        /// Creates a Conflict Response (409)
        /// </summary>
        protected static TrakHoundServiceResponse Conflict(string message = null)
        {
            return TrakHoundServiceResponse.Conflict(message);
        }

        /// <summary>
        /// Creates a Conflict Response (409)
        /// </summary>
        protected static TrakHoundServiceResponse<TResult> Conflict<TResult>(string message = null)
        {
            return TrakHoundServiceResponse<TResult>.Conflict(message);
        }

        /// <summary>
        /// Creates a Route Not Configured Response (405)
        /// </summary>
        protected static TrakHoundServiceResponse RouteNotConfigured(string message = null)
        {
            return TrakHoundServiceResponse.RouteNotConfigured(message);
        }

        /// <summary>
        /// Creates a Route Not Configured Response (405)
        /// </summary>
        protected static TrakHoundServiceResponse<TResult> RouteNotConfigured<TResult>(string message = null)
        {
            return TrakHoundServiceResponse<TResult>.RouteNotConfigured(message);
        }

        /// <summary>
        /// Creates a Not Available Response (503)
        /// </summary>
        protected static TrakHoundServiceResponse NotAvailable(string message = null)
        {
            return TrakHoundServiceResponse.NotAvailable(message);
        }

        /// <summary>
        /// Creates a Not Available Response (503)
        /// </summary>
        protected static TrakHoundServiceResponse<TResult> NotAvailable<TResult>(string message = null)
        {
            return TrakHoundServiceResponse<TResult>.NotAvailable(message);
        }

        /// <summary>
        /// Creates an Internal Error Response (500)
        /// </summary>
        protected static TrakHoundServiceResponse InternalError(string message = null)
        {
            return TrakHoundServiceResponse.InternalError(message);
        }

        /// <summary>
        /// Creates an Internal Error Response (500)
        /// </summary>
        protected static TrakHoundServiceResponse<TResult> InternalError<TResult>(string message = null)
        {
            return TrakHoundServiceResponse<TResult>.InternalError(message);
        }

        /// <summary>
        /// Creates a Timeout Response (504)
        /// </summary>
        protected static TrakHoundServiceResponse Timeout(string message = null)
        {
            return TrakHoundServiceResponse.Timeout(message);
        }

        /// <summary>
        /// Creates a Timeout Response (504)
        /// </summary>
        protected static TrakHoundServiceResponse<TResult> Timeout<TResult>(string message = null)
        {
            return TrakHoundServiceResponse<TResult>.Timeout(message);
        }
    }
}
