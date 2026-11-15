// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TrakHound.Services.MessageQueues
{
    /// <summary>
    /// Creates the Content Stream (and default Content Type) of a Message to Publish. Used by the Message Queue Clients
    /// </summary>
    public static class TrakHoundQueueContent
    {
        public const string TextContentType = "text/plain; charset=utf-8";
        public const string JsonContentType = "application/json";


        /// <summary>
        /// Creates Content from bytes (an empty array is an empty Message). Returns null if content is null
        /// </summary>
        public static Stream FromBytes(byte[] content) => content != null ? new MemoryStream(content, 0, content.Length, false, true) : null;

        /// <summary>
        /// Creates Content from a UTF-8 string (an empty string is an empty Message). Returns null if content is null
        /// </summary>
        public static Stream FromString(string content) => content != null ? FromBytes(Encoding.UTF8.GetBytes(content)) : null;

        /// <summary>
        /// Creates Content from the object serialized as JSON. Returns null if content is null
        /// </summary>
        public static Stream FromJson(object content) => content != null ? FromBytes(JsonSerializer.SerializeToUtf8Bytes(content, Json.SharedOptions)) : null;

        /// <summary>
        /// Formats Headers as name:value (how the System API's publish endpoint receives them, one header query parameter each)
        /// </summary>
        public static IEnumerable<string> FormatHeaders(IReadOnlyDictionary<string, string> headers)
        {
            if (headers == null) return Enumerable.Empty<string>();
            return headers.Where(o => !string.IsNullOrEmpty(o.Key)).Select(o => $"{o.Key}:{o.Value}").ToList();
        }

        /// <summary>
        /// Parses Headers formatted as name:value (the value may contain ':'). Returns null if there are none
        /// </summary>
        public static Dictionary<string, string> ParseHeaders(IEnumerable<string> headers)
        {
            if (headers == null) return null;

            var results = new Dictionary<string, string>();
            foreach (var header in headers)
            {
                var i = header?.IndexOf(':') ?? -1;
                if (i > 0) results[header.Substring(0, i).Trim()] = header.Substring(i + 1).Trim();
            }

            return results.Count > 0 ? results : null;
        }

        /// <summary>
        /// Returns the options with the Content Type set to contentType if it isn't already set (a copy, so the caller's options are not changed)
        /// </summary>
        public static TrakHoundQueuePublishOptions WithDefaultContentType(TrakHoundQueuePublishOptions options, string contentType)
        {
            if (!string.IsNullOrEmpty(options?.ContentType)) return options;

            return new TrakHoundQueuePublishOptions
            {
                MessageId = options?.MessageId,
                ContentType = contentType,
                Headers = options?.Headers
            };
        }
    }
}
