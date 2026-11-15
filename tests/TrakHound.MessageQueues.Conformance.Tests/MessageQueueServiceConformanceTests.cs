// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using TrakHound.Services.MessageQueues;

namespace TrakHound.MessageQueues.Conformance.Tests
{
    /// <summary>
    /// What every Message Queue Service must do. Each test uses its own Queues (from NewQueue), so a shared Broker can be used
    /// </summary>
    public abstract class MessageQueueServiceConformanceTests
    {
        private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);

        protected abstract ITrakHoundMessageQueueService Service { get; }

        /// <summary>
        /// Creates a Queue name for one test (the Service creates the Queue when it is first used). Subclasses delete them afterwards
        /// </summary>
        protected abstract string NewQueue();


        private async Task<string> Publish(string queue, string content, TrakHoundQueuePublishOptions options = null)
        {
            var response = await Service.Publish(queue, TrakHoundQueueContent.FromString(content), options);
            Assert.True(response.Success, response.Message);
            return response.Result.MessageId;
        }

        private async Task<List<TrakHoundQueueMessage>> Pull(string queue, int count = 100, bool autoAcknowledge = false)
        {
            var response = await Service.Pull(queue, count, autoAcknowledge);
            Assert.True(response.Success, response.Message);
            return response.Result.ToList();
        }

        private static async Task WaitFor(Func<bool> condition)
        {
            var timeout = DateTime.UtcNow + DeliveryTimeout;
            while (!condition() && DateTime.UtcNow < timeout) await Task.Delay(25);
        }

        private async Task<(ITrakHoundStream<TrakHoundQueueMessage> Stream, ConcurrentQueue<TrakHoundQueueMessage> Received)> Subscribe(string queue, bool autoAcknowledge, int prefetch = 0)
        {
            var response = await Service.Subscribe(queue, autoAcknowledge, prefetch);
            Assert.True(response.Success, response.Message);

            var received = new ConcurrentQueue<TrakHoundQueueMessage>();
            response.Result.Received += (sender, message) => received.Enqueue(message);
            response.Result.Start();
            return (response.Result, received);
        }


        [SkippableFact]
        public async Task The_service_is_available()
        {
            var response = await Service.CheckAvailability();

            Assert.True(response.Success, response.Message);
            Assert.True(response.Result.IsAvailable, response.Result.Message);
        }

        [SkippableFact]
        public async Task A_published_message_is_pulled_with_its_content_and_properties()
        {
            var queue = NewQueue();
            var content = new byte[] { 0xFF, 0x00, 0xC3, 0x28, 0x7B }; // Not valid UTF-8
            var options = new TrakHoundQueuePublishOptions
            {
                MessageId = "message-1",
                ContentType = "application/octet-stream",
                Headers = new Dictionary<string, string> { ["correlation-id"] = "abc:123" }
            };

            var publish = await Service.Publish(queue, TrakHoundQueueContent.FromBytes(content), options);
            Assert.True(publish.Success, publish.Message);
            Assert.Equal("message-1", publish.Result.MessageId);

            var message = Assert.Single(await Pull(queue, autoAcknowledge: true));
            Assert.Equal(queue, message.Queue);
            Assert.Equal(content, message.Content);
            Assert.Equal("message-1", message.MessageId);
            Assert.Equal("application/octet-stream", message.ContentType);
            Assert.Equal("abc:123", message.Headers["correlation-id"]);
            Assert.False(message.Redelivered);
            Assert.False(string.IsNullOrEmpty(message.DeliveryId));
            Assert.True(message.PublishedTimestamp > 0);
            Assert.True(message.ReceivedTimestamp > 0);
            Assert.True(message.IsValid);
        }

        [SkippableFact]
        public async Task Publish_creates_and_returns_a_message_id_when_not_set()
        {
            var queue = NewQueue();

            var messageId = await Publish(queue, "a");

            Assert.False(string.IsNullOrEmpty(messageId));
            Assert.Equal(messageId, Assert.Single(await Pull(queue, autoAcknowledge: true)).MessageId);
        }

