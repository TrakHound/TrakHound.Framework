// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Api;
using TrakHound.Http;

namespace TrakHound.Clients
{
    internal class TrakHoundHttpApiClient : ITrakHoundApiClient
    {
        private readonly TrakHoundHttpClient _baseClient;


        public string HttpBaseUrl => _baseClient != null ? _baseClient.HttpBaseUrl : null;

        public string WebSocketsBaseUrl => _baseClient != null ? _baseClient.WebSocketsBaseUrl : null;

        public string RouterId => _baseClient != null ? _baseClient.RouterId : null;


        public TrakHoundHttpApiClient(TrakHoundHttpClient baseClient)
        {
            _baseClient = baseClient;
        }


        public Task<TrakHoundApiResponse> Send(TrakHoundApiMethod requestMethod, string requestRoute, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return Send(requestMethod, requestRoute, requestBody, contentType, queryParameters, null, cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Send(TrakHoundApiMethod requestMethod, string requestRoute, Stream requestBody, string contentType, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
        {
            var httpMethod = requestMethod.ToHttpMethod();

            var url = Url.Combine(HttpBaseUrl, requestRoute);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            // Sent with the Client's own Headers. Withheld Headers are removed, so a caller can't replace the Client's credentials (ex. its API Key)
            var requestHeaders = TrakHoundApiHeaders.Filter(headers);

            TrakHoundHttpResponse httpResponse;
            if (requestBody != null) httpResponse = await _baseClient.Requests.Send(httpMethod, url, requestBody, contentType, requestHeaders, cancellationToken);
            else if (requestMethod == TrakHoundApiMethod.GET) httpResponse = await _baseClient.Requests.GetResponse(url, requestHeaders, cancellationToken);
            else httpResponse = await _baseClient.Requests.Send(httpMethod, url, (byte[])null, contentType ?? "application/octet-stream", requestHeaders, cancellationToken);

            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }


        #region "Information"

        public async Task<IEnumerable<TrakHoundApiInformation>> GetRouteInformation(CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, "_api/information/route");
            return await _baseClient.Requests.Get<IEnumerable<TrakHoundApiInformation>>(url, cancellationToken: cancellationToken);
        }

        public async Task<IEnumerable<TrakHoundApiInformation>> GetPackageInformation(CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, "_api/information/package");
            return await _baseClient.Requests.Get<IEnumerable<TrakHoundApiInformation>>(url, cancellationToken: cancellationToken);
        }

        public async Task<IEnumerable<TrakHoundApiMetrics>> GetMetrics(CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, "_api/metrics");
            return await _baseClient.Requests.Get<IEnumerable<TrakHoundApiMetrics>>(url, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiMetrics> GetMetrics(string apiId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(apiId)) return null;

            var url = Url.Combine(HttpBaseUrl, "_api/metrics", apiId);
            return await _baseClient.Requests.Get<TrakHoundApiMetrics>(url, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiRecentRequests> GetRecentRequests(CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, "_api/requests");
            return await _baseClient.Requests.Get<TrakHoundApiRecentRequests>(url, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiStreams> GetStreams(CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, "_api/streams");
            return await _baseClient.Requests.Get<TrakHoundApiStreams>(url, cancellationToken: cancellationToken);
        }

        public async Task<bool> CloseStream(string streamId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(streamId)) return false;

            var url = Url.Combine(HttpBaseUrl, "_api/streams", streamId, "close");
            var response = await _baseClient.Requests.PostResponse(url, string.Empty, cancellationToken: cancellationToken);
            return response.Success;
        }

        public async Task<TrakHoundApiInformation> GetInformation(string apiId, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, "_api/information");
            url = Url.Combine(url, apiId);

            return await _baseClient.Requests.Get<TrakHoundApiInformation>(url, cancellationToken: cancellationToken);
        }

        #endregion

        #region "Configuration"

        public async Task<bool> ReloadConfiguration(string apiId, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, "_api/reload", apiId);
            var response = await _baseClient.Requests.PostResponse(url, string.Empty, cancellationToken: cancellationToken);
            return response.Success;
        }

        #endregion


        #region "Get"

