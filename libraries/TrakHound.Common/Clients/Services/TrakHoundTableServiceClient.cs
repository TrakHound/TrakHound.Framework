// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Services;
using TrakHound.Services.Tables;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Helper methods that implement the typed Table Service reads on top of ITrakHoundTableServiceClient.ExecuteQuery.
    /// They read the Result set at resultIndex, or every Result set when it is not set. A failed Query keeps the Service's
    /// Status Code (ex. 503 Not Available), and a value that can't be converted returns an Internal Error (500)
    /// </summary>
    public static class TrakHoundTableServiceClient
    {
        /// <summary>
        /// Executes the Query and returns the first Column of the first row, from the first Result set where it is not null (Not Found if there is none)
        /// </summary>
        public static Task<TrakHoundServiceResponse<string>> ReadSingleString(ITrakHoundTableServiceClient client, string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Read(client, query, resultIndex, timeout, parameters, routerId, cancellationToken, results =>
            {
                var value = results.Select(o => o.ReadSingleString()).FirstOrDefault(o => o != null);
                return value != null ? TrakHoundServiceResponse<string>.Ok(value) : TrakHoundServiceResponse<string>.NotFound();
            });
        }

        /// <summary>
        /// Executes the Query and returns the non-null values of the first Column (Ok with an empty list if there are none)
        /// </summary>
        public static Task<TrakHoundServiceResponse<IEnumerable<string>>> ReadMultipleString(ITrakHoundTableServiceClient client, string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Read(client, query, resultIndex, timeout, parameters, routerId, cancellationToken, results =>
            {
                var values = results.SelectMany(o => o.ReadMultipleString() ?? Enumerable.Empty<string>()).ToList();
                return TrakHoundServiceResponse<IEnumerable<string>>.Ok(values);
            });
        }

        /// <summary>
        /// Executes the Query and returns the first row read into T (Not Found if there are no rows).
        /// See TrakHoundTableResult.ReadMultiple for how rows are read
        /// </summary>
        public static Task<TrakHoundServiceResponse<T>> ReadSingle<T>(ITrakHoundTableServiceClient client, string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Read(client, query, resultIndex, timeout, parameters, routerId, cancellationToken, results =>
            {
                foreach (var result in results)
                {
                    // Only the first row is read
                    foreach (var item in TrakHoundTableReader.Read<T>(result))
                    {
                        return TrakHoundServiceResponse<T>.Ok(item);
                    }
                }

                return TrakHoundServiceResponse<T>.NotFound();
            });
        }

        /// <summary>
        /// Executes the Query and returns each row read into T (Ok with an empty list if there are no rows).
        /// See TrakHoundTableResult.ReadMultiple for how rows are read
        /// </summary>
        public static Task<TrakHoundServiceResponse<IEnumerable<T>>> ReadMultiple<T>(ITrakHoundTableServiceClient client, string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Read(client, query, resultIndex, timeout, parameters, routerId, cancellationToken, results =>
            {
                var items = results.SelectMany(TrakHoundTableReader.Read<T>).ToList();
                return TrakHoundServiceResponse<IEnumerable<T>>.Ok(items);
            });
        }


        /// <summary>
        /// Runs a Request and returns the result sets of its Query steps, in order
        /// </summary>
        public static async Task<TrakHoundServiceResponse<TrakHoundTableResult[]>> ExecuteQuery(this ITrakHoundTableServiceClient client, TrakHoundTableRequest request, string routerId = null, CancellationToken cancellationToken = default)
        {
            var response = await client.Execute(request, routerId, cancellationToken);

            var results = response.Success ? response.Result?.Where(o => o?.Results != null).SelectMany(o => o.Results).ToArray() : null;
            return new TrakHoundServiceResponse<TrakHoundTableResult[]>(results, response.StatusCode, response.Message) { RouterId = response.RouterId, ServiceId = response.ServiceId, Duration = response.Duration };
        }

        /// <summary>
        /// Runs a Request and returns its Status Code and the message of its last step
        /// </summary>
        public static async Task<TrakHoundServiceResponse> ExecuteNonQuery(this ITrakHoundTableServiceClient client, TrakHoundTableRequest request, string routerId = null, CancellationToken cancellationToken = default)
        {
            var response = await client.Execute(request, routerId, cancellationToken);

            var message = response.Success ? response.Result?.LastOrDefault()?.Message : response.Message;
            return new TrakHoundServiceResponse(response.StatusCode, message) { RouterId = response.RouterId, ServiceId = response.ServiceId, Duration = response.Duration };
        }

        /// <summary>
        /// Runs a Request and returns the first Column of the first row, from the first Result set where it is not null (Not Found if there is none)
        /// </summary>
        public static Task<TrakHoundServiceResponse<string>> ReadSingleString(this ITrakHoundTableServiceClient client, TrakHoundTableRequest request, int? resultIndex = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Read(client, request, resultIndex, routerId, cancellationToken, ReadSingleString);
        }

        /// <summary>
        /// Runs a Request and returns the non-null values of the first Column (Ok with an empty list if there are none)
        /// </summary>
        public static Task<TrakHoundServiceResponse<IEnumerable<string>>> ReadMultipleString(this ITrakHoundTableServiceClient client, TrakHoundTableRequest request, int? resultIndex = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Read(client, request, resultIndex, routerId, cancellationToken, ReadMultipleString);
        }

        /// <summary>
        /// Runs a Request and returns the first row read into T (Not Found if there are no rows)
        /// </summary>
        public static Task<TrakHoundServiceResponse<T>> ReadSingle<T>(this ITrakHoundTableServiceClient client, TrakHoundTableRequest request, int? resultIndex = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Read(client, request, resultIndex, routerId, cancellationToken, ReadSingle<T>);
        }

        /// <summary>
        /// Runs a Request and returns each row read into T (Ok with an empty list if there are no rows)
        /// </summary>
        public static Task<TrakHoundServiceResponse<IEnumerable<T>>> ReadMultiple<T>(this ITrakHoundTableServiceClient client, TrakHoundTableRequest request, int? resultIndex = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Read(client, request, resultIndex, routerId, cancellationToken, ReadMultiple<T>);
        }


        private static TrakHoundServiceResponse<string> ReadSingleString(IEnumerable<TrakHoundTableResult> results)
        {
            var value = results.Select(o => o.ReadSingleString()).FirstOrDefault(o => o != null);
            return value != null ? TrakHoundServiceResponse<string>.Ok(value) : TrakHoundServiceResponse<string>.NotFound();
        }

        private static TrakHoundServiceResponse<IEnumerable<string>> ReadMultipleString(IEnumerable<TrakHoundTableResult> results)
        {
            var values = results.SelectMany(o => o.ReadMultipleString() ?? Enumerable.Empty<string>()).ToList();
            return TrakHoundServiceResponse<IEnumerable<string>>.Ok(values);
        }

        private static TrakHoundServiceResponse<T> ReadSingle<T>(IEnumerable<TrakHoundTableResult> results)
        {
            foreach (var result in results)
            {
                // Only the first row is read
                foreach (var item in TrakHoundTableReader.Read<T>(result))
                {
                    return TrakHoundServiceResponse<T>.Ok(item);
                }
            }

            return TrakHoundServiceResponse<T>.NotFound();
        }

        private static TrakHoundServiceResponse<IEnumerable<T>> ReadMultiple<T>(IEnumerable<TrakHoundTableResult> results)
        {
            var items = results.SelectMany(TrakHoundTableReader.Read<T>).ToList();
            return TrakHoundServiceResponse<IEnumerable<T>>.Ok(items);
        }

        private static async Task<TrakHoundServiceResponse<TResult>> Read<TResult>(
            ITrakHoundTableServiceClient client,
            TrakHoundTableRequest request,
            int? resultIndex,
            string routerId,
            CancellationToken cancellationToken,
            Func<IEnumerable<TrakHoundTableResult>, TrakHoundServiceResponse<TResult>> read
            )
        {
            if (client == null) return TrakHoundServiceResponse<TResult>.BadRequest("Client Not Specified");
            if (request == null) return TrakHoundServiceResponse<TResult>.BadRequest("Request Not Specified");
            if (resultIndex < 0) return TrakHoundServiceResponse<TResult>.BadRequest($"Invalid Result Index ({resultIndex})");

            return ReadResponse(await client.ExecuteQuery(request, routerId, cancellationToken), resultIndex, read);
        }

        private static async Task<TrakHoundServiceResponse<TResult>> Read<TResult>(
            ITrakHoundTableServiceClient client,
            string query,
            int? resultIndex,
            int? timeout,
            TrakHoundTableParameterCollection parameters,
            string routerId,
            CancellationToken cancellationToken,
            Func<IEnumerable<TrakHoundTableResult>, TrakHoundServiceResponse<TResult>> read
            )
        {
            if (client == null) return TrakHoundServiceResponse<TResult>.BadRequest("Client Not Specified");
            if (string.IsNullOrEmpty(query)) return TrakHoundServiceResponse<TResult>.BadRequest("Query Not Specified");
            if (resultIndex < 0) return TrakHoundServiceResponse<TResult>.BadRequest($"Invalid Result Index ({resultIndex})");

            return ReadResponse(await client.ExecuteQuery(query, timeout, parameters, routerId, cancellationToken), resultIndex, read);
        }

        private static TrakHoundServiceResponse<TResult> ReadResponse<TResult>(
            TrakHoundServiceResponse<TrakHoundTableResult[]> queryResponse,
            int? resultIndex,
            Func<IEnumerable<TrakHoundTableResult>, TrakHoundServiceResponse<TResult>> read
            )
        {
            var results = queryResponse.Result ?? Array.Empty<TrakHoundTableResult>();

            TrakHoundServiceResponse<TResult> response;
            if (!queryResponse.Success)
            {
                // Keep the Service's Status Code (ex. 404 Router Not Found, 503 Not Available)
                response = new TrakHoundServiceResponse<TResult>(default, queryResponse.StatusCode > 0 ? queryResponse.StatusCode : 500, queryResponse.Message);
            }
            else if (resultIndex.HasValue && results.Length > 0 && resultIndex.Value >= results.Length)
            {
                response = TrakHoundServiceResponse<TResult>.BadRequest($"Result Index {resultIndex} is out of range (the Query returned {results.Length} Result sets)");
            }
            else
            {
                var selected = resultIndex.HasValue && results.Length > 0 ? new[] { results[resultIndex.Value] } : results;

                try
                {
                    response = read(selected.Where(o => o != null));
                }
                catch (Exception ex) when (ex is FormatException || ex is InvalidOperationException)
                {
                    response = TrakHoundServiceResponse<TResult>.InternalError(ex.Message);
                }
            }

            response.RouterId = queryResponse.RouterId;
            response.ServiceId = queryResponse.ServiceId;
            response.Duration = queryResponse.Duration;
            return response;
        }
    }
}
