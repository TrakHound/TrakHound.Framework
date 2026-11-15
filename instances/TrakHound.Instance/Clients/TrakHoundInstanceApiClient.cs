// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Api;
using TrakHound.Zones;

namespace TrakHound.Clients
{
    public partial class TrakHoundInstanceApiClient : ITrakHoundApiClient
    {
        private readonly TrakHoundInstanceClient _baseClient;
        private readonly ITrakHoundApiProvider _apiProvider;

        /// <summary>
        /// What Requests made through this Client are attributed to (for API Metrics and Zones)
        /// </summary>
        public TrakHoundApiRequestSource Source { get; set; } = TrakHoundApiRequestSource.Instance;


        public TrakHoundInstanceApiClient(TrakHoundInstanceClient baseClient, ITrakHoundApiProvider apiProvider)
        {
            _baseClient = baseClient;
            _apiProvider = apiProvider;
        }


        public async Task<IEnumerable<TrakHoundApiInformation>> GetRouteInformation(CancellationToken cancellationToken = default)
        {
            return _apiProvider.GetRouteInformation();
        }

        public async Task<IEnumerable<TrakHoundApiInformation>> GetPackageInformation(CancellationToken cancellationToken = default)
        {
            return _apiProvider.GetPackageInformation();
        }

        public async Task<TrakHoundApiInformation> GetInformation(string apiId, CancellationToken cancellationToken = default)
        {
            return _apiProvider.GetInformation(apiId);
        }

        public Task<IEnumerable<TrakHoundApiMetrics>> GetMetrics(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_apiProvider.GetMetrics());
        }

