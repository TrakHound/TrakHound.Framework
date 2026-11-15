// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.Blobs;
using TrakHound.Services.KeyValues;
using TrakHound.Services.Tables;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Testing.Clients
{
    // Clients that call a Service directly, the way the Instance's Client calls the Service its Router selects.
    // The Router ID is ignored (a test Client has one Service per family). Missing arguments return Bad Request


    /// <summary>
    /// Client.Services.KeyValues for a Key Value Service
    /// </summary>
    public class TrakHoundTestKeyValueServiceClient : ITrakHoundKeyValueServiceClient
    {
        private readonly ITrakHoundKeyValueService _service;


        public ITrakHoundKeyValueService Service => _service;


        public TrakHoundTestKeyValueServiceClient(ITrakHoundKeyValueService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }


        public Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default) => _service.CheckAvailability(cancellationToken);

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(string routerId = null, CancellationToken cancellationToken = default) => _service.ListCollections(cancellationToken);

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(pattern)) return Task.FromResult(TrakHoundServiceResponse<IEnumerable<string>>.BadRequest("Collection and Pattern are required"));
            return _service.ListKeys(collection, pattern, skip, take, cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<string>> Read(string collection, string key, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(key)) return TrakHoundServiceResponse<string>.BadRequest("Collection and Key are required");

            var response = await _service.Read(collection, new[] { key }, cancellationToken);

            var output = new TrakHoundServiceResponse<string>();
            output.StatusCode = response.StatusCode;
            output.Message = response.Message;
            if (response.Result?.Values != null && response.Result.Values.TryGetValue(key, out var value)) output.Result = value;
            return output;
        }

        public Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || keys.IsNullOrEmpty()) return Task.FromResult(TrakHoundServiceResponse<TrakHoundKeyValueResponse>.BadRequest("Collection and Keys are required"));
            return _service.Read(collection, keys, cancellationToken);
        }

        public Task<TrakHoundServiceResponse<int>> Count(string collection, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection)) return Task.FromResult(TrakHoundServiceResponse<int>.BadRequest("Collection is required"));
            return _service.Count(collection, cancellationToken);
        }

        public Task<TrakHoundServiceResponse> Publish(string collection, string key, string value, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return Task.FromResult(TrakHoundServiceResponse.BadRequest("Collection, Key and Value are required"));
            return _service.Publish(collection, new Dictionary<string, string> { [key] = value }, cancellationToken: cancellationToken);
        }

        public Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || entries.IsNullOrEmpty()) return Task.FromResult(TrakHoundServiceResponse.BadRequest("Collection and Entries are required"));
            return _service.Publish(collection, entries, cancellationToken: cancellationToken);
        }

        public Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || keys.IsNullOrEmpty()) return Task.FromResult(TrakHoundServiceResponse.BadRequest("Collection and Keys are required"));
            return _service.Delete(collection, keys, cancellationToken);
        }
    }


    /// <summary>
    /// Client.Services.Blobs for a Blob Service
    /// </summary>
    public class TrakHoundTestBlobServiceClient : ITrakHoundBlobServiceClient
    {
        private readonly ITrakHoundBlobService _service;


        public ITrakHoundBlobService Service => _service;


        public TrakHoundTestBlobServiceClient(ITrakHoundBlobService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }


        public Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default) => _service.CheckAvailability(cancellationToken);

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>> ListCollections(string routerId = null, CancellationToken cancellationToken = default) => _service.ListCollections(cancellationToken);

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, int skip, int take, string routerId = null, CancellationToken cancellationToken = default) => _service.ListKeys(collection, skip, take, cancellationToken);

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>> List(string collection, int skip, int take, string routerId = null, CancellationToken cancellationToken = default) => _service.List(collection, skip, take, cancellationToken);

        public Task<TrakHoundServiceResponse<TrakHoundBlob>> Read(string collection, string key, string routerId = null, CancellationToken cancellationToken = default) => _service.Read(collection, key, cancellationToken);

        public Task<TrakHoundServiceResponse<Stream>> Download(string collection, string key, string routerId = null, CancellationToken cancellationToken = default) => _service.Download(collection, key, cancellationToken);

        public Task<TrakHoundServiceResponse> Upload(string collection, string key, Stream content, string contentType = "application/octet-stream", IReadOnlyDictionary<string, string> metadata = null, bool overwrite = true, string routerId = null, CancellationToken cancellationToken = default) => _service.Upload(collection, key, content, contentType, metadata, overwrite, cancellationToken);

        public Task<TrakHoundServiceResponse> Delete(string collection, string key, string routerId = null, CancellationToken cancellationToken = default) => _service.Delete(collection, key, cancellationToken);
    }


    /// <summary>
    /// Client.Services.Tables for a Table Service (the typed reads use the same helpers as the Instance's Client)
    /// </summary>
    public class TrakHoundTestTableServiceClient : ITrakHoundTableServiceClient
    {
        private readonly ITrakHoundTableService _service;


        public ITrakHoundTableService Service => _service;


        public TrakHoundTestTableServiceClient(ITrakHoundTableService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }


        public Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default) => _service.CheckAvailability(cancellationToken);

        public Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(string routerId = null, CancellationToken cancellationToken = default) => _service.List(cancellationToken);

        public Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, string routerId = null, CancellationToken cancellationToken = default) => _service.GetColumns(tableName, cancellationToken);

        public Task<TrakHoundServiceResponse<string[]>> GetDialects(string routerId = null, CancellationToken cancellationToken = default) => _service.GetDialects(cancellationToken);

        public Task<TrakHoundServiceResponse<TrakHoundTableStepResult[]>> Execute(TrakHoundTableRequest request, string routerId = null, CancellationToken cancellationToken = default) => _service.Execute(request, cancellationToken);

        public Task<TrakHoundServiceResponse> ExecuteNonQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => _service.ExecuteNonQuery(query, timeout, parameters, cancellationToken);

        public Task<TrakHoundServiceResponse<TrakHoundTableResult[]>> ExecuteQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => _service.ExecuteQuery(query, timeout, parameters, cancellationToken);

        public Task<TrakHoundServiceResponse<string>> ReadSingleString(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => TrakHoundTableServiceClient.ReadSingleString(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ReadMultipleString(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => TrakHoundTableServiceClient.ReadMultipleString(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);

        public Task<TrakHoundServiceResponse<T>> ReadSingle<T>(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => TrakHoundTableServiceClient.ReadSingle<T>(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);

        public Task<TrakHoundServiceResponse<IEnumerable<T>>> ReadMultiple<T>(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => TrakHoundTableServiceClient.ReadMultiple<T>(this, query, resultIndex, timeout, parameters, routerId, cancellationToken);
    }


    /// <summary>
    /// Client.Services.TimeSeries for a Time Series Service
    /// </summary>
    public class TrakHoundTestTimeSeriesServiceClient : ITrakHoundTimeSeriesServiceClient
    {
        private readonly ITrakHoundTimeSeriesService _service;


        public ITrakHoundTimeSeriesService Service => _service;


        public TrakHoundTestTimeSeriesServiceClient(ITrakHoundTimeSeriesService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }


        public Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default) => _service.CheckAvailability(cancellationToken);

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListMeasurements(string routerId = null, CancellationToken cancellationToken = default) => _service.ListMeasurements(cancellationToken);

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>> ListSeries(string measurement, IReadOnlyDictionary<string, string> tags = null, int skip = 0, int take = TrakHoundTimeSeriesQuery.DefaultTake, string routerId = null, CancellationToken cancellationToken = default) => _service.ListSeries(measurement, tags, skip, take, cancellationToken);

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>> ListFields(string measurement, string routerId = null, CancellationToken cancellationToken = default) => _service.ListFields(measurement, cancellationToken);

        public Task<TrakHoundServiceResponse> Write(IEnumerable<TrakHoundTimeSeriesPoint> points, string routerId = null, CancellationToken cancellationToken = default) => _service.Write(points, cancellationToken);

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Read(TrakHoundTimeSeriesQuery query, string routerId = null, CancellationToken cancellationToken = default) => _service.Read(query, cancellationToken);

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>> ReadLatest(string measurement, IReadOnlyDictionary<string, string[]> tags = null, IEnumerable<string> fields = null, string routerId = null, CancellationToken cancellationToken = default) => _service.ReadLatest(measurement, tags, fields, cancellationToken);

        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Aggregate(TrakHoundTimeSeriesAggregateQuery query, string routerId = null, CancellationToken cancellationToken = default) => _service.Aggregate(query, cancellationToken);

        public Task<TrakHoundServiceResponse> Delete(string measurement, IReadOnlyDictionary<string, string> tags, DateTimeOffset start, DateTimeOffset stop, string routerId = null, CancellationToken cancellationToken = default) => _service.Delete(measurement, tags, start, stop, cancellationToken);
    }
}
