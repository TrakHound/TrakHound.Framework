// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TrakHound.Http
{
    /// <summary>
    /// Response returned from an HTTP request, containing the Status Code, Headers, and Content
    /// </summary>
    public struct TrakHoundHttpResponse
    {
        /// <summary>
        /// HTTP Status Code of the response. 0 if the request failed before a response was received (see <see cref="Message"/>)
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Media Type of the response Content (ex. application/json). Does not include parameters such as charset (see <see cref="ContentHeaders"/>)
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Body of the response. Null if the response had no content
        /// </summary>
        public Stream Content { get; set; }

        /// <summary>
        /// Headers of the response (excluding Content headers, see <see cref="ContentHeaders"/>). Null if the response was not created from an <see cref="HttpResponseMessage"/>
        /// </summary>
        public HttpResponseHeaders Headers { get; set; }

        /// <summary>
        /// Content headers of the response (ex. Content-Type, Content-Length, Content-Disposition). Null if the response was not created from an <see cref="HttpResponseMessage"/>
        /// </summary>
        public HttpContentHeaders ContentHeaders { get; set; }

        /// <summary>
        /// Error message. Set to the Reason Phrase for non-success Status Codes, or the Exception message if the request failed. Null otherwise
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// True if the <see cref="StatusCode"/> is between 200 and 399
        /// </summary>
        public bool Success => StatusCode >= 200 && StatusCode < 400;


        /// <summary>
        /// Create a new response from an <see cref="HttpResponseMessage"/>
        /// </summary>
        /// <param name="responseMessage">Response message to read the Status Code and Headers from</param>
        /// <param name="content">Body of the response</param>
        public TrakHoundHttpResponse(HttpResponseMessage responseMessage, Stream content)
        {
            StatusCode = (int)responseMessage.StatusCode;
            ContentType = responseMessage.Content.Headers.ContentType?.MediaType;
            Content = content;
            Headers = responseMessage.Headers;
            ContentHeaders = responseMessage.Content.Headers;
            Message = !responseMessage.IsSuccessStatusCode ? responseMessage.ReasonPhrase : null;
        }

        /// <summary>
        /// Create a new response with the specified Status Code, Content Type, and Content
        /// </summary>
        /// <param name="statusCode">HTTP Status Code of the response</param>
        /// <param name="contentType">Media Type of the response Content (ex. application/json)</param>
        /// <param name="content">Body of the response</param>
        public TrakHoundHttpResponse(int statusCode, string contentType, Stream content)
        {
            StatusCode = statusCode;
            ContentType = contentType;
            Content = content;
            Headers = null;
            ContentHeaders = null;
            Message = null;
        }


        /// <summary>
        /// Read the <see cref="Content"/> as a byte array. A <see cref="MemoryStream"/> can be read multiple times, any other Stream is disposed after it is read. Null if there is no Content
        /// </summary>
        public byte[] GetContentBytes()
        {
            if (Content != null)
            {
                // Read MemoryStream content without disposing so it can be read multiple times
                if (Content is MemoryStream memoryStream)
                {
                    return memoryStream.ToArray();
                }

                return GetContentBytes(Content);
            }

            return null;
        }

        /// <summary>
        /// Read the <see cref="Content"/> as a UTF-8 string. Null if there is no Content
        /// </summary>
        public string GetContentUtf8String()
        {
            var contentBytes = GetContentBytes();
            return contentBytes != null ? Encoding.UTF8.GetString(contentBytes) : null;
        }

        /// <summary>
        /// Read the <see cref="Content"/> as a Base64 encoded string. Null if there is no Content
        /// </summary>
        public string GetContentBase64String()
        {
            var contentBytes = GetContentBytes();
            return contentBytes != null ? Convert.ToBase64String(contentBytes) : null;
        }

        /// <summary>
        /// Read the <see cref="Content"/> as UTF-8 JSON and deserialize it to an object. Default if there is no Content or it is not valid JSON
        /// </summary>
        /// <typeparam name="TOutput">Type to deserialize the JSON content to</typeparam>
        public TOutput GetJsonContentObject<TOutput>()
        {
            if (Content != null)
            {
                try
                {
                    // Deserialize straight from the bytes (no intermediate string). A MemoryStream is read without changing it so it can be read multiple times
                    if (Content is MemoryStream memoryStream)
                    {
                        ReadOnlySpan<byte> json = memoryStream.TryGetBuffer(out var buffer) ? buffer.AsSpan() : memoryStream.ToArray();
                        if (!json.IsEmpty) return JsonSerializer.Deserialize<TOutput>(json, Json.SharedOptions);
                    }
                    else
                    {
                        var json = GetContentBytes(Content);
                        if (!json.IsNullOrEmpty()) return JsonSerializer.Deserialize<TOutput>(json, Json.SharedOptions);
                    }
                }
                catch { }
            }

            return default;
        }

        /// <summary>
        /// Returns the Status Code, Content Type, and Message of the response (ex. 404 : text/plain : Not Found)
        /// </summary>
        public override string ToString()
        {
            var output = StatusCode.ToString();
            if (!string.IsNullOrEmpty(ContentType)) output += $" : {ContentType}";
            if (!string.IsNullOrEmpty(Message)) output += $" : {Message}";
            return output;
        }


        /// <summary>
        /// Serialize an object to JSON and return it as a UTF-8 encoded Stream. Null if the object is null or serialization fails
        /// </summary>
        /// <param name="contentObject">Object to serialize</param>
        public static Stream GetJsonContentStream(object contentObject)
        {
            if (contentObject != null)
            {
                try
                {
                    var json = Json.Convert(contentObject);
                    var contentBytes = Encoding.UTF8.GetBytes(json);
                    return GetContentStream(contentBytes);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Return a string as a UTF-8 encoded Stream. Null if the string is null
        /// </summary>
        /// <param name="content">String to encode</param>
        public static Stream GetUtf8ContentStream(string content)
        {
            return content != null ? GetContentStream(Encoding.UTF8.GetBytes(content)) : null;
        }

        /// <summary>
        /// Return a byte array as a Stream. Null if the byte array is null
        /// </summary>
        /// <param name="contentBytes">Bytes to wrap in a Stream</param>
        public static Stream GetContentStream(byte[] contentBytes)
        {
            return contentBytes != null ? new MemoryStream(contentBytes) : null;
        }

        /// <summary>
        /// Read a Stream to a byte array. The Stream is always disposed after it is read. Null if the Stream is null or reading fails
        /// </summary>
        /// <param name="contentStream">Stream to read</param>
        public static byte[] GetContentBytes(Stream contentStream)
        {
            if (contentStream != null)
            {
                try
                {
                    using (var readStream = new MemoryStream())
                    {
                        contentStream.CopyTo(readStream);
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
        /// Decode a Base64 string and return it as a Stream. Null if the string is null, empty, or not valid Base64
        /// </summary>
        /// <param name="base64Content">Base64 encoded string to decode</param>
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
    }
}
