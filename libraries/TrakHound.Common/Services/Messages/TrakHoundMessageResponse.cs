// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.IO;
using System.Text;
using System.Text.Json;

namespace TrakHound.Services.Messages
{
    /// <summary>
    /// A Message received from the Message Service
    /// </summary>
    public struct TrakHoundMessageResponse
    {
        /// <summary>
        /// True if the Message has a Topic, Content, and Timestamp
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Topic) && Content != null && Timestamp > 0;

        /// <summary>
        /// The Topic the Message was published on
        /// </summary>
        public string Topic { get; set; }

        /// <summary>
        /// The Content of the Message
        /// </summary>
        public Stream Content { get; set; }

        /// <summary>
        /// True if the Message is (or was published as) the Topic's Retained Message
        /// </summary>
        public bool Retain { get; set; }

        /// <summary>
        /// The QoS level of the Message (0, 1, or 2)
        /// </summary>
        public byte Qos { get; set; }

        /// <summary>
        /// The time the Message was published (Unix nanoseconds)
        /// </summary>
        public long Timestamp { get; set; }


        /// <summary>
        /// Reads the full Content as a byte array (null if there is no Content)
        /// </summary>
        public byte[] GetContentBytes() => GetContentBytes(Content);

        /// <summary>
        /// Reads the full Content as a UTF-8 string (null if there is no Content)
        /// </summary>
        public string GetContentUtf8String() => GetContentUtf8String(Content);

        /// <summary>
        /// Reads the Content and deserializes it from JSON to TOutput
        /// </summary>
        public TOutput GetJsonContentObject<TOutput>()
        {
            return Json.Convert<TOutput>(GetContentUtf8String());
        }

        /// <summary>
        /// Reads the full Content. A <see cref="MemoryStream"/> is read without changing its Position, so the same
        /// Message can safely be read by multiple consumers (including concurrently).
        /// </summary>
        public static byte[] GetContentBytes(Stream contentStream)
        {
            if (contentStream != null)
            {
                try
                {
                    if (contentStream is MemoryStream memoryStream)
                    {
                        // ToArray() copies the whole buffer regardless of Position and doesn't modify the stream
                        return memoryStream.ToArray();
                    }

                    if (contentStream.CanSeek && contentStream.Position != 0) contentStream.Seek(0, SeekOrigin.Begin);

                    using (var readStream = new MemoryStream())
                    {
                        contentStream.CopyTo(readStream);
                        return readStream.ToArray();
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Reads the full Content as a UTF-8 string (null if the Stream is null or cannot be read)
        /// </summary>
        public static string GetContentUtf8String(Stream contentStream)
        {
            var contentBytes = GetContentBytes(contentStream);
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
        /// Creates Content from a UTF-8 string. An empty string creates empty Content (ex. to clear a Retained Message). Returns null if <paramref name="content"/> is null.
        /// </summary>
        public static Stream GetContent(string content)
        {
            if (content != null)
            {
                try
                {
                    return GetContent(System.Text.Encoding.UTF8.GetBytes(content));
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Creates Content from the object serialized as JSON. Returns null if contentJson is null
        /// </summary>
        public static Stream GetContent(object contentJson, bool indented = false)
        {
            if (contentJson != null)
            {
                try
                {
                    // Serialized straight to UTF-8 (no intermediate string)
                    var options = indented ? IndentedOptions : Json.SharedOptions;
                    return GetContent(JsonSerializer.SerializeToUtf8Bytes(contentJson, options));
                }
                catch { }
            }

            return null;
        }

        // Cached because System.Text.Json caches Type metadata per options instance
        private static JsonSerializerOptions _indentedOptions;
        private static JsonSerializerOptions IndentedOptions => _indentedOptions ??= CreateIndentedOptions();

        private static JsonSerializerOptions CreateIndentedOptions()
        {
            var options = new JsonSerializerOptions(Json.SharedOptions) { WriteIndented = true };
            options.MakeReadOnly();
            return options;
        }

        /// <summary>
        /// Creates Content from bytes. An empty array creates empty Content (ex. to clear a Retained Message). Returns null if <paramref name="content"/> is null.
        /// </summary>
        public static Stream GetContent(byte[] content)
        {
            if (content != null)
            {
                try
                {
                    // Publicly visible so the buffer can be read with TryGetBuffer (no copy) when the Message is framed
                    return new MemoryStream(content, 0, content.Length, true, true);
                }
                catch { }
            }

            return null;
        }
    }
}