        [SkippableFact]
        public async Task An_empty_message_is_delivered_with_empty_content()
        {
            var queue = NewQueue();
            await Publish(queue, "");

            var message = Assert.Single(await Pull(queue, autoAcknowledge: true));
            Assert.NotNull(message.Content);
            Assert.Empty(message.Content);
        }

        [SkippableFact]
        public async Task Pulling_an_empty_queue_returns_an_empty_list()
        {
            Assert.Empty(await Pull(NewQueue()));
        }

        [SkippableFact]
        public async Task Messages_are_pulled_in_order_up_to_the_count()
        {
            var queue = NewQueue();
            for (var i = 0; i < 5; i++) await Publish(queue, $"m{i}");

            var first = await Pull(queue, count: 3, autoAcknowledge: true);
            var rest = await Pull(queue, count: 10, autoAcknowledge: true);

            Assert.Equal(new[] { "m0", "m1", "m2" }, first.Select(o => o.GetContentUtf8String()));
            Assert.Equal(new[] { "m3", "m4" }, rest.Select(o => o.GetContentUtf8String()));
        }

        [SkippableFact]
        public async Task An_auto_acknowledged_message_is_removed_from_the_queue()
        {
            var queue = NewQueue();
            await Publish(queue, "a");

            Assert.Single(await Pull(queue, autoAcknowledge: true));
            Assert.Empty(await Pull(queue));
        }

        [SkippableFact]
        public async Task A_pulled_message_is_not_delivered_again_until_rejected()
        {
            var queue = NewQueue();
            await Publish(queue, "a");

            Assert.Single(await Pull(queue));
            Assert.Empty(await Pull(queue));
        }

        [SkippableFact]
        public async Task An_acknowledged_message_is_removed_from_the_queue()
        {
            var queue = NewQueue();
            await Publish(queue, "a");
            var message = Assert.Single(await Pull(queue));

            var response = await Service.Acknowledge(queue, new[] { message.DeliveryId });

            Assert.True(response.Success, response.Message);
            Assert.True(Assert.Single(response.Result).Success);
            Assert.Empty(await Pull(queue));
        }

        [SkippableFact]
        public async Task A_rejected_message_is_requeued_and_marked_redelivered()
        {
            var queue = NewQueue();
            await Publish(queue, "a");
            var message = Assert.Single(await Pull(queue));

            var response = await Service.Reject(queue, new[] { message.DeliveryId }, requeue: true);
            Assert.True(Assert.Single(response.Result).Success);

            var redelivered = Assert.Single(await Pull(queue, autoAcknowledge: true));
            Assert.True(redelivered.Redelivered);
            Assert.Equal("a", redelivered.GetContentUtf8String());
            Assert.Equal(message.MessageId, redelivered.MessageId);
            Assert.NotEqual(message.DeliveryId, redelivered.DeliveryId);
        }

        [SkippableFact]
        public async Task A_message_rejected_without_requeue_is_discarded()
        {
            var queue = NewQueue();
            await Publish(queue, "a");
            var message = Assert.Single(await Pull(queue));

            var response = await Service.Reject(queue, new[] { message.DeliveryId }, requeue: false);
            Assert.True(Assert.Single(response.Result).Success);

            Assert.Empty(await Pull(queue));
        }

        [SkippableFact]
        public async Task Each_delivery_id_gets_its_own_result()
        {
            var queue = NewQueue();
            await Publish(queue, "a");
            var message = Assert.Single(await Pull(queue));
            var unknown = $"{Guid.NewGuid()}:1:1";

            var response = await Service.Acknowledge(queue, new[] { message.DeliveryId, unknown });

            Assert.True(response.Success, response.Message);
            var results = response.Result.ToDictionary(o => o.DeliveryId);
            Assert.Equal(2, results.Count);
            Assert.True(results[message.DeliveryId].Success);
            Assert.False(results[unknown].Success);
            Assert.False(string.IsNullOrEmpty(results[unknown].Message));
        }

        [SkippableFact]
        public async Task Acknowledge_and_reject_need_a_queue_and_delivery_ids()
        {
            Assert.Equal(400, (await Service.Acknowledge(NewQueue(), Array.Empty<string>())).StatusCode);
            Assert.Equal(400, (await Service.Reject("", new[] { "x" })).StatusCode);
            Assert.Equal(400, (await Service.Pull("", 1, true)).StatusCode);
            Assert.Equal(400, (await Service.Publish("", TrakHoundQueueContent.FromString("a"))).StatusCode);
        }

