// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Packages;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Functions
{
    /// <summary>
    /// Base class for implementing a Function. Override OnRun to perform the work and return a Response using helpers such as Ok() or BadRequest()
    /// </summary>
    public abstract class TrakHoundFunction : ITrakHoundFunction
    {
        private readonly ITrakHoundFunctionConfiguration _configuration;
        private ITrakHoundClient _client;
        private ITrakHoundVolume _volume;
        private ITrakHoundVariableCollection _variables;


        /// <summary>
        /// The unique Identifier of the Function
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The Id of the Instance the Function is running in
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// The Package the Function was loaded from
        /// </summary>
        public TrakHoundPackage Package { get; set; }

        /// <summary>
        /// The Configuration the Function was created from
        /// </summary>
        public ITrakHoundFunctionConfiguration Configuration => _configuration;

        /// <summary>
        /// Gets the value of a Configuration Variable shared by every configuration in the Profile (decrypted, with ${env:NAME} references replaced).
        /// Returns null if it isn't defined. Parameters that reference a Variable as ${name} are already replaced by GetParameter().
        /// </summary>
        public string GetConfigurationVariable(string name)
        {
            return TrakHound.Configurations.TrakHoundConfigurationVariables.GetValue(name);
        }

        /// <summary>
        /// The Client used to access the Instance (set by the Instance through SetClient)
        /// </summary>
        public ITrakHoundClient Client => _client;

        /// <summary>
        /// The Volume used to store Files for the Function (set by the Instance through SetVolume)
        /// </summary>
        public ITrakHoundVolume Volume => _volume;

        /// <summary>
        /// The Variables available to the Function (set by the Instance through SetVariables)
        /// </summary>
        public ITrakHoundVariableCollection Variables => _variables;

        /// <summary>
        /// Callback invoked when the Function writes a Log Item
        /// </summary>
        public Action<TrakHoundLogItem> OnLogReceived { get; set; }


        /// <summary>
        /// Raised when the Function writes a Log Item
        /// </summary>
        public event EventHandler<TrakHoundLogItem> LogReceived;


        /// <summary>
        /// Create a new Function with the specified Configuration. The Client, Volume, and Variables are set afterwards by the Instance
        /// </summary>
        public TrakHoundFunction(ITrakHoundFunctionConfiguration configuration)
        {
            _configuration = configuration;
        }

        // BACKWARDS COMPATIBLE : 6/15/2026
        /// <summary>
        /// Create a new Function with the specified Configuration, Client, and Volume (kept for backwards compatibility)
        /// </summary>
        public TrakHoundFunction(ITrakHoundFunctionConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume)
        {
            _configuration = configuration;
            _client = client;
            _volume = volume;
        }


        /// <summary>
        /// Sets the Client used by the Function and calls AfterClientSet()
        /// </summary>
        public void SetClient(ITrakHoundClient client)
        {
            _client = client;
            AfterClientSet();
        }

        /// <summary>
        /// Sets the Volume used by the Function and calls AfterVolumeSet()
        /// </summary>
        public void SetVolume(ITrakHoundVolume volume)
        {
            _volume = volume;
            AfterVolumeSet();
        }

        /// <summary>
        /// Sets the Variables available to the Function and calls AfterVariablesSet()
        /// </summary>
        public void SetVariables(ITrakHoundVariableCollection variables)
        {
            _variables = variables;
            AfterVariablesSet();
        }

        /// <summary>
        /// Called after the Client has been set. Override to initialize anything that depends on the Client
        /// </summary>
        protected virtual void AfterClientSet() { }

        /// <summary>
        /// Called after the Volume has been set. Override to initialize anything that depends on the Volume
        /// </summary>
        protected virtual void AfterVolumeSet() { }

        /// <summary>
        /// Called after the Variables have been set. Override to initialize anything that depends on the Variables
        /// </summary>
        protected virtual void AfterVariablesSet() { }


        /// <summary>
        /// Run the Function with the specified Parameters (calls OnRun)
        /// </summary>
        public async Task<TrakHoundFunctionResponse> Run(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default)
        {
            return await OnRun(parameters, cancellationToken);
        }

        /// <summary>
        /// Run the Function. Override this overload to support cancellation: observe the cancellationToken
        /// (ex. pass it to async calls or call ThrowIfCancellationRequested) so the Run can be stopped.
        /// </summary>
        protected virtual Task<TrakHoundFunctionResponse> OnRun(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken) => OnRun(parameters);

        /// <summary>
        /// Run the Function. Override this overload when cancellation does not need to be supported (returns an empty Response by default)
        /// </summary>
        protected async virtual Task<TrakHoundFunctionResponse> OnRun(IReadOnlyDictionary<string, string> parameters) => new TrakHoundFunctionResponse();


        /// <summary>
        /// Dispose the Function (calls OnDispose)
        /// </summary>
        public void Dispose()
        {
            OnDispose();
        }

        /// <summary>
        /// Called when the Function is Disposed. Override to release any resources
        /// </summary>
        protected virtual void OnDispose() { }


        /// <summary>
        /// Write a Log Item to the "Main" Log
        /// </summary>
        protected void Log(TrakHoundLogLevel level, string message, string code = null)
        {
            var logItem = new TrakHoundLogItem("Main", level, message, code, UnixDateTime.Now);

            if (OnLogReceived != null) OnLogReceived(logItem);
            if (LogReceived != null) LogReceived.Invoke(this, logItem);
        }

        /// <summary>
        /// Write a Log Item to the specified Log
        /// </summary>
        protected void Log(string logName, TrakHoundLogLevel level, string message, string code = null)
        {
            var logItem = new TrakHoundLogItem(logName, level, message, code, UnixDateTime.Now);

            if (OnLogReceived != null) OnLogReceived(logItem);
            if (LogReceived != null) LogReceived.Invoke(this, logItem);
        }


        /// <summary>
        /// Create a 200 (OK) Response
        /// </summary>
        public static TrakHoundFunctionResponse Ok() => new TrakHoundFunctionResponse(200);
        /// <summary>
        /// Create a 200 (OK) Response with the specified Message
        /// </summary>
        public static TrakHoundFunctionResponse Ok(string message) => new TrakHoundFunctionResponse(200, message);
        /// <summary>
        /// Create a 200 (OK) Response with the properties of the specified Object added as Parameters
        /// </summary>
        public static TrakHoundFunctionResponse Ok(object response) => new TrakHoundFunctionResponse(200, response);
        /// <summary>
        /// Create a 200 (OK) Response with the specified Content
        /// </summary>
        public static TrakHoundFunctionResponse Ok(byte[] content, string contentType) => new TrakHoundFunctionResponse(200, content, contentType);


        /// <summary>
        /// Create a 201 (Created) Response
        /// </summary>
        public static TrakHoundFunctionResponse Created() => new TrakHoundFunctionResponse(201);
        /// <summary>
        /// Create a 201 (Created) Response with the specified Message
        /// </summary>
        public static TrakHoundFunctionResponse Created(string message) => new TrakHoundFunctionResponse(201, message);
        /// <summary>
        /// Create a 201 (Created) Response with the specified Object serialized as JSON Content
        /// </summary>
        public static TrakHoundFunctionResponse Created(object response) => new TrakHoundFunctionResponse(201, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Create a 201 (Created) Response with the specified Content
        /// </summary>
        public static TrakHoundFunctionResponse Created(byte[] content, string contentType) => new TrakHoundFunctionResponse(201, content, contentType);


        /// <summary>
        /// Create a 202 (Accepted) Response
        /// </summary>
        public static TrakHoundFunctionResponse Accepted() => new TrakHoundFunctionResponse(202);
        /// <summary>
        /// Create a 202 (Accepted) Response with the specified Message
        /// </summary>
        public static TrakHoundFunctionResponse Accepted(string message) => new TrakHoundFunctionResponse(202, message);
        /// <summary>
        /// Create a 202 (Accepted) Response with the specified Object serialized as JSON Content
        /// </summary>
        public static TrakHoundFunctionResponse Accepted(object response) => new TrakHoundFunctionResponse(202, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Create a 202 (Accepted) Response with the specified Content
        /// </summary>
        public static TrakHoundFunctionResponse Accepted(byte[] content, string contentType) => new TrakHoundFunctionResponse(202, content, contentType);

        /// <summary>
        /// Create a 200 (OK) Response containing a File (the content is added as a Base64 encoded Parameter)
        /// </summary>
        public static TrakHoundFunctionResponse File(byte[] content, string contentType, string filename)
        {
            var response = new TrakHoundFunctionResponse();
            response.StatusCode = 200;
            response.AddObjectParameter("filename", filename);
            response.AddObjectParameter("contentType", contentType);
            if (content != null) response.AddObjectParameter("content", Convert.ToBase64String(content));

            return response;
        }

        /// <summary>
        /// Create a 400 (Bad Request) Response
        /// </summary>
        public static TrakHoundFunctionResponse BadRequest() => new TrakHoundFunctionResponse(400);
        /// <summary>
        /// Create a 400 (Bad Request) Response with the specified Message
        /// </summary>
        public static TrakHoundFunctionResponse BadRequest(string message) => new TrakHoundFunctionResponse(400, message);
        /// <summary>
        /// Create a 400 (Bad Request) Response with the specified Object serialized as JSON Content
        /// </summary>
        public static TrakHoundFunctionResponse BadRequest(object response) => new TrakHoundFunctionResponse(400, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Create a 400 (Bad Request) Response with the specified Content
        /// </summary>
        public static TrakHoundFunctionResponse BadRequest(byte[] content, string contentType) => new TrakHoundFunctionResponse(400, content, contentType);


        /// <summary>
        /// Create a 401 (Not Authorized) Response
        /// </summary>
        public static TrakHoundFunctionResponse NotAuthorized() => new TrakHoundFunctionResponse(401);
        /// <summary>
        /// Create a 401 (Not Authorized) Response with the specified Message
        /// </summary>
        public static TrakHoundFunctionResponse NotAuthorized(string message) => new TrakHoundFunctionResponse(401, message);
        /// <summary>
        /// Create a 401 (Not Authorized) Response with the specified Object serialized as JSON Content
        /// </summary>
        public static TrakHoundFunctionResponse NotAuthorized(object response) => new TrakHoundFunctionResponse(401, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Create a 401 (Not Authorized) Response with the specified Content
        /// </summary>
        public static TrakHoundFunctionResponse NotAuthorized(byte[] content, string contentType) => new TrakHoundFunctionResponse(401, content, contentType);


        /// <summary>
        /// Create a 404 (Not Found) Response
        /// </summary>
        public static TrakHoundFunctionResponse NotFound() => new TrakHoundFunctionResponse(404);
        /// <summary>
        /// Create a 404 (Not Found) Response with the specified Message
        /// </summary>
        public static TrakHoundFunctionResponse NotFound(string message) => new TrakHoundFunctionResponse(404, message);
        /// <summary>
        /// Create a 404 (Not Found) Response with the specified Object serialized as JSON Content
        /// </summary>
        public static TrakHoundFunctionResponse NotFound(object response) => new TrakHoundFunctionResponse(404, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Create a 404 (Not Found) Response with the specified Content
        /// </summary>
        public static TrakHoundFunctionResponse NotFound(byte[] content, string contentType) => new TrakHoundFunctionResponse(404, content, contentType);


        /// <summary>
        /// Create a 500 (Internal Error) Response
        /// </summary>
        public static TrakHoundFunctionResponse InternalError() => new TrakHoundFunctionResponse(500);
        /// <summary>
        /// Create a 500 (Internal Error) Response with the specified Message
        /// </summary>
        public static TrakHoundFunctionResponse InternalError(string message) => new TrakHoundFunctionResponse(500, message);
        /// <summary>
        /// Create a 500 (Internal Error) Response with the specified Object serialized as JSON Content
        /// </summary>
        public static TrakHoundFunctionResponse InternalError(object response) => new TrakHoundFunctionResponse(500, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Create a 500 (Internal Error) Response with the specified Content
        /// </summary>
        public static TrakHoundFunctionResponse InternalError(byte[] content, string contentType) => new TrakHoundFunctionResponse(500, content, contentType);
    }
}