        public Task<TrakHoundApiMetrics> GetMetrics(string apiId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_apiProvider.GetMetrics(apiId));
        }

        public Task<TrakHoundApiRecentRequests> GetRecentRequests(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_apiProvider.GetRecentRequests());
        }

        public Task<TrakHoundApiStreams> GetStreams(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_apiProvider.GetStreams());
        }

        public Task<bool> CloseStream(string streamId, CancellationToken cancellationToken = default)
        {
            if (_baseClient.IsAdminInterfaceCall && TrakHound.Instance.Security.TrakHoundAdminContext.GetDenial(TrakHound.Instance.Security.TrakHoundAdminSecurity.Modules, TrakHound.Instance.Security.TrakHoundAdminSecurity.WritePermission) != null) return Task.FromResult(false);

            return Task.FromResult(_apiProvider.CloseStream(streamId));
        }


        public async Task<bool> ReloadConfiguration(string apiId, CancellationToken cancellationToken = default)
        {
            var zoneDecision = _baseClient.AuthorizeZone(TrakHoundZoneResources.GetMemberId(TrakHoundZoneResources.ApiPrefix, apiId), TrakHoundZoneResources.GetManageResourceId(TrakHoundZoneResources.ApiPrefix, apiId), TrakHoundZoneResources.WritePermission);
            if (!zoneDecision.IsAllowed) return false;

            return await _apiProvider.ReloadConfiguration(apiId);
        }


        public async Task<TrakHoundApiResponse> Send(TrakHoundApiMethod requestMethod, string requestRoute, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(requestMethod, requestRoute, requestBody, contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Send(TrakHoundApiMethod requestMethod, string requestRoute, Stream requestBody, string contentType, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(requestMethod, requestRoute, requestBody, contentType, queryParameters, Source, headers, cancellationToken);
        }

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, Dictionary<string, string> queryParameters, IDictionary<string, string> headers, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleStream(route, null, null, queryParameters, Source, headers, cancellationToken);
        }


        #region "Get"

        public async Task<TrakHoundApiResponse> Get(string route, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.GET, route, null, null, null, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Get(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.GET, route, null, null, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Get(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.GET, route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Get(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.GET, route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Get(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.GET, route, requestBody, contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }



        public async Task<byte[]> GetBytes(string route, CancellationToken cancellationToken = default)
        {
            return (await Get(route, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> GetBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Get(route, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> GetBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Get(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> GetBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Get(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> GetBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Get(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }


        public async Task<string> GetString(string route, CancellationToken cancellationToken = default)
        {
            return (await Get(route, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> GetString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Get(route, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> GetString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Get(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> GetString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Get(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> GetString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Get(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }


        public async Task<TOutput> GetJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            return (await Get(route, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> GetJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Get(route, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> GetJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Get(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> GetJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Get(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> GetJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Get(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        #endregion

        #region "Put"

        public async Task<TrakHoundApiResponse> Put(string route, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.PUT, route, null, null, null, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Put(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.PUT, route, null, null, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Put(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.PUT, route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Put(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.PUT, route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Put(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.PUT, route, requestBody, contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }



        public async Task<byte[]> PutBytes(string route, CancellationToken cancellationToken = default)
        {
            return (await Put(route, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PutBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Put(route, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PutBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Put(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PutBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Put(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PutBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Put(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }


        public async Task<string> PutString(string route, CancellationToken cancellationToken = default)
        {
            return (await Put(route, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PutString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Put(route, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PutString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Put(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PutString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Put(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PutString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Put(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }


        public async Task<TOutput> PutJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            return (await Put(route, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PutJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Put(route, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PutJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Put(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PutJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Put(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PutJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Put(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        #endregion

        #region "Post"

        public async Task<TrakHoundApiResponse> Post(string route, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.POST, route, null, null, null, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Post(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.POST, route, null, null, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Post(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.POST, route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Post(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.POST, route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Post(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.POST, route, requestBody, contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }



        public async Task<byte[]> PostBytes(string route, CancellationToken cancellationToken = default)
        {
            return (await Post(route, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PostBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Post(route, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PostBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Post(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PostBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PostBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }


        public async Task<string> PostString(string route, CancellationToken cancellationToken = default)
        {
            return (await Post(route, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PostString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Post(route, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PostString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Post(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PostString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PostString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }


        public async Task<TOutput> PostJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            return (await Post(route, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PostJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Post(route, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PostJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Post(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PostJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PostJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Post(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        #endregion

        #region "Delete"

        public async Task<TrakHoundApiResponse> Delete(string route, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.DELETE, route, null, null, null, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Delete(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.DELETE, route, null, null, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Delete(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.DELETE, route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Delete(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.DELETE, route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Delete(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.DELETE, route, requestBody, contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }



        public async Task<byte[]> DeleteBytes(string route, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> DeleteBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> DeleteBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> DeleteBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> DeleteBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }


        public async Task<string> DeleteString(string route, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> DeleteString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> DeleteString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> DeleteString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> DeleteString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }


        public async Task<TOutput> DeleteJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> DeleteJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> DeleteJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> DeleteJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> DeleteJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Delete(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        #endregion

        #region "Patch"

        public async Task<TrakHoundApiResponse> Patch(string route, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.PATCH, route, null, null, null, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Patch(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.PATCH, route, null, null, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Patch(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.PATCH, route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Patch(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.PATCH, route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Patch(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.PATCH, route, requestBody, contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }



        public async Task<byte[]> PatchBytes(string route, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PatchBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PatchBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PatchBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> PatchBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }


        public async Task<string> PatchString(string route, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PatchString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PatchString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PatchString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> PatchString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }


        public async Task<TOutput> PatchJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PatchJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PatchJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PatchJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> PatchJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Patch(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        #endregion

        #region "Head"

        public async Task<TrakHoundApiResponse> Head(string route, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.HEAD, route, null, null, null, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Head(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.HEAD, route, null, null, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Head(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.HEAD, route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Head(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.HEAD, route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Head(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.HEAD, route, requestBody, contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }



        public async Task<byte[]> HeadBytes(string route, CancellationToken cancellationToken = default)
        {
            return (await Head(route, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> HeadBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Head(route, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> HeadBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Head(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> HeadBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Head(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> HeadBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Head(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }


        public async Task<string> HeadString(string route, CancellationToken cancellationToken = default)
        {
            return (await Head(route, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> HeadString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Head(route, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> HeadString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Head(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> HeadString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Head(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> HeadString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Head(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }


        public async Task<TOutput> HeadJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            return (await Head(route, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> HeadJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Head(route, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> HeadJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Head(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> HeadJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Head(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> HeadJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Head(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        #endregion

        #region "Options"

        public async Task<TrakHoundApiResponse> Options(string route, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.OPTIONS, route, null, null, null, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Options(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.OPTIONS, route, null, null, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Options(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.OPTIONS, route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Options(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.OPTIONS, route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Options(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.OPTIONS, route, requestBody, contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }



        public async Task<byte[]> OptionsBytes(string route, CancellationToken cancellationToken = default)
        {
            return (await Options(route, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> OptionsBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Options(route, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> OptionsBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Options(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> OptionsBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Options(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> OptionsBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Options(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }


        public async Task<string> OptionsString(string route, CancellationToken cancellationToken = default)
        {
            return (await Options(route, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> OptionsString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Options(route, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> OptionsString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Options(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> OptionsString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Options(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> OptionsString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Options(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }


        public async Task<TOutput> OptionsJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            return (await Options(route, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> OptionsJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Options(route, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> OptionsJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Options(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> OptionsJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Options(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> OptionsJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Options(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        #endregion

        #region "Connect"

        public async Task<TrakHoundApiResponse> Connect(string route, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.CONNECT, route, null, null, null, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Connect(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.CONNECT, route, null, null, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Connect(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.CONNECT, route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Connect(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.CONNECT, route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Connect(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.CONNECT, route, requestBody, contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }



        public async Task<byte[]> ConnectBytes(string route, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> ConnectBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> ConnectBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> ConnectBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> ConnectBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }


        public async Task<string> ConnectString(string route, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> ConnectString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> ConnectString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> ConnectString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> ConnectString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }


        public async Task<TOutput> ConnectJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> ConnectJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> ConnectJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> ConnectJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> ConnectJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Connect(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        #endregion

        #region "Trace"

        public async Task<TrakHoundApiResponse> Trace(string route, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.TRACE, route, null, null, null, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Trace(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.TRACE, route, null, null, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Trace(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.TRACE, route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Trace(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.TRACE, route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<TrakHoundApiResponse> Trace(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleRequest(TrakHoundApiMethod.TRACE, route, requestBody, contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }



        public async Task<byte[]> TraceBytes(string route, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> TraceBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> TraceBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> TraceBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }

        public async Task<byte[]> TraceBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentBytes();
        }


        public async Task<string> TraceString(string route, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> TraceString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> TraceString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> TraceString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }

        public async Task<string> TraceString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetContentUtf8String();
        }


        public async Task<TOutput> TraceJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> TraceJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> TraceJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> TraceJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        public async Task<TOutput> TraceJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return (await Trace(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken)).GetJsonContentObject<TOutput>();
        }

        #endregion


        #region "Stream"

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleStream(route, null, null, null, Source, cancellationToken: cancellationToken);
        }

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleStream(route, null, null, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleStream(route, TrakHoundApiResponse.GetJsonContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleStream(route, TrakHoundApiResponse.GetContentStream(requestBody), contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }

        public async Task<ITrakHoundStream<TrakHoundApiResponse>> Stream(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            return await _apiProvider.HandleStream(route, requestBody, contentType, queryParameters, Source, cancellationToken: cancellationToken);
        }


        public async Task<ITrakHoundStream<byte[]>> StreamBytes(string route, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetContentBytes();

                return new TrakHoundStream<TrakHoundApiResponse, byte[]>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<byte[]>> StreamBytes(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetContentBytes();

                return new TrakHoundStream<TrakHoundApiResponse, byte[]>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<byte[]>> StreamBytes(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetContentBytes();

                return new TrakHoundStream<TrakHoundApiResponse, byte[]>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<byte[]>> StreamBytes(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetContentBytes();

                return new TrakHoundStream<TrakHoundApiResponse, byte[]>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<byte[]>> StreamBytes(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetContentBytes();

                return new TrakHoundStream<TrakHoundApiResponse, byte[]>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }


        public async Task<ITrakHoundStream<string>> StreamString(string route, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetContentUtf8String();

                return new TrakHoundStream<TrakHoundApiResponse, string>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<string>> StreamString(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetContentUtf8String();

                return new TrakHoundStream<TrakHoundApiResponse, string>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<string>> StreamString(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetContentUtf8String();

                return new TrakHoundStream<TrakHoundApiResponse, string>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<string>> StreamString(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetContentUtf8String();

                return new TrakHoundStream<TrakHoundApiResponse, string>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<string>> StreamString(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetContentUtf8String();

                return new TrakHoundStream<TrakHoundApiResponse, string>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }


        public async Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetJsonContentObject<TOutput>();

                return new TrakHoundStream<TrakHoundApiResponse, TOutput>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, Dictionary<string, string> queryParameters, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetJsonContentObject<TOutput>();

                return new TrakHoundStream<TrakHoundApiResponse, TOutput>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, object requestBody, string contentType = "application/json", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetJsonContentObject<TOutput>();

                return new TrakHoundStream<TrakHoundApiResponse, TOutput>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, byte[] requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetJsonContentObject<TOutput>();

                return new TrakHoundStream<TrakHoundApiResponse, TOutput>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        public async Task<ITrakHoundStream<TOutput>> StreamJson<TOutput>(string route, Stream requestBody, string contentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, CancellationToken cancellationToken = default)
        {
            var apiStream = await Stream(route, requestBody, contentType, queryParameters, cancellationToken: cancellationToken);
            if (apiStream != null)
            {
                var onReceived = (TrakHoundApiResponse response) => response.GetJsonContentObject<TOutput>();

                return new TrakHoundStream<TrakHoundApiResponse, TOutput>(apiStream, apiStream.CancellationToken, apiStream.Id, onReceived: onReceived);
            }

            return null;
        }

        #endregion


        private static string GetString(byte[] bytes)
        {
            if (!bytes.IsNullOrEmpty())
            {
                try
                {
                    return System.Text.Encoding.UTF8.GetString(bytes);
                }
                catch { }
            }

            return null;
        }
    }
}
