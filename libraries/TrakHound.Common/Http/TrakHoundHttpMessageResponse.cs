// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Text;
using TrakHound.Services.Messages;

namespace TrakHound.Http
{
    /// <summary>
    /// Creates and parses the binary frame used to send a Message Response over HTTP (a CR delimited header of Topic, Retain, QoS and Timestamp followed by the raw Content)
    /// </summary>
    public static class TrakHoundHttpMessageResponse
    {
        // Frame Format :
        // TOPIC\r RETAIN (0 or 1)\r QOS (0, 1, or 2)\r TIMESTAMP (UNIX ns)\r
        // followed by the Header Divider bytes 10, 13 (LF then CR) and then the raw Content bytes
        private static readonly byte[] _headerDividerBytes = new byte[] { 10, 13 };


        /// <summary>
        /// Creates the binary frame for the Response (null if the Topic is empty or contains CR/LF, or the Content is null)
        /// </summary>
        public static byte[] Create(TrakHoundMessageResponse response)
        {
            // The Header uses CR and LF as delimiters, so they can't appear in the Topic
            if (!string.IsNullOrEmpty(response.Topic) && response.Content != null && response.Topic.IndexOfAny(new[] { '\r', '\n' }) < 0)
            {
                var headers = new string[4];
                headers[0] = response.Topic;
                headers[1] = response.Retain ? "1" : "0";
                headers[2] = response.Qos.ToString();
                headers[3] = response.Timestamp.ToString();

                // Copy a MemoryStream's buffer straight into the frame (no intermediate array). The Stream isn't changed, so concurrent subscribers can frame the same Message
                if (response.Content is MemoryStream memoryStream && memoryStream.TryGetBuffer(out var buffer))
                {
                    return CreateResponseBytes(headers, buffer.AsSpan());
                }

                return CreateResponseBytes(headers, response.GetContentBytes() ?? Array.Empty<byte>());
            }

            return null;
        }

        private static byte[] CreateResponseBytes(string[] headers, ReadOnlySpan<byte> content)
        {
            var headerDividerBytes = _headerDividerBytes;

            var builder = new StringBuilder();
            for (var i = 0; i < headers.Length; i++)
            {
                builder.Append(headers[i]);
                builder.Append("\r");
            }
            var header = builder.ToString();
            var headerLength = Encoding.UTF8.GetByteCount(header);

            // Encode the Header and copy the Content directly into the frame
            var responseBytes = new byte[headerLength + headerDividerBytes.Length + content.Length];
            Encoding.UTF8.GetBytes(header, 0, header.Length, responseBytes, 0);
            Array.Copy(headerDividerBytes, 0, responseBytes, headerLength, headerDividerBytes.Length);
            content.CopyTo(responseBytes.AsSpan(headerLength + headerDividerBytes.Length));
            return responseBytes;
        }

        /// <summary>
        /// Parses a binary frame into a TrakHoundMessageResponse (an empty Response is returned if the frame could not be parsed)
        /// </summary>
        public static TrakHoundMessageResponse Parse(byte[] responseBytes)
        {
            var response = new TrakHoundMessageResponse();

            if (TrySplit(responseBytes, out var headers, out var contentBytes))
            {
                if (headers.Length > 3)
                {
                    response.Topic = headers[0];
                    response.Retain = headers[1] == "1";
                    response.Qos = headers[2].ToByte();
                    response.Timestamp = headers[3].ToLong();
                }

                response.Content = TrakHoundMessageResponse.GetContent(contentBytes);
            }

            return response;
        }


        /// <summary>
        /// Creates the binary frame sent to a Publish Stream. A null Topic, Retain or QoS is left empty so the Stream's default is used
        /// (null if the Topic contains CR/LF or the Content is null)
        /// </summary>
        public static byte[] CreatePublish(string topic, bool? retain, int? qos, byte[] content)
        {
            if (content == null) return null;
            if (topic != null && topic.IndexOfAny(new[] { '\r', '\n' }) >= 0) return null;

            var headers = new string[4];
            headers[0] = topic ?? "";
            headers[1] = retain.HasValue ? (retain.Value ? "1" : "0") : "";
            headers[2] = qos.HasValue ? qos.Value.ToString() : "";
            headers[3] = ""; // The Message Service sets the Timestamp when it publishes

            return CreateResponseBytes(headers, content);
        }

        /// <summary>
        /// Parses a frame received by a Publish Stream. Empty Topic, Retain and QoS fields use the defaults. Returns false (with the reason in error) when the frame isn't a valid Message
        /// </summary>
        public static bool TryParsePublish(byte[] frame, string defaultTopic, bool defaultRetain, int defaultQos, out TrakHoundMessageResponse message, out string error)
        {
            message = new TrakHoundMessageResponse();
            error = null;

            if (!TrySplit(frame, out var headers, out var contentBytes) || headers.Length < 3)
            {
                error = "Invalid Message Frame : Expected TOPIC\\rRETAIN\\rQOS\\rTIMESTAMP\\r followed by LF CR and the Content";
                return false;
            }

            var topic = !string.IsNullOrEmpty(headers[0]) ? headers[0] : defaultTopic;
            if (string.IsNullOrEmpty(topic))
            {
                error = "No Topic Specified";
                return false;
            }

            var retain = defaultRetain;
            switch (headers[1])
            {
                case "": break;
                case "1": retain = true; break;
                case "0": retain = false; break;
                default:
                    error = $"Invalid Retain ({headers[1]}) : Must be 0 or 1";
                    message.Topic = topic;
                    return false;
            }

            var qos = defaultQos;
            if (headers[2] != "" && (!int.TryParse(headers[2], out qos) || qos < 0 || qos > 2))
            {
                error = $"Invalid QoS ({headers[2]}) : Must be 0, 1, or 2";
                message.Topic = topic;
                return false;
            }

            message.Topic = topic;
            message.Retain = retain;
            message.Qos = (byte)qos;
            message.Content = TrakHoundMessageResponse.GetContent(contentBytes);
            return true;
        }


        /// <summary>
        /// Splits a frame into its CR delimited header fields and its Content (false if the frame has no Header Divider)
        /// </summary>
        private static bool TrySplit(byte[] frame, out string[] headers, out byte[] content)
        {
            headers = null;
            content = null;

            if (frame != null)
            {
                var headerIndex = 0;

                var i = 0;
                while (i < frame.Length - 1)
                {
                    if (frame[i] == 10 && frame[i + 1] == 13)
                    {
                        headerIndex = i;
                        break;
                    }

                    i++;
                }

                if (headerIndex > 0)
                {
                    var headerBytes = new byte[headerIndex];
                    Array.Copy(frame, 0, headerBytes, 0, headerIndex);
                    headers = StringFunctions.GetUtf8String(headerBytes).Split("\r");

                    var contentLength = frame.Length - headerIndex - 2; // 2 = Header Divider bytes (LF then CR)
                    content = new byte[contentLength];
                    Array.Copy(frame, headerIndex + 2, content, 0, contentLength);
                    return true;
                }
            }

            return false;
        }
    }
}
