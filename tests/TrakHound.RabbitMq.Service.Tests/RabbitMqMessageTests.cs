// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using RabbitMQ.Client;
using System.Text;

namespace TrakHound.RabbitMq.Service.Tests
{
    /// <summary>
    /// Delivery IDs and AMQP property conversion (no broker needed)
    /// </summary>
    public class RabbitMqMessageTests
    {
        [Fact]
        public void A_delivery_id_round_trips()
        {
            var connectionId = Guid.NewGuid().ToString();
            var deliveryId = RabbitMqMessageQueueService.CreateDeliveryId(connectionId, 3, 42);

            Assert.Equal(connectionId, RabbitMqMessageQueueService.GetConnectionId(deliveryId));
            Assert.Equal(3, RabbitMqMessageQueueService.GetChannelNumber(deliveryId));
            Assert.Equal(42UL, RabbitMqMessageQueueService.GetDeliveryTag(deliveryId));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-delivery-id")]
        public void An_invalid_delivery_id_has_no_delivery_tag(string deliveryId)
        {
            Assert.Null(RabbitMqMessageQueueService.GetConnectionId(deliveryId));
            Assert.Equal(0UL, RabbitMqMessageQueueService.GetDeliveryTag(deliveryId));
        }

        [Fact]
        public void Header_values_are_converted_to_strings()
        {
            var headers = RabbitMqMessageQueueService.ReadHeaders(new Dictionary<string, object>
            {
                ["text"] = Encoding.UTF8.GetBytes("héllo"),
                ["number"] = 1.5,
                ["flag"] = true,
                ["timestamp"] = new AmqpTimestamp(1700000000),
                ["empty"] = null
            });

            Assert.Equal("héllo", headers["text"]);
            Assert.Equal("1.5", headers["number"]);
            Assert.Equal("True", headers["flag"]);
            Assert.Equal("1700000000", headers["timestamp"]);
            Assert.Null(headers["empty"]);
        }

        [Fact]
        public void No_headers_is_null()
        {
            Assert.Null(RabbitMqMessageQueueService.ReadHeaders(null));
            Assert.Null(RabbitMqMessageQueueService.ReadHeaders(new Dictionary<string, object>()));
        }
    }
}
