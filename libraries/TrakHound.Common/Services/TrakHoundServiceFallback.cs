// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections;
using System.Collections.Generic;

namespace TrakHound.Services
{
    /// <summary>
    /// Decides when a Router tries the next Target of a Route. Each Target lists the results it falls back on
    /// (<see cref="TrakHoundServiceTargetConfiguration.FallbackOn"/>), using the names of <see cref="TrakHoundServiceResultType"/> (ex. "NotFound")
    /// </summary>
    public static class TrakHoundServiceFallback
    {
        /// <summary>
        /// The results a Target falls back on when it doesn't list any: the Service didn't have it, or couldn't answer.
        /// Requests that were wrong or not allowed (ex. Bad Request or Forbidden) are not sent to the next Target
        /// </summary>
        public static readonly IReadOnlyList<TrakHoundServiceResultType> Default = new[]
        {
            TrakHoundServiceResultType.NotFound,
            TrakHoundServiceResultType.NotAvailable,
            TrakHoundServiceResultType.Timeout,
            TrakHoundServiceResultType.InternalError
        };

        /// <summary>
        /// The results a Target can fall back on (Ok and Route Not Configured are not included: a Target that doesn't handle the Request is always skipped)
        /// </summary>
        public static readonly IReadOnlyList<TrakHoundServiceResultType> Options = new[]
        {
            TrakHoundServiceResultType.NotFound,
            TrakHoundServiceResultType.NotAvailable,
            TrakHoundServiceResultType.Timeout,
            TrakHoundServiceResultType.InternalError,
            TrakHoundServiceResultType.BadRequest,
            TrakHoundServiceResultType.Empty
        };


        /// <summary>
        /// The Result Type of a Status Code (null if it doesn't have one, ex. 401 Unauthorized)
        /// </summary>
        public static TrakHoundServiceResultType? GetResultType(int statusCode)
        {
            switch (statusCode)
            {
                case 400: return TrakHoundServiceResultType.BadRequest;
                case 404: return TrakHoundServiceResultType.NotFound;
                case 405: return TrakHoundServiceResultType.RouteNotConfigured;
                case 408: return TrakHoundServiceResultType.Timeout;
                case 503: return TrakHoundServiceResultType.NotAvailable;
                case 504: return TrakHoundServiceResultType.Timeout;
            }

            if (statusCode >= 200 && statusCode < 400) return TrakHoundServiceResultType.Ok;
            if (statusCode >= 500) return TrakHoundServiceResultType.InternalError;
            return null;
        }

        /// <summary>
        /// The Result Types the Target falls back on (the Default if it doesn't list any). Unknown names are ignored, and Status Codes (ex. "404") are also accepted
        /// </summary>
        public static HashSet<TrakHoundServiceResultType> GetFallbackOn(TrakHoundServiceTargetConfiguration target)
        {
            var conditions = target?.FallbackOn;
            if (conditions == null) return new HashSet<TrakHoundServiceResultType>(Default);

            var resultTypes = new HashSet<TrakHoundServiceResultType>();
            foreach (var condition in conditions)
            {
                var value = condition?.Trim();
                if (string.IsNullOrEmpty(value)) continue;

                if (int.TryParse(value, out var statusCode))
                {
                    var resultType = GetResultType(statusCode);
                    if (resultType.HasValue) resultTypes.Add(resultType.Value);
                }
                else if (Enum.TryParse<TrakHoundServiceResultType>(value, true, out var resultType))
                {
                    resultTypes.Add(resultType);
                }
            }
            return resultTypes;
        }

        /// <summary>
        /// Whether a failed Response from the Target sends the Request on to the next Target
        /// </summary>
        public static bool IsFallback(TrakHoundServiceTargetConfiguration target, int statusCode)
        {
            var resultType = GetResultType(statusCode);
            return resultType.HasValue && resultType != TrakHoundServiceResultType.Ok && GetFallbackOn(target).Contains(resultType.Value);
        }

        /// <summary>
        /// Whether a successful Result from the Target sends the Request on to the next Target (the Target falls back on Empty and the Result is empty)
        /// </summary>
        public static bool IsEmptyFallback<TResult>(TrakHoundServiceTargetConfiguration target, TResult result)
        {
            return IsEmpty(result) && GetFallbackOn(target).Contains(TrakHoundServiceResultType.Empty);
        }

        /// <summary>
        /// Whether the Result is empty: null, or a collection with no items. Other values (including streams and queries that haven't run) are not empty,
        /// since reading them to check would use them up
        /// </summary>
        public static bool IsEmpty<TResult>(TResult result)
        {
            if (result == null) return true;
            if (result is string) return false;
            if (result is ICollection collection) return collection.Count == 0;
            return false;
        }
    }
}
