// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text.Json.Serialization;

namespace TrakHound.Api
{
    /// <summary>
    /// Information describing a Response an Api Endpoint can return
    /// </summary>
    public class TrakHoundApiResponseInformation
    {
        /// <summary>
        /// The HTTP Status Code of the Response
        /// </summary>
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        /// <summary>
        /// The Content Type of the Response ("application/octet-stream" for byte[], "text/plain" for primitives, strings, and DateTime, otherwise "application/json")
        /// </summary>
        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }

        /// <summary>
        /// The Schema of the Response Content (only set for "application/json" Responses)
        /// </summary>
        [JsonPropertyName("schema")]
        public TrakHoundApiSchemaInformation Schema { get; set; }


        /// <summary>
        /// Creates the Information for a Response with the specified Status Code and Content Type. Returns null if the Type is null
        /// </summary>
        public static TrakHoundApiResponseInformation Create(int statusCode, Type objectType)
        {
            if (objectType != null)
            {
                var responseInformation = new TrakHoundApiResponseInformation();
                responseInformation.StatusCode = statusCode;

                if (objectType == typeof(byte[]))
                {
                    responseInformation.ContentType = "application/octet-stream";
                }
                else if (!objectType.IsPrimitive && objectType != typeof(string) && objectType != typeof(DateTime))
                {
                    responseInformation.ContentType = "application/json";
                    responseInformation.Schema = TrakHoundApiSchemaInformation.Create(objectType);
                }
                else
                {
                    responseInformation.ContentType = "text/plain";
                }

                return responseInformation;
            }

            return null;
        }
    }
}
