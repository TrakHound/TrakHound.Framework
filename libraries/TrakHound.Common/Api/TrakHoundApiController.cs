// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Packages;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Api
{
    /// <summary>
    /// Base class for Api Controllers. Public Methods marked with an Endpoint Attribute (ex. ApiGet) handle Requests
    /// </summary>
    public abstract class TrakHoundApiController : ITrakHoundApiController
    {
        private const string _filenameParameterKey = "Filename";
        private const string _locationParameterKey = "Location";

        private string _assemblyDirectory;
        private string _assemblyPath;

        internal ITrakHoundApiConfiguration _configuration;
        internal TrakHoundPackage _package;
        internal ITrakHoundClient _client;
        internal ITrakHoundVolume _volume;
        internal ITrakHoundVariableCollection _variables;
        internal ITrakHoundLogger _logger;

        /// <summary>
        /// The ID of the Api Configuration the Controller was loaded for
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The ID of the Instance the Controller is running in
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// The base URL of the Instance's HTTP Interface
        /// </summary>
        public string BaseUrl { get; set; }

        /// <summary>
        /// The Route of the Controller
        /// </summary>
        public string BasePath { get; set; }

        /// <summary>
        /// The location of the Controller's Package files (relative to the Instance)
        /// </summary>
        public string BaseLocation { get; set; }

        /// <summary>
        /// The directory of the Assembly that contains the Controller
        /// </summary>
        public string AssemblyDirectory
        {
            get
            {
                if (string.IsNullOrEmpty(_assemblyDirectory))
                {
                    _assemblyDirectory = Path.GetDirectoryName(Assembly.GetAssembly(GetType()).Location);
                }

                return _assemblyDirectory;
            }
        }

        /// <summary>
        /// The full path of the Assembly that contains the Controller
        /// </summary>
        public string AssemblyPath
        {
            get
            {
                if (string.IsNullOrEmpty(_assemblyPath))
                {
                    _assemblyPath = Assembly.GetAssembly(GetType()).Location;
                }

                return _assemblyPath;
            }
        }

        /// <summary>
        /// The Api Configuration the Controller was loaded for
        /// </summary>
        public ITrakHoundApiConfiguration Configuration => _configuration;

        /// <summary>
        /// Gets the value of a Configuration Variable shared by every configuration in the Profile (decrypted, with ${env:NAME} references replaced).
        /// Returns null if it isn't defined. Parameters that reference a Variable as ${name} are already replaced by GetParameter().
        /// </summary>
        public string GetConfigurationVariable(string name)
        {
            return TrakHound.Configurations.TrakHoundConfigurationVariables.GetValue(name);
        }

        /// <summary>
        /// The Package the Controller was loaded from
        /// </summary>
        public TrakHoundPackage Package => _package;

        /// <summary>
        /// The Client used to access the Instance (using the Configuration's Router)
        /// </summary>
        public ITrakHoundClient Client => _client;

        /// <summary>
        /// The Volume used for file storage
        /// </summary>
        public ITrakHoundVolume Volume => _volume;

        /// <summary>
        /// The Variables available to the Api
        /// </summary>
        public ITrakHoundVariableCollection Variables => _variables;

        /// <summary>
        /// The Logger used to write Log entries for the Controller
        /// </summary>
        public ITrakHoundLogger Logger => _logger;


        /// <summary>
        /// Sets the Api Configuration
        /// </summary>
        public void SetConfiguration(ITrakHoundApiConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Sets the Package and calls AfterPackageSet()
        /// </summary>
        public void SetPackage(TrakHoundPackage package)
        {
            _package = package;
            AfterPackageSet();
        }

        /// <summary>
        /// Sets the Client and calls AfterClientSet()
        /// </summary>
        public void SetClient(ITrakHoundClient client)
        {
            _client = client;
            AfterClientSet();
        }

        /// <summary>
        /// Sets the Volume and calls AfterVolumeSet()
        /// </summary>
        public void SetVolume(ITrakHoundVolume volume)
        {
            _volume = volume;
            AfterVolumeSet();
        }

        /// <summary>
        /// Sets the Variables and calls AfterVariablesSet()
        /// </summary>
        public void SetVariables(ITrakHoundVariableCollection variables)
        {
            _variables = variables;
            AfterVariablesSet();
        }

        /// <summary>
        /// Sets the Logger and calls AfterLoggerSet()
        /// </summary>
        public void SetLogger(ITrakHoundLogger logger)
        {
            _logger = logger;
            AfterLoggerSet();
        }

        /// <summary>
        /// Initializes the Controller by calling OnInitialize()
        /// </summary>
        public void Initialize()
        {
            OnInitialize();
        }

        /// <summary>
        /// Initializes the Controller by calling OnInitializeAsync()
        /// </summary>
        public Task InitializeAsync()
        {
            return OnInitializeAsync();
        }

        /// <summary>
        /// Override to run synchronous initialization after the Controller is loaded
        /// </summary>
        protected virtual void OnInitialize() { }

        /// <summary>
        /// Override to run asynchronous initialization after the Controller is loaded
        /// </summary>
        protected virtual Task OnInitializeAsync() => Task.CompletedTask;


        /// <summary>
        /// Combines BaseUrl with the specified destination
        /// </summary>
        protected string GetUrl(string destination)
        {
            return Url.Combine(BaseUrl, destination);
        }

        /// <summary>
        /// Combines BasePath with the specified destination
        /// </summary>
        protected string GetPath(string destination)
        {
            return Url.Combine(BasePath, destination);
        }

        /// <summary>
        /// Combines BaseLocation with the specified destination
        /// </summary>
        protected string GetLocation(string destination)
        {
            return Url.Combine(BaseLocation, destination);
        }


        /// <summary>
        /// Called after the Package is set
        /// </summary>
        protected virtual void AfterPackageSet() { }

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
        /// Called after the Logger is set
        /// </summary>
        protected virtual void AfterLoggerSet() { }


        /// <summary>
        /// Writes an entry to the "Main" Log (ignored if no Logger is set)
        /// </summary>
        protected void Log(TrakHoundLogLevel level, string message, string code = null)
        {
            if (_logger != null) _logger.Log(new TrakHoundLogItem("Main", level, message, code, UnixDateTime.Now));
        }

        /// <summary>
        /// Writes an entry to the specified Log (ignored if no Logger is set)
        /// </summary>
        protected void Log(string logName, TrakHoundLogLevel level, string message, string code = null)
        {
            if (_logger != null) _logger.Log(new TrakHoundLogItem(logName, level, message, code, UnixDateTime.Now));
        }


        /// <summary>
        /// Creates a 200 (OK) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse Ok() => new TrakHoundApiResponse(200);
        /// <summary>
        /// Creates a 200 (OK) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse Ok(string message) => new TrakHoundApiResponse(200, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 200 (OK) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse Ok(object response) => new TrakHoundApiResponse(200, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Creates a 200 (OK) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse Ok(byte[] content, string contentType) => new TrakHoundApiResponse(200, content, contentType);


        /// <summary>
        /// Creates a 201 (Created) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse Created() => new TrakHoundApiResponse(201);
        /// <summary>
        /// Creates a 201 (Created) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse Created(string message) => new TrakHoundApiResponse(201, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 201 (Created) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse Created(object response) => new TrakHoundApiResponse(201, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Creates a 201 (Created) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse Created(byte[] content, string contentType) => new TrakHoundApiResponse(201, content, contentType);


        /// <summary>
        /// Creates a 202 (Accepted) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse Accepted() => new TrakHoundApiResponse(202);
        /// <summary>
        /// Creates a 202 (Accepted) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse Accepted(string message) => new TrakHoundApiResponse(202, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 202 (Accepted) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse Accepted(object response) => new TrakHoundApiResponse(202, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Creates a 202 (Accepted) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse Accepted(byte[] content, string contentType) => new TrakHoundApiResponse(202, content, contentType);

        /// <summary>
        /// Creates a 200 (OK) Response that returns the Content as a file with the specified filename
        /// </summary>
        public static TrakHoundApiResponse File(byte[] content, string contentType, string filename)
        {
            var response = new TrakHoundApiResponse();
            response.Success = true;
            response.StatusCode = 200;
            response.ContentType = contentType;

            var parameters = new Dictionary<string, string>();
            parameters.Add(_filenameParameterKey, filename);
            response.Parameters = parameters;

            if (content != null)
            {
                try
                {
                    response.Content = new MemoryStream(content);
                }
                catch { }
            }

            return response;
        }

        /// <summary>
        /// Creates a 200 (OK) Response that returns the Content Stream as a file with the specified filename
        /// </summary>
        public static TrakHoundApiResponse File(Stream content, string contentType, string filename)
        {
            var response = new TrakHoundApiResponse();
            response.Success = true;
            response.StatusCode = 200;
            response.Content = content;
            response.ContentType = contentType;

            var parameters = new Dictionary<string, string>();
            parameters.Add(_filenameParameterKey, filename);
            response.Parameters = parameters;

            return response;
        }


        /// <summary>
        /// Creates a Redirect Response to the specified location (303 See Other by default)
        /// </summary>
        public static TrakHoundApiResponse Redirect(string location, int statusCode = 303)
        {
            var response = new TrakHoundApiResponse();
            response.Success = true;
            response.StatusCode = statusCode;

            var parameters = new Dictionary<string, string>();
            parameters.Add(_locationParameterKey, location);
            response.Parameters = parameters;

            return response;
        }


        /// <summary>
        /// Creates a 400 (Bad Request) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse BadRequest() => new TrakHoundApiResponse(400);
        /// <summary>
        /// Creates a 400 (Bad Request) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse BadRequest(string message) => new TrakHoundApiResponse(400, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 400 (Bad Request) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse BadRequest(object response) => new TrakHoundApiResponse(400, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Creates a 400 (Bad Request) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse BadRequest(byte[] content, string contentType) => new TrakHoundApiResponse(400, content, contentType);


        /// <summary>
        /// Creates a 401 (Unauthorized) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse NotAuthorized() => new TrakHoundApiResponse(401);
        /// <summary>
        /// Creates a 401 (Unauthorized) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse NotAuthorized(string message) => new TrakHoundApiResponse(401, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 401 (Unauthorized) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse NotAuthorized(object response) => new TrakHoundApiResponse(401, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Creates a 401 (Unauthorized) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse NotAuthorized(byte[] content, string contentType) => new TrakHoundApiResponse(401, content, contentType);


        /// <summary>
        /// Creates a 404 (Not Found) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse NotFound() => new TrakHoundApiResponse(404);
        /// <summary>
        /// Creates a 404 (Not Found) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse NotFound(string message) => new TrakHoundApiResponse(404, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 404 (Not Found) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse NotFound(object response) => new TrakHoundApiResponse(404, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Creates a 404 (Not Found) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse NotFound(byte[] content, string contentType) => new TrakHoundApiResponse(404, content, contentType);


        /// <summary>
        /// Creates a 500 (Internal Server Error) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse InternalError() => new TrakHoundApiResponse(500);
        /// <summary>
        /// Creates a 500 (Internal Server Error) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse InternalError(string message) => new TrakHoundApiResponse(500, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 500 (Internal Server Error) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse InternalError(object response) => new TrakHoundApiResponse(500, response.ToJsonBytes(true), "application/json");
        /// <summary>
        /// Creates a 500 (Internal Server Error) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse InternalError(byte[] content, string contentType) => new TrakHoundApiResponse(500, content, contentType);
    }
}
