// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text;
using TrakHound.Services.MessageQueues;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Queue Messages are sent by the System API as JSON (the Pull response and each Subscribe WebSocket message)
    /// </summary>
    public class QueueMessageSerializationTests
    {
        private static TrakHoundQueueMessage RoundTrip(TrakHoundQueueMessage message)
        {
            return Json.Convert<TrakHoundQueueMessage>(Encoding.UTF8.GetString(Json.ConvertToUtf8(message)));
        }


        [Fact]
        public void Binary_content_round_trips()
        {
            var content = new byte[] { 0xFF, 0x00, 0xC3, 0x28, 0x7B }; // Not valid UTF-8

            var message = RoundTrip(new TrakHoundQueueMessage { Queue = "orders", DeliveryId = "d", Content = content });

            Assert.Equal(content, message.Content);
        }

        [Fact]
        public void Empty_content_round_trips_as_empty()
        {
            var message = RoundTrip(new TrakHoundQueueMessage { Queue = "orders", DeliveryId = "d", Content = Array.Empty<byte>() });

            Assert.NotNull(message.Content);
            Assert.Empty(message.Content);
            Assert.True(message.IsValid);
        }

        [Fact]
        public void The_queue_and_properties_round_trip()
        {
            var message = RoundTrip(new TrakHoundQueueMessage
            {
                Queue = "orders",
                DeliveryId = "delivery-1",
                MessageId = "message-1",
                ContentType = "application/json",
                Headers = new Dictionary<string, string> { ["correlation-id"] = "abc" },
                Content = Encoding.UTF8.GetBytes("{}"),
                Redelivered = true,
                PublishedTimestamp = 1000,
                ReceivedTimestamp = 2000
            });

            Assert.Equal("orders", message.Queue);
            Assert.Equal("delivery-1", message.DeliveryId);
            Assert.Equal("message-1", message.MessageId);
            Assert.Equal("application/json", message.ContentType);
            Assert.Equal("abc", message.Headers["correlation-id"]);
            Assert.True(message.Redelivered);
            Assert.Equal(1000, message.PublishedTimestamp);
            Assert.Equal(2000, message.ReceivedTimestamp);
            Assert.True(message.IsValid);
        }

        [Fact]
        public void A_list_of_messages_round_trips()
        {
            var json = Encoding.UTF8.GetString(Json.ConvertToUtf8(new[] { new TrakHoundQueueMessage { Queue = "a", Content = new byte[] { 1 } }, new TrakHoundQueueMessage { Queue = "b", Content = new byte[] { 2 } } }));

            var messages = Json.Convert<List<TrakHoundQueueMessage>>(json);

            Assert.Equal(new[] { "a", "b" }, messages.Select(o => o.Queue));
        }

        [Fact]
        public void String_content_defaults_to_text_and_keeps_a_set_content_type()
        {
            var defaulted = TrakHoundQueueContent.WithDefaultContentType(null, TrakHoundQueueContent.TextContentType);
            var kept = TrakHoundQueueContent.WithDefaultContentType(new TrakHoundQueuePublishOptions { ContentType = "text/csv" }, TrakHoundQueueContent.TextContentType);

            Assert.Equal(TrakHoundQueueContent.TextContentType, defaulted.ContentType);
            Assert.Equal("text/csv", kept.ContentType);
        }

        [Fact]
        public void Headers_round_trip_through_the_publish_query_format()
        {
            var headers = new Dictionary<string, string> { ["correlation-id"] = "abc:123", ["empty"] = "" };

            var parsed = TrakHoundQueueContent.ParseHeaders(TrakHoundQueueContent.FormatHeaders(headers));

            Assert.Equal("abc:123", parsed["correlation-id"]);
            Assert.Equal("", parsed["empty"]);
            Assert.Null(TrakHoundQueueContent.ParseHeaders(new[] { "no-separator", ":no-name" }));
        }

        [Fact]
        public void A_failed_delivery_result_is_written_with_success_false()
        {
            var json = Encoding.UTF8.GetString(Json.ConvertToUtf8(new TrakHoundQueueDeliveryResult("d", false, "Channel Closed")));

            Assert.Contains("\"success\":false", json);
        }

        [Fact]
        public void Empty_string_content_is_an_empty_message()
        {
            using var stream = TrakHoundQueueContent.FromString("");

            Assert.NotNull(stream);
            Assert.Equal(0, stream.Length);
            Assert.Null(TrakHoundQueueContent.FromString(null));
        }
    }
}
