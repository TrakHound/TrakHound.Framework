// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TrakHound.Api
{
    /// <summary>
    /// A Response returned from an Api Endpoint
    /// </summary>
    public struct TrakHoundApiResponse
    {
        private const string _filenameParameterKey = "Filename";
        private const string _locationParameterKey = "Location";


        /// <summary>
        /// True if the Status Code indicates success (200-399)
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The HTTP Status Code of the Response
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// The MIME Content Type of the Content
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// The Content (body) of the Response
        /// </summary>
        public Stream Content { get; set; }

        /// <summary>
        /// Additional values used when sending the Response (ex. "Filename" for File Responses, "Location" for Redirects)
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; }


        /// <summary>
        /// Creates a Response with no Content
        /// </summary>
        public TrakHoundApiResponse(int statusCode, Dictionary<string, string> parameters = null)
        {
            StatusCode = statusCode;
            Parameters = parameters;
            Success = statusCode >= 200 && statusCode < 400;
        }

        /// <summary>
        /// Creates a Response with the specified Content bytes
        /// </summary>
        public TrakHoundApiResponse(int statusCode, byte[] content, string contentType = null, Dictionary<string, string> parameters = null)
        {
            StatusCode = statusCode;
            ContentType = contentType;
            Parameters = parameters;
            Success = statusCode >= 200 && statusCode < 400;

            if (content != null)
            {
                try
                {
                    Content = new MemoryStream(content);
                }
                catch { }
            }
        }

        /// <summary>
        /// Creates a Response with the specified Content Stream
        /// </summary>
        public TrakHoundApiResponse(int statusCode, Stream content, string contentType = null, Dictionary<string, string> parameters = null)
        {
            StatusCode = statusCode;
            Content = content;
            ContentType = contentType;
            Parameters = parameters;
            Success = statusCode >= 200 && statusCode < 400;
        }


        /// <summary>
        /// Returns true if a Status Code has been set (false for a default Response)
        /// </summary>
        public bool IsValid() => StatusCode != 0;


        /// <summary>
        /// Returns true if the specified Parameter is set
        /// </summary>
        public bool ParameterExists(string name)
        {
            if (!string.IsNullOrEmpty(name) && !Parameters.IsNullOrEmpty())
            {
                return Parameters.ContainsKey(name);
            }

            return false;
        }

        /// <summary>
        /// Gets the value of the specified Parameter (null if not set)
        /// </summary>
        public string GetParameter(string name)
        {
            if (!string.IsNullOrEmpty(name) && !Parameters.IsNullOrEmpty())
            {
                return Parameters.GetValueOrDefault(name);
            }

            return null;
        }

        /// <summary>
        /// Gets the value of the specified Parameter converted to T (default if not set or not convertible)
        /// </summary>
        public T GetParameter<T>(string name)
        {
            if (!string.IsNullOrEmpty(name) && !Parameters.IsNullOrEmpty())
            {
                var value = Parameters.GetValueOrDefault(name);
                if (value != null)
                {
                    try
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    catch { }
                }
            }

            return default;
        }


        /// <summary>
        /// Reads the Content into a byte array. The Content Stream is disposed, so it can only be read once
        /// </summary>
        public byte[] GetContentBytes()
        {
            if (Content != null)
            {
                return GetContentBytes(Content);
            }

            return null;
        }

        /// <summary>
        /// Reads the Content as a UTF-8 string. The Content Stream is disposed, so it can only be read once
        /// </summary>
        public string GetContentUtf8String()
        {
            var contentBytes = GetContentBytes();
            if (contentBytes != null)
            {
                try
                {
                    return Encoding.UTF8.GetString(contentBytes);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Reads the Content as a Base64 string. The Content Stream is disposed, so it can only be read once
        /// </summary>
        public string GetContentBase64String()
        {
            var contentBytes = GetContentBytes();
            if (contentBytes != null)
            {
                try
                {
                    return Convert.ToBase64String(contentBytes);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Reads the Content as JSON and deserializes it to TOutput. The Content Stream is disposed, so it can only be read once
        /// </summary>
        public TOutput GetJsonContentObject<TOutput>()
        {
            // Deserialize straight from the bytes (no intermediate string)
            var contentBytes = GetContentBytes();
            if (!contentBytes.IsNullOrEmpty())
            {
                try
                {
                    return JsonSerializer.Deserialize<TOutput>(contentBytes, Json.SharedOptions);
                }
                catch { }
            }

            return default;
        }


        /// <summary>
        /// Serializes the object to JSON and returns it as a UTF-8 Stream (null if the object is null)
        /// </summary>
        public static Stream GetJsonContentStream(object contentObject)
        {
            return GetContentStream(GetJsonBytes(contentObject));
        }

        // Serializes straight to UTF-8 (no intermediate string). Null if the object is null or serialization fails
        private static byte[] GetJsonBytes(object contentObject)
        {
            if (contentObject != null)
            {
                try
                {
                    return JsonSerializer.SerializeToUtf8Bytes(contentObject, Json.SharedOptions);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Returns the string as a UTF-8 Stream (null if the string is null)
        /// </summary>
        public static Stream GetUtf8ContentStream(string content)
        {
            if (content != null)
            {
                try
                {
                    var contentBytes = Encoding.UTF8.GetBytes(content);
                    return GetContentStream(contentBytes);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Returns the bytes as a MemoryStream (null if the bytes are null)
        /// </summary>
        public static Stream GetContentStream(byte[] contentBytes)
        {
            if (contentBytes != null)
            {
                try
                {
                    return new MemoryStream(contentBytes);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Reads the Stream into a byte array and disposes the Stream
        /// </summary>
        public static byte[] GetContentBytes(Stream contentStream)
        {
            if (contentStream != null)
            {
                try
                {
                    using (var readStream = new MemoryStream())
                    {
                        contentStream.CopyTo(readStream);
                        readStream.Seek(0, SeekOrigin.Begin);
                        
                        return readStream.ToArray();
                    }
                }
                catch { }
                finally
                {
                    contentStream.Dispose();
                }
            }

            return null;
        }

        /// <summary>
        /// Decodes a Base64 string into a Stream (null if empty or not valid Base64)
        /// </summary>
        public static Stream GetContentStreamFromBase64String(string base64Content)
        {
            if (!string.IsNullOrEmpty(base64Content))
            {
                try
                {
                    var contentBytes = Convert.FromBase64String(base64Content);
                    return GetContentStream(contentBytes);
                }
                catch { }
            }

            return null;
        }


        /// <summary>
        /// Creates a 200 (OK) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse Ok() => new TrakHoundApiResponse(200);
        /// <summary>
        /// Creates a 200 (OK) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse Ok(string message) => new TrakHoundApiResponse(200, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 200 (OK) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse Ok(object response) => new TrakHoundApiResponse(200, GetJsonBytes(response), "application/json");
        /// <summary>
        /// Creates a 200 (OK) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse Ok(byte[] content, string contentType) => new TrakHoundApiResponse(200, content, contentType);


        /// <summary>
        /// Creates a 201 (Created) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse Created() => new TrakHoundApiResponse(201);
        /// <summary>
        /// Creates a 201 (Created) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse Created(string message) => new TrakHoundApiResponse(201, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 201 (Created) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse Created(object response) => new TrakHoundApiResponse(201, GetJsonBytes(response), "application/json");
        /// <summary>
        /// Creates a 201 (Created) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse Created(byte[] content, string contentType) => new TrakHoundApiResponse(201, content, contentType);


        /// <summary>
        /// Creates a 202 (Accepted) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse Accepted() => new TrakHoundApiResponse(202);
        /// <summary>
        /// Creates a 202 (Accepted) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse Accepted(string message) => new TrakHoundApiResponse(202, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 202 (Accepted) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse Accepted(object response) => new TrakHoundApiResponse(202, GetJsonBytes(response), "application/json");
        /// <summary>
        /// Creates a 202 (Accepted) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse Accepted(byte[] content, string contentType) => new TrakHoundApiResponse(202, content, contentType);

        /// <summary>
        /// Creates a 200 (OK) Response that returns the Content as a file with the specified filename
        /// </summary>
        public static TrakHoundApiResponse File(byte[] content, string contentType, string filename)
        {
            var response = new TrakHoundApiResponse();
            response.Success = true;
            response.StatusCode = 200;
            response.ContentType = contentType;

            var parameters = new Dictionary<string, string>();
            parameters.Add(_filenameParameterKey, filename);
            response.Parameters = parameters;

            if (content != null)
            {
                try
                {
                    response.Content = new MemoryStream(content);
                }
                catch { }
            }

            return response;
        }

        /// <summary>
        /// Creates a 200 (OK) Response that returns the Content Stream as a file with the specified filename
        /// </summary>
        public static TrakHoundApiResponse File(Stream content, string contentType, string filename)
        {
            var response = new TrakHoundApiResponse();
            response.Success = true;
            response.StatusCode = 200;
            response.Content = content;
            response.ContentType = contentType;

            var parameters = new Dictionary<string, string>();
            parameters.Add(_filenameParameterKey, filename);
            response.Parameters = parameters;

            return response;
        }


        /// <summary>
        /// Creates a Redirect Response to the specified location (303 See Other by default)
        /// </summary>
        public static TrakHoundApiResponse Redirect(string location, int statusCode = 303)
        {
            var response = new TrakHoundApiResponse();
            response.Success = true;
            response.StatusCode = statusCode;

            var parameters = new Dictionary<string, string>();
            parameters.Add(_locationParameterKey, location);
            response.Parameters = parameters;

            return response;
        }


        /// <summary>
        /// Creates a 400 (Bad Request) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse BadRequest() => new TrakHoundApiResponse(400);
        /// <summary>
        /// Creates a 400 (Bad Request) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse BadRequest(string message) => new TrakHoundApiResponse(400, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 400 (Bad Request) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse BadRequest(object response) => new TrakHoundApiResponse(400, GetJsonBytes(response), "application/json");
        /// <summary>
        /// Creates a 400 (Bad Request) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse BadRequest(byte[] content, string contentType) => new TrakHoundApiResponse(400, content, contentType);


        /// <summary>
        /// Creates a 401 (Unauthorized) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse NotAuthorized() => new TrakHoundApiResponse(401);
        /// <summary>
        /// Creates a 401 (Unauthorized) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse NotAuthorized(string message) => new TrakHoundApiResponse(401, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 401 (Unauthorized) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse NotAuthorized(object response) => new TrakHoundApiResponse(401, GetJsonBytes(response), "application/json");
        /// <summary>
        /// Creates a 401 (Unauthorized) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse NotAuthorized(byte[] content, string contentType) => new TrakHoundApiResponse(401, content, contentType);


        /// <summary>
        /// Creates a 404 (Not Found) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse NotFound() => new TrakHoundApiResponse(404);
        /// <summary>
        /// Creates a 404 (Not Found) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse NotFound(string message) => new TrakHoundApiResponse(404, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 404 (Not Found) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse NotFound(object response) => new TrakHoundApiResponse(404, GetJsonBytes(response), "application/json");
        /// <summary>
        /// Creates a 404 (Not Found) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse NotFound(byte[] content, string contentType) => new TrakHoundApiResponse(404, content, contentType);


        /// <summary>
        /// Creates a 500 (Internal Server Error) Response with no Content
        /// </summary>
        public static TrakHoundApiResponse InternalError() => new TrakHoundApiResponse(500);
        /// <summary>
        /// Creates a 500 (Internal Server Error) Response with the message as "text/plain" Content
        /// </summary>
        public static TrakHoundApiResponse InternalError(string message) => new TrakHoundApiResponse(500, message.ToUtf8Bytes(), "text/plain");
        /// <summary>
        /// Creates a 500 (Internal Server Error) Response with the object serialized as "application/json" Content
        /// </summary>
        public static TrakHoundApiResponse InternalError(object response) => new TrakHoundApiResponse(500, GetJsonBytes(response), "application/json");
        /// <summary>
        /// Creates a 500 (Internal Server Error) Response with the specified Content and Content Type
        /// </summary>
        public static TrakHoundApiResponse InternalError(byte[] content, string contentType) => new TrakHoundApiResponse(500, content, contentType);
    }
}
