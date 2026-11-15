// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;
using TrakHound.Services.KeyValues;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Client used to list, read, publish, and delete Key Values in the KeyValue Service
    /// </summary>
    public interface ITrakHoundKeyValueServiceClient : ITrakHoundServiceClient
    {
        /// <summary>
        /// Lists the KeyValue Collections available through the Router
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the Keys in the specified Collection that match the pattern (paged using skip and take)
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the Value for the specified Key in the Collection
        /// </summary>
        Task<TrakHoundServiceResponse<string>> Read(string collection, string key, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the Values for the specified Keys in the Collection
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, string routerId = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets the number of Keys in the specified Collection
        /// </summary>
        Task<TrakHoundServiceResponse<int>> Count(string collection, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds or updates the Value for the specified Key in the Collection
        /// </summary>
        Task<TrakHoundServiceResponse> Publish(string collection, string key, string value, string routerId = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Adds or updates each of the Key Value entries in the Collection
        /// </summary>
        Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, string routerId = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the specified Keys from the Collection
        /// </summary>
        Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, string routerId = null, CancellationToken cancellationToken = default);
    }
}
