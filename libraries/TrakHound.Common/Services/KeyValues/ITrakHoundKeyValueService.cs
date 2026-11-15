// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.KeyValues
{
    /// <summary>
    /// A Service that stores string Values by Key within named Collections
    /// </summary>
    public interface ITrakHoundKeyValueService : ITrakHoundService
    {
        /// <summary>
        /// Lists the KeyValue Collections in the Service
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(CancellationToken cancellationToken = default);

        /// <summary>
        /// Lists the Keys in the specified Collection that match the pattern ("*" wildcard), paged using skip and take
        /// </summary>
        Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the Values for the specified Keys in the Collection
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the number of Keys in the specified Collection
        /// </summary>
        Task<TrakHoundServiceResponse<int>> Count(string collection, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds or updates each of the Key Value entries in the Collection, with an optional time to live (ttl) if supported by the Service
        /// </summary>
        Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the specified Keys from the Collection
        /// </summary>
        Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default);
    }
}