        [SkippableFact]
        public async Task A_subscriber_receives_published_messages_in_order()
        {
            var queue = NewQueue();
            var (stream, received) = await Subscribe(queue, autoAcknowledge: true);
            using (stream)
            {
                await Task.Delay(SettleTime); // Let the Subscription start before publishing
                for (var i = 0; i < 5; i++) await Publish(queue, $"m{i}");

                await WaitFor(() => received.Count >= 5);
                Assert.Equal(new[] { "m0", "m1", "m2", "m3", "m4" }, received.Select(o => o.GetContentUtf8String()));
                Assert.All(received, o => Assert.Equal(queue, o.Queue));
            }

            Assert.Empty(await Pull(queue));
        }

        [SkippableFact]
        public async Task A_subscriber_receives_at_most_prefetch_unacknowledged_messages()
        {
            var queue = NewQueue();
            for (var i = 0; i < 5; i++) await Publish(queue, $"m{i}");

            var (stream, received) = await Subscribe(queue, autoAcknowledge: false, prefetch: 2);
            using (stream)
            {
                await WaitFor(() => received.Count >= 2);
                await Task.Delay(SettleTime);
                Assert.Equal(2, received.Count);

                // Acknowledging makes room for the next Messages
                var acknowledge = await Service.Acknowledge(queue, received.Select(o => o.DeliveryId).ToList());
                Assert.All(acknowledge.Result, o => Assert.True(o.Success, o.Message));

                await WaitFor(() => received.Count >= 4);
                await Task.Delay(SettleTime);
                Assert.Equal(4, received.Count);
            }
        }

        [SkippableFact]
        public async Task Disposing_a_subscription_returns_its_unacknowledged_messages()
        {
            var queue = NewQueue();
            await Publish(queue, "a");

            var (stream, received) = await Subscribe(queue, autoAcknowledge: false);
            await WaitFor(() => received.Count >= 1);
            stream.Dispose();

            List<TrakHoundQueueMessage> returned = null;
            await WaitFor(() => (returned = Pull(queue, autoAcknowledge: true).GetAwaiter().GetResult()).Count > 0);

            var message = Assert.Single(returned);
            Assert.Equal("a", message.GetContentUtf8String());
            Assert.True(message.Redelivered);
        }

        [SkippableFact]
        public async Task Each_message_is_delivered_to_one_subscriber()
        {
            var queue = NewQueue();
            var (first, firstReceived) = await Subscribe(queue, autoAcknowledge: true);
            var (second, secondReceived) = await Subscribe(queue, autoAcknowledge: true);
            using (first)
            using (second)
            {
                await Task.Delay(SettleTime);
                for (var i = 0; i < 10; i++) await Publish(queue, $"m{i}");

                await WaitFor(() => firstReceived.Count + secondReceived.Count >= 10);
                await Task.Delay(SettleTime);

                var all = firstReceived.Concat(secondReceived).Select(o => o.GetContentUtf8String()).OrderBy(o => o).ToList();
                Assert.Equal(Enumerable.Range(0, 10).Select(o => $"m{o}").OrderBy(o => o), all);
            }
        }

        [SkippableFact]
        public async Task Metrics_report_the_ready_messages_of_a_queue()
        {
            var queue = NewQueue();
            for (var i = 0; i < 3; i++) await Publish(queue, $"m{i}");

            // A Broker's statistics can lag behind, so wait for them
            TrakHoundMessageQueueMetrics metrics = null;
            await WaitFor(() =>
            {
                var response = Service.GetMetrics(new[] { queue }).GetAwaiter().GetResult();
                metrics = response.Result?.FirstOrDefault(o => o.Queue == queue);
                return metrics?.ReadyCount == 3;
            });

            Assert.NotNull(metrics);
            Assert.Equal(3, metrics.ReadyCount);
            Assert.True(metrics.MessageCount >= 3);
            Assert.True(metrics.Timestamp > 0);
        }
    }
}
