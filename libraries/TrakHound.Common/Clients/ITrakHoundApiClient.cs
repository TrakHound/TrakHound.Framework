// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Api;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to send Requests to and query information about the Apis hosted by a TrakHound Instance
    /// </summary>
    public interface ITrakHoundApiClient
    {
        /// <summary>
        /// Gets the Information for each Api route configured on the Instance
        /// </summary>
        Task<IEnumerable<TrakHoundApiInformation>> GetRouteInformation(CancellationToken cancellationToken = default);
        /// <summary>
        /// Gets the Information for each Api loaded from an installed Package
        /// </summary>
        Task<IEnumerable<TrakHoundApiInformation>> GetPackageInformation(CancellationToken cancellationToken = default);
        /// <summary>
        /// Gets the Information for the Api with the specified ID (null if not found)
        /// </summary>
        Task<TrakHoundApiInformation> GetInformation(string apiId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the Request Metrics for all Apis
        /// </summary>
        Task<IEnumerable<TrakHoundApiMetrics>> GetMetrics(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the Request Metrics for the specified Api (null if apiId is empty or it has not received any Requests)
        /// </summary>
        Task<TrakHoundApiMetrics> GetMetrics(string apiId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the most recent Requests to every Api (newest first)
        /// </summary>
        Task<TrakHoundApiRecentRequests> GetRecentRequests(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the open and most recently closed Streams of every Api
        /// </summary>
        Task<TrakHoundApiStreams> GetStreams(CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes an open Stream (its Source sees the Stream end). Returns true if the Stream was open and is now closed
        /// </summary>
        Task<bool> CloseStream(string streamId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reloads the Configuration for the specified Api. Returns true if the Request was successful
        /// </summary>
        Task<bool> ReloadConfiguration(string apiId, CancellationToken cancellationToken = default);


        /// <summary>
        /// Sends a Request to the specified Api route using the specified method, body, and query parameters
        /// </summary>
        Task<TrakHoundApiResponse> Send(TrakHoundApiMethod requestMethod, string requestRoute, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a Request with Headers (read by the Endpoint's [FromHeader] Parameters) to the specified Api route.
        /// Headers that carry credentials for the Instance are never passed to an Api (see TrakHoundApiHeaders.Withheld)
        /// </summary>
        Task<TrakHoundApiResponse> Send(TrakHoundApiMethod requestMethod, string requestRoute, Stream requestBody, string contentType, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default);


        #region "Headers"

        /// <summary>
        /// Opens a bidirectional Stream with query parameters and Headers to the specified Api route, receiving Api Responses
        /// </summary>
        Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default);

        #endregion
        

        #region "GET"

        /// <summary>
        /// Sends a GET Request to the specified Api route and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Get(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a GET Request to the specified Api route with query parameters and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Get(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a GET Request to the specified Api route and returns the Response content as bytes
        /// </summary>
        Task<byte[]> GetBytes(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a GET Request to the specified Api route with query parameters and returns the Response content as bytes
        /// </summary>
        Task<byte[]> GetBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a GET Request to the specified Api route and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> GetString(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a GET Request to the specified Api route with query parameters and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> GetString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a GET Request to the specified Api route and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> GetJson<TOutput>(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a GET Request to the specified Api route with query parameters and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> GetJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        #endregion

        #region "PUT"

        /// <summary>
        /// Sends a PUT Request to the specified Api route and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Put(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a PUT Request to the specified Api route with query parameters and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Put(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a PUT Request to the specified Api route and returns the Response content as bytes
        /// </summary>
        Task<byte[]> PutBytes(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a PUT Request to the specified Api route with query parameters and returns the Response content as bytes
        /// </summary>
        Task<byte[]> PutBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a PUT Request to the specified Api route and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> PutString(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a PUT Request to the specified Api route with query parameters and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> PutString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a PUT Request to the specified Api route and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> PutJson<TOutput>(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a PUT Request to the specified Api route with query parameters and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> PutJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        #endregion

        #region "POST"

        /// <summary>
        /// Sends a POST Request to the specified Api route and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Post(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with query parameters and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Post(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a body serialized as JSON (by default) and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Post(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a byte array body and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Post(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a Stream body and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Post(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a POST Request to the specified Api route and returns the Response content as bytes
        /// </summary>
        Task<byte[]> PostBytes(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with query parameters and returns the Response content as bytes
        /// </summary>
        Task<byte[]> PostBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a body serialized as JSON (by default) and returns the Response content as bytes
        /// </summary>
        Task<byte[]> PostBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a byte array body and returns the Response content as bytes
        /// </summary>
        Task<byte[]> PostBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a Stream body and returns the Response content as bytes
        /// </summary>
        Task<byte[]> PostBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a POST Request to the specified Api route and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> PostString(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with query parameters and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> PostString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a body serialized as JSON (by default) and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> PostString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a byte array body and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> PostString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a Stream body and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> PostString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a POST Request to the specified Api route and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> PostJson<TOutput>(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with query parameters and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> PostJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a body serialized as JSON (by default) and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> PostJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a byte array body and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> PostJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a POST Request to the specified Api route with a Stream body and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> PostJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);

        #endregion

        #region "DELETE"

        /// <summary>
        /// Sends a DELETE Request to the specified Api route and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Delete(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a DELETE Request to the specified Api route with query parameters and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Delete(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a DELETE Request to the specified Api route and returns the Response content as bytes
        /// </summary>
        Task<byte[]> DeleteBytes(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a DELETE Request to the specified Api route with query parameters and returns the Response content as bytes
        /// </summary>
        Task<byte[]> DeleteBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a DELETE Request to the specified Api route and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> DeleteString(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a DELETE Request to the specified Api route with query parameters and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> DeleteString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a DELETE Request to the specified Api route and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> DeleteJson<TOutput>(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a DELETE Request to the specified Api route with query parameters and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> DeleteJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        #endregion

        #region "PATCH"

        /// <summary>
        /// Sends a PATCH Request to the specified Api route and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Patch(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a PATCH Request to the specified Api route with query parameters and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Patch(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a PATCH Request to the specified Api route and returns the Response content as bytes
        /// </summary>
        Task<byte[]> PatchBytes(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a PATCH Request to the specified Api route with query parameters and returns the Response content as bytes
        /// </summary>
        Task<byte[]> PatchBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a PATCH Request to the specified Api route and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> PatchString(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a PATCH Request to the specified Api route with query parameters and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> PatchString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a PATCH Request to the specified Api route and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> PatchJson<TOutput>(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a PATCH Request to the specified Api route with query parameters and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> PatchJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        #endregion

        #region "HEAD"

        /// <summary>
        /// Sends a HEAD Request to the specified Api route and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Head(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a HEAD Request to the specified Api route with query parameters and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Head(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a HEAD Request to the specified Api route and returns the Response content as bytes
        /// </summary>
        Task<byte[]> HeadBytes(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a HEAD Request to the specified Api route with query parameters and returns the Response content as bytes
        /// </summary>
        Task<byte[]> HeadBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a HEAD Request to the specified Api route and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> HeadString(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a HEAD Request to the specified Api route with query parameters and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> HeadString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a HEAD Request to the specified Api route and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> HeadJson<TOutput>(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a HEAD Request to the specified Api route with query parameters and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> HeadJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        #endregion

        #region "OPTIONS"

        /// <summary>
        /// Sends an OPTIONS Request to the specified Api route and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Options(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends an OPTIONS Request to the specified Api route with query parameters and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Options(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends an OPTIONS Request to the specified Api route and returns the Response content as bytes
        /// </summary>
        Task<byte[]> OptionsBytes(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends an OPTIONS Request to the specified Api route with query parameters and returns the Response content as bytes
        /// </summary>
        Task<byte[]> OptionsBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends an OPTIONS Request to the specified Api route and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> OptionsString(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends an OPTIONS Request to the specified Api route with query parameters and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> OptionsString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends an OPTIONS Request to the specified Api route and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> OptionsJson<TOutput>(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends an OPTIONS Request to the specified Api route with query parameters and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> OptionsJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        #endregion

        #region "CONNECT"

        /// <summary>
        /// Sends a CONNECT Request to the specified Api route and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Connect(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a CONNECT Request to the specified Api route with query parameters and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Connect(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a CONNECT Request to the specified Api route and returns the Response content as bytes
        /// </summary>
        Task<byte[]> ConnectBytes(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a CONNECT Request to the specified Api route with query parameters and returns the Response content as bytes
        /// </summary>
        Task<byte[]> ConnectBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a CONNECT Request to the specified Api route and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> ConnectString(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a CONNECT Request to the specified Api route with query parameters and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> ConnectString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a CONNECT Request to the specified Api route and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> ConnectJson<TOutput>(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a CONNECT Request to the specified Api route with query parameters and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> ConnectJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        #endregion

        #region "TRACE"

        /// <summary>
        /// Sends a TRACE Request to the specified Api route and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Trace(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a TRACE Request to the specified Api route with query parameters and returns the Api Response
        /// </summary>
        Task<TrakHoundApiResponse> Trace(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a TRACE Request to the specified Api route and returns the Response content as bytes
        /// </summary>
        Task<byte[]> TraceBytes(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a TRACE Request to the specified Api route with query parameters and returns the Response content as bytes
        /// </summary>
        Task<byte[]> TraceBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a TRACE Request to the specified Api route and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> TraceString(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a TRACE Request to the specified Api route with query parameters and returns the Response content as a UTF-8 string
        /// </summary>
        Task<string> TraceString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a TRACE Request to the specified Api route and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> TraceJson<TOutput>(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Sends a TRACE Request to the specified Api route with query parameters and deserializes the JSON Response content to TOutput
        /// </summary>
        Task<TOutput> TraceJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);

        #endregion


        #region "Stream"

        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route, receiving Api Responses
        /// </summary>
        Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with query parameters, receiving Api Responses
        /// </summary>
        Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial body serialized as JSON (by default), receiving Api Responses
        /// </summary>
        Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial byte array body, receiving Api Responses
        /// </summary>
        Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial Stream body, receiving Api Responses
        /// </summary>
        Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route, receiving Response content as bytes
        /// </summary>
        Task<ITrakHoundStream<byte[]>> StreamBytes(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with query parameters, receiving Response content as bytes
        /// </summary>
        Task<ITrakHoundStream<byte[]>> StreamBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial body serialized as JSON (by default), receiving Response content as bytes
        /// </summary>
        Task<ITrakHoundStream<byte[]>> StreamBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial byte array body, receiving Response content as bytes
        /// </summary>
        Task<ITrakHoundStream<byte[]>> StreamBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial Stream body, receiving Response content as bytes
        /// </summary>
        Task<ITrakHoundStream<byte[]>> StreamBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route, receiving Response content as UTF-8 strings
        /// </summary>
        Task<ITrakHoundStream<string>> StreamString(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with query parameters, receiving Response content as UTF-8 strings
        /// </summary>
        Task<ITrakHoundStream<string>> StreamString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial body serialized as JSON (by default), receiving Response content as UTF-8 strings
        /// </summary>
        Task<ITrakHoundStream<string>> StreamString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial byte array body, receiving Response content as UTF-8 strings
        /// </summary>
        Task<ITrakHoundStream<string>> StreamString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial Stream body, receiving Response content as UTF-8 strings
        /// </summary>
        Task<ITrakHoundStream<string>> StreamString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route, receiving Response content deserialized from JSON to TOutput
        /// </summary>
        Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with query parameters, receiving Response content deserialized from JSON to TOutput
        /// </summary>
        Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial body serialized as JSON (by default), receiving Response content deserialized from JSON to TOutput
        /// </summary>
        Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial byte array body, receiving Response content deserialized from JSON to TOutput
        /// </summary>
        Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Opens a bidirectional Stream to the specified Api route with an initial Stream body, receiving Response content deserialized from JSON to TOutput
        /// </summary>
        Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default);

        #endregion

    }
}