        public async Task<TrakHoundApiResponse> Get(string route, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Get(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }


        public async Task<byte[]> GetBytes(string route, CancellationToken cancellationToken = default)
        {
            var response = await Get(route, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> GetBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Get(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }


        public async Task<string> GetString(string route, CancellationToken cancellationToken = default)
        {
            var response = await Get(route, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> GetString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Get(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }


        public async Task<TOutput> GetJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            var response = await Get(route, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> GetJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Get(route, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        #endregion

        #region "Put"

        public async Task<TrakHoundApiResponse> Put(string route, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            var httpResponse = await _baseClient.Requests.PutResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Put(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var httpResponse = await _baseClient.Requests.PutResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }


        public async Task<byte[]> PutBytes(string route, CancellationToken cancellationToken = default)
        {
            var response = await Put(route, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> PutBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Put(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }


        public async Task<string> PutString(string route, CancellationToken cancellationToken = default)
        {
            var response = await Put(route, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> PutString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Put(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }


        public async Task<TOutput> PutJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            var response = await Put(route, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> PutJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Put(route, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        #endregion

        #region "Post"

        public async Task<TrakHoundApiResponse> Post(string route, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Post(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Post(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            TrakHoundHttpResponse httpResponse;
            if (requestBody != null) httpResponse = await _baseClient.Requests.PostResponse(url, requestBody, contentType, cancellationToken: cancellationToken);
            else httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);

            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Post(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            TrakHoundHttpResponse httpResponse;
            if (requestBody != null) httpResponse = await _baseClient.Requests.PostResponse(url, requestBody, contentType, cancellationToken: cancellationToken);
            else httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);

            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Post(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            TrakHoundHttpResponse httpResponse;
            if (requestBody != null) httpResponse = await _baseClient.Requests.PostResponse(url, requestBody, contentType, cancellationToken: cancellationToken);
            else httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);

            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }


        public async Task<byte[]> PostBytes(string route, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> PostBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> PostBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> PostBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> PostBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }


        public async Task<string> PostString(string route, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> PostString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> PostString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> PostString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> PostString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }


        public async Task<TOutput> PostJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> PostJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> PostJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> PostJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> PostJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var response = await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        #endregion

        #region "Patch"

        public async Task<TrakHoundApiResponse> Patch(string route, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Patch(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }


        public async Task<byte[]> PatchBytes(string route, CancellationToken cancellationToken = default)
        {
            var response = await Patch(route, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> PatchBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Patch(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }


        public async Task<string> PatchString(string route, CancellationToken cancellationToken = default)
        {
            var response = await Patch(route, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> PatchString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Patch(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }


        public async Task<TOutput> PatchJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            var response = await Patch(route, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> PatchJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Patch(route, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        #endregion

        #region "Delete"

        public async Task<TrakHoundApiResponse> Delete(string route, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            var httpResponse = await _baseClient.Requests.DeleteResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Delete(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var httpResponse = await _baseClient.Requests.DeleteResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }


        public async Task<byte[]> DeleteBytes(string route, CancellationToken cancellationToken = default)
        {
            var response = await Delete(route, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> DeleteBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Delete(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }


        public async Task<string> DeleteString(string route, CancellationToken cancellationToken = default)
        {
            var response = await Delete(route, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> DeleteString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Delete(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }


        public async Task<TOutput> DeleteJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            var response = await Delete(route, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> DeleteJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Delete(route, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        #endregion

        #region "Head"

        public async Task<TrakHoundApiResponse> Head(string route, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Head(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }


        public async Task<byte[]> HeadBytes(string route, CancellationToken cancellationToken = default)
        {
            var response = await Head(route, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> HeadBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Head(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<string> HeadString(string route, CancellationToken cancellationToken = default)
        {
            var response = await Head(route, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> HeadString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Head(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }


        public async Task<TOutput> HeadJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            var response = await Head(route, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> HeadJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Head(route, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        #endregion

        #region "Options"

        public async Task<TrakHoundApiResponse> Options(string route, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Options(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }


        public async Task<byte[]> OptionsBytes(string route, CancellationToken cancellationToken = default)
        {
            var response = await Options(route, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> OptionsBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Options(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }


        public async Task<string> OptionsString(string route, CancellationToken cancellationToken = default)
        {
            var response = await Options(route, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> OptionsString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Options(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }


        public async Task<TOutput> OptionsJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            var response = await Options(route, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> OptionsJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Options(route, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        #endregion

        #region "Connect"

        public async Task<TrakHoundApiResponse> Connect(string route, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Connect(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }


        public async Task<byte[]> ConnectBytes(string route, CancellationToken cancellationToken = default)
        {
            var response = await Connect(route, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> ConnectBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Connect(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }


        public async Task<string> ConnectString(string route, CancellationToken cancellationToken = default)
        {
            var response = await Connect(route, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> ConnectString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Connect(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }


        public async Task<TOutput> ConnectJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            var response = await Connect(route, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> ConnectJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Connect(route, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        #endregion

        #region "Trace"

        public async Task<TrakHoundApiResponse> Trace(string route, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }

        public async Task<TrakHoundApiResponse> Trace(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(HttpBaseUrl, route);

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var httpResponse = await _baseClient.Requests.GetResponse(url, cancellationToken: cancellationToken);
            var parameters = TrakHoundHttp.GetApiParameters(httpResponse);

            return new TrakHoundApiResponse(httpResponse.StatusCode, httpResponse.Content, httpResponse.ContentType, parameters);
        }


        public async Task<byte[]> TraceBytes(string route, CancellationToken cancellationToken = default)
        {
            var response = await Trace(route, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }

        public async Task<byte[]> TraceBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Trace(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentBytes();
        }


        public async Task<string> TraceString(string route, CancellationToken cancellationToken = default)
        {
            var response = await Trace(route, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }

        public async Task<string> TraceString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Trace(route, queryParameters, cancellationToken: cancellationToken);
            return response.GetContentUtf8String();
        }


        public async Task<TOutput> TraceJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            var response = await Trace(route, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        public async Task<TOutput> TraceJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var response = await Trace(route, queryParameters, cancellationToken: cancellationToken);
            return Json.Convert<TOutput>(response.GetContentUtf8String());
        }

        #endregion


        #region "Stream"

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, CancellationToken cancellationToken = default)
        {
            var url = route;

            var stream = new TrakHoundHttpApiStream(_baseClient.ClientConfiguration, url);
            //stream.Start();
            return stream;
        }

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var url = route;

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var stream = new TrakHoundHttpApiStream(_baseClient.ClientConfiguration, url);
            //stream.Start();
            return stream;
        }

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
        {
            var url = route;

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var stream = new TrakHoundHttpApiStream(_baseClient.ClientConfiguration, url);
            stream.AddHeaders(TrakHoundApiHeaders.Filter(headers));
            return stream;
        }

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var url = route;

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var stream = new TrakHoundHttpApiStream(_baseClient.ClientConfiguration, url, requestBody, contentType);
            //stream.Start();
            return stream;
        }

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var url = route;

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var stream = new TrakHoundHttpApiStream(_baseClient.ClientConfiguration, url, requestBody, contentType);
            //stream.Start();
            return stream;
        }

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var url = route;

            // Add Parameters
            if (!queryParameters.IsNullOrEmpty())
            {
                foreach (var queryParameter in queryParameters)
                {
                    url = Url.AddQueryParameter(url, queryParameter.Key, queryParameter.Value);
                }
            }

            var stream = new TrakHoundHttpApiStream(_baseClient.ClientConfiguration, url, requestBody, contentType);
            //stream.Start();
            return stream;
        }


        public async Task<ITrakHoundStream<byte[]>> StreamBytes(string route, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, byte[]>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetContentBytes();
                stream.OnPush = (bytes) => TrakHoundApiResponse.Ok(bytes, "application/octect-stream");
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<byte[]>> StreamBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, byte[]>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetContentBytes();
                stream.OnPush = (bytes) => TrakHoundApiResponse.Ok(bytes, "application/octect-stream");
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<byte[]>> StreamBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, byte[]>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetContentBytes();
                stream.OnPush = (bytes) => TrakHoundApiResponse.Ok(bytes, "application/octect-stream");
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<byte[]>> StreamBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, byte[]>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetContentBytes();
                stream.OnPush = (bytes) => TrakHoundApiResponse.Ok(bytes, "application/octect-stream");
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<byte[]>> StreamBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, byte[]>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetContentBytes();
                stream.OnPush = (bytes) => TrakHoundApiResponse.Ok(bytes, "application/octect-stream");
                return stream;
            }

            return null;
        }


        public async Task<ITrakHoundStream<string>> StreamString(string route, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, string>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetContentUtf8String();
                stream.OnPush = (str) => TrakHoundApiResponse.Ok(str);
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<string>> StreamString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, string>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetContentUtf8String();
                stream.OnPush = (str) => TrakHoundApiResponse.Ok(str);
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<string>> StreamString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, string>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetContentUtf8String();
                stream.OnPush = (str) => TrakHoundApiResponse.Ok(str);
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<string>> StreamString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, string>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetContentUtf8String();
                stream.OnPush = (str) => TrakHoundApiResponse.Ok(str);
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<string>> StreamString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, string>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetContentUtf8String();
                stream.OnPush = (str) => TrakHoundApiResponse.Ok(str);
                return stream;
            }

            return null;
        }


        public async Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, TOutput>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetJsonContentObject<TOutput>();
                stream.OnPush = (json) => TrakHoundApiResponse.Ok(json);
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, TOutput>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetJsonContentObject<TOutput>();
                stream.OnPush = (json) => TrakHoundApiResponse.Ok(json);
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, TOutput>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetJsonContentObject<TOutput>();
                stream.OnPush = (json) => TrakHoundApiResponse.Ok(json);
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, TOutput>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetJsonContentObject<TOutput>();
                stream.OnPush = (json) => TrakHoundApiResponse.Ok(json);
                return stream;
            }

            return null;
        }

        public async Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiConsumer = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiConsumer != null)
            {
                var stream = new TrakHoundStream<TrakHoundApiResponse, TOutput>(apiConsumer, apiConsumer.CancellationToken, apiConsumer.Id);
                stream.OnInputReceived = (response) => response.GetJsonContentObject<TOutput>();
                stream.OnPush = (json) => TrakHoundApiResponse.Ok(json);
                return stream;
            }

            return null;
        }

        #endregion

    }
}
