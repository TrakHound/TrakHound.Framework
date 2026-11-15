// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using TrakHound.Services;
using TrakHound.Services.Blobs;

namespace TrakHound.Testing.Services
{
    /// <summary>
    /// A Blob Service that keeps its Blobs in memory (the default for Client.Services.Blobs in TrakHoundTestClient)
    /// </summary>
    public class TrakHoundMemoryBlobService : TrakHoundService, ITrakHoundBlobService
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, (TrakHoundBlob Blob, byte[] Content)>> _collections = new(StringComparer.OrdinalIgnoreCase);


        public TrakHoundMemoryBlobService() : base(new TrakHoundServiceConfiguration { Id = "memory-blobs" }) { }


        public override Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Ok(new TrakHoundServiceAvailabilityResponse { IsAvailable = true, Message = "Available" }));
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>> ListCollections(CancellationToken cancellationToken = default)
        {
            IEnumerable<TrakHoundBlobCollection> collections = _collections
                .Where(o => !o.Value.IsEmpty)
                .OrderBy(o => o.Key)
                .Select(o => new TrakHoundBlobCollection { Name = o.Key, Count = o.Value.Count })
                .ToList();

            return Task.FromResult(Ok(collections));
        }

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, int skip, int take, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection)) return Task.FromResult(BadRequest<IEnumerable<string>>("Collection is required"));
            if (!_collections.TryGetValue(collection, out var blobs)) return Task.FromResult(NotFound<IEnumerable<string>>());

            IEnumerable<string> keys = blobs.Keys.OrderBy(o => o).Skip(Math.Max(skip, 0));
            if (take > 0) keys = keys.Take(take);

            return Task.FromResult(Ok<IEnumerable<string>>(keys.ToList()));
        }

        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>> List(string collection, int skip, int take, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection)) return Task.FromResult(BadRequest<IEnumerable<TrakHoundBlob>>("Collection is required"));
            if (!_collections.TryGetValue(collection, out var blobs)) return Task.FromResult(NotFound<IEnumerable<TrakHoundBlob>>());

            IEnumerable<TrakHoundBlob> results = blobs.OrderBy(o => o.Key).Select(o => o.Value.Blob).Skip(Math.Max(skip, 0));
            if (take > 0) results = results.Take(take);

            return Task.FromResult(Ok<IEnumerable<TrakHoundBlob>>(results.ToList()));
        }

        public Task<TrakHoundServiceResponse<TrakHoundBlob>> Read(string collection, string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(key)) return Task.FromResult(BadRequest<TrakHoundBlob>("Collection and Key are required"));
            if (!_collections.TryGetValue(collection, out var blobs) || !blobs.TryGetValue(key, out var entry)) return Task.FromResult(NotFound<TrakHoundBlob>());

            return Task.FromResult(Ok(entry.Blob));
        }

        public Task<TrakHoundServiceResponse<Stream>> Download(string collection, string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(key)) return Task.FromResult(BadRequest<Stream>("Collection and Key are required"));
            if (!_collections.TryGetValue(collection, out var blobs) || !blobs.TryGetValue(key, out var entry)) return Task.FromResult(NotFound<Stream>());

            return Task.FromResult(Ok<Stream>(new MemoryStream(entry.Content, false)));
        }

        public async Task<TrakHoundServiceResponse> Upload(string collection, string key, Stream content, string contentType, IReadOnlyDictionary<string, string> metadata, bool overwrite = true, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(key) || content == null) return BadRequest("Collection, Key and Content are required");

            var blobs = _collections.GetOrAdd(collection, _ => new ConcurrentDictionary<string, (TrakHoundBlob, byte[])>(StringComparer.OrdinalIgnoreCase));
            if (!overwrite && blobs.ContainsKey(key)) return TrakHoundServiceResponse.Conflict($"Blob already exists : {collection} : {key}");

            byte[] bytes;
            using (var memoryStream = new MemoryStream())
            {
                await content.CopyToAsync(memoryStream, cancellationToken);
                bytes = memoryStream.ToArray();
            }

            var blob = new TrakHoundBlob();
            blob.Collection = collection;
            blob.Key = key;
            blob.ContentType = contentType;
            blob.Size = bytes.Length;
            blob.Created = UnixDateTime.Now;
            blob.Metadata = metadata != null ? new Dictionary<string, string>(metadata) : null;

            blobs[key] = (blob, bytes);
            return Ok();
        }

        public Task<TrakHoundServiceResponse> Delete(string collection, string key, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(key)) return Task.FromResult(BadRequest("Collection and Key are required"));

            if (_collections.TryGetValue(collection, out var blobs) && blobs.TryRemove(key, out _)) return Task.FromResult(Ok());
            return Task.FromResult(NotFound());
        }
    }
}
