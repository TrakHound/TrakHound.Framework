// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace TrakHound.Api
{
    /// <summary>
    /// A JSON serializable form of a TrakHoundApiResponse (the Content is Base64 encoded)
    /// </summary>
    public class TrakHoundApiJsonResponse
    {
        /// <summary>
        /// The HTTP Status Code of the Response
        /// </summary>
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        /// <summary>
        /// The MIME Content Type of the Content
        /// </summary>
        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }

        /// <summary>
        /// The Content of the Response as a Base64 string
        /// </summary>
        [JsonPropertyName("content")]
        public string Content { get; set; }

        /// <summary>
        /// Additional values used when sending the Response (ex. "Filename" or "Location")
        /// </summary>
        [JsonPropertyName("parameters")]
        public Dictionary<string, string> Parameters { get; set; }


        /// <summary>
        /// Creates an empty Response
        /// </summary>
        public TrakHoundApiJsonResponse() { }

        /// <summary>
        /// Creates a JSON Response from a TrakHoundApiResponse (reads and disposes its Content Stream)
        /// </summary>
        public TrakHoundApiJsonResponse(TrakHoundApiResponse response)
        {
            StatusCode = response.StatusCode;
            ContentType = response.ContentType;
            Parameters = response.Parameters;
            Content = response.GetContentBase64String();
        }

        /// <summary>
        /// Converts back to a TrakHoundApiResponse (decodes the Base64 Content)
        /// </summary>
        public TrakHoundApiResponse ToResponse()
        {
            var response = new TrakHoundApiResponse();
            response.StatusCode = StatusCode;
            response.ContentType = ContentType;
            response.Parameters = Parameters;
            response.Content = TrakHoundApiResponse.GetContentStreamFromBase64String(Content);
            return response;
        }


        /// <summary>
        /// Creates a 200 (OK) Response with the content serialized as "application/json" (no Content if serialization returns empty)
        /// </summary>
        public static TrakHoundApiResponse Ok(object content, bool indentOutput = true)
        {
            var json = Json.Convert(content, indented: indentOutput);
            if (!string.IsNullOrEmpty(json))
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                return new TrakHoundApiResponse(200, bytes, "application/json");
            }

            return new TrakHoundApiResponse(200);
        }
    }
}
