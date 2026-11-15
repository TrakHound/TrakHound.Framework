// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Services
{
    /// <summary>
    /// The Response returned from a Service Request
    /// </summary>
    public struct TrakHoundServiceResponse
    {
        /// <summary>
        /// True if the StatusCode is a success (200 - 399)
        /// </summary>
        public bool Success => StatusCode >= 200 && StatusCode < 400;

        /// <summary>
        /// An HTTP style Status Code (ex. 200 = Ok, 404 = Not Found, 500 = Internal Error)
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// A message describing the result (ex. an error message)
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// How long the Request took
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// The ID of the Router that handled the Request
        /// </summary>
        public string RouterId { get; set; }

        /// <summary>
        /// The ID of the Service that handled the Request
        /// </summary>
        public string ServiceId { get; set; }


        /// <summary>
        /// Creates a Response with the specified Status Code, message and duration
        /// </summary>
        public TrakHoundServiceResponse(int statusCode = 0, string message = null, TimeSpan? duration = null)
        {
            StatusCode = statusCode;
            Message = message;
            if (duration.HasValue) Duration = duration.Value;
        }

        /// <summary>
        /// Creates a successful Response (200)
        /// </summary>
        public static TrakHoundServiceResponse Ok(string message = null)
        {
            return new TrakHoundServiceResponse(statusCode: 200, message: message);
        }

        /// <summary>
        /// Creates a Bad Request Response (400)
        /// </summary>
        public static TrakHoundServiceResponse BadRequest(string message = null)
        {
            return new TrakHoundServiceResponse(statusCode: 400, message: message);
        }

        /// <summary>
        /// Creates a Not Found Response (404)
        /// </summary>
        public static TrakHoundServiceResponse NotFound(string message = null)
        {
            return new TrakHoundServiceResponse(statusCode: 404, message: message);
        }

        /// <summary>
        /// Creates a Not Available Response (503). The Service (or what it connects to) is not available
        /// </summary>
        public static TrakHoundServiceResponse NotAvailable(string message = null)
        {
            return new TrakHoundServiceResponse(statusCode: 503, message: message);
        }

        /// <summary>
        /// Creates a Conflict Response (409)
        /// </summary>
        public static TrakHoundServiceResponse Conflict(string message = null)
        {
            return new TrakHoundServiceResponse(statusCode: 409, message: message);
        }

        /// <summary>
        /// Creates an Unauthorized Response (401). The Request requires an Identity
        /// </summary>
        public static TrakHoundServiceResponse Unauthorized(string message = null)
        {
            return new TrakHoundServiceResponse(statusCode: 401, message: message);
        }

        /// <summary>
        /// Creates a Forbidden Response (403). The Identity does not have access
        /// </summary>
        public static TrakHoundServiceResponse Forbidden(string message = null)
        {
            return new TrakHoundServiceResponse(statusCode: 403, message: message);
        }

        /// <summary>
        /// Creates a Route Not Configured Response (405)
        /// </summary>
        public static TrakHoundServiceResponse RouteNotConfigured(string message = null)
        {
            return new TrakHoundServiceResponse(statusCode: 405, message: message);
        }

        /// <summary>
        /// Creates an Internal Error Response (500)
        /// </summary>
        public static TrakHoundServiceResponse InternalError(string message = null)
        {
            return new TrakHoundServiceResponse(statusCode: 500, message: message);
        }

        /// <summary>
        /// Creates a Timeout Response (504). The Service (or what it connects to) did not respond in time
        /// </summary>
        public static TrakHoundServiceResponse Timeout(string message = null)
        {
            return new TrakHoundServiceResponse(statusCode: 504, message: message);
        }
    }

    /// <summary>
    /// The Response returned from a Service Request that returns a Result
    /// </summary>
    public struct TrakHoundServiceResponse<TResult>
    {
        /// <summary>
        /// True if the StatusCode is a success (200 - 399)
        /// </summary>
        public bool Success => StatusCode >= 200 && StatusCode < 400;

        /// <summary>
        /// The Result returned by the Service (default if the Request did not succeed)
        /// </summary>
        public TResult Result { get; set; }

        /// <summary>
        /// An HTTP style Status Code (ex. 200 = Ok, 404 = Not Found, 500 = Internal Error)
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// A message describing the result (ex. an error message)
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// How long the Request took
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// The ID of the Router that handled the Request
        /// </summary>
        public string RouterId { get; set; }

        /// <summary>
        /// The ID of the Service that handled the Request
        /// </summary>
        public string ServiceId { get; set; }


        /// <summary>
        /// Creates a Response with the specified Result, Status Code, message and duration
        /// </summary>
        public TrakHoundServiceResponse(TResult result = default, int statusCode = 0, string message = null, TimeSpan? duration = null)
        {
            Result = result;
            StatusCode = statusCode;
            Message = message;
            if (duration.HasValue) Duration = duration.Value;
        }

        /// <summary>
        /// Creates a successful Response (200) with the specified Result
        /// </summary>
        public static TrakHoundServiceResponse<TResult> Ok(TResult result, string message = null)
        {
            return new TrakHoundServiceResponse<TResult>(result, statusCode: 200, message: message);
        }

        /// <summary>
        /// Creates a Bad Request Response (400)
        /// </summary>
        public static TrakHoundServiceResponse<TResult> BadRequest(string message = null)
        {
            return new TrakHoundServiceResponse<TResult>(statusCode: 400, message: message);
        }

        /// <summary>
        /// Creates a Not Found Response (404)
        /// </summary>
        public static TrakHoundServiceResponse<TResult> NotFound(string message = null)
        {
            return new TrakHoundServiceResponse<TResult>(statusCode: 404, message: message);
        }

        /// <summary>
        /// Creates a Not Available Response (503). The Service (or what it connects to) is not available
        /// </summary>
        public static TrakHoundServiceResponse<TResult> NotAvailable(string message = null)
        {
            return new TrakHoundServiceResponse<TResult>(statusCode: 503, message: message);
        }

        /// <summary>
        /// Creates a Conflict Response (409)
        /// </summary>
        public static TrakHoundServiceResponse<TResult> Conflict(string message = null)
        {
            return new TrakHoundServiceResponse<TResult>(statusCode: 409, message: message);
        }

        /// <summary>
        /// Creates an Unauthorized Response (401). The Request requires an Identity
        /// </summary>
        public static TrakHoundServiceResponse<TResult> Unauthorized(string message = null)
        {
            return new TrakHoundServiceResponse<TResult>(statusCode: 401, message: message);
        }

        /// <summary>
        /// Creates a Forbidden Response (403). The Identity does not have access
        /// </summary>
        public static TrakHoundServiceResponse<TResult> Forbidden(string message = null)
        {
            return new TrakHoundServiceResponse<TResult>(statusCode: 403, message: message);
        }

        /// <summary>
        /// Creates a Route Not Configured Response (405)
        /// </summary>
        public static TrakHoundServiceResponse<TResult> RouteNotConfigured(string message = null)
        {
            return new TrakHoundServiceResponse<TResult>(statusCode: 405, message: message);
        }

        /// <summary>
        /// Creates an Internal Error Response (500)
        /// </summary>
        public static TrakHoundServiceResponse<TResult> InternalError(string message = null)
        {
            return new TrakHoundServiceResponse<TResult>(statusCode: 500, message: message);
        }

        /// <summary>
        /// Creates a Timeout Response (504). The Service (or what it connects to) did not respond in time
        /// </summary>
        public static TrakHoundServiceResponse<TResult> Timeout(string message = null)
        {
            return new TrakHoundServiceResponse<TResult>(statusCode: 504, message: message);
        }
    }
}
