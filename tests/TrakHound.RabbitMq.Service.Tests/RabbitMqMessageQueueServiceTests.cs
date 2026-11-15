// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using RabbitMQ.Client;
using TrakHound.Services;
using TrakHound.Services.MessageQueues;

namespace TrakHound.RabbitMq.Service.Tests
{
    /// <summary>
    /// What only the RabbitMQ Service does (the behavior every Message Queue Service shares is in TrakHound.MessageQueues.Conformance.Tests).
    /// Runs when TRAKHOUND_RABBITMQ_TEST_SERVER is set (optionally with TRAKHOUND_RABBITMQ_TEST_PORT, TRAKHOUND_RABBITMQ_TEST_USERNAME
    /// and TRAKHOUND_RABBITMQ_TEST_PASSWORD). Each test uses its own Queues and deletes them, so any broker can be used
    /// </summary>
    public class RabbitMqMessageQueueServiceTests : IAsyncLifetime
    {
        private const string Prefix = "TRAKHOUND_RABBITMQ_TEST_";

        private readonly string _queue = $"trakhound-tests-{Guid.NewGuid():N}";
        private readonly List<string> _queues = new List<string>();
        private RabbitMqMessageQueueService _service;
        private ConnectionFactory _connectionFactory;


        public Task InitializeAsync()
        {
            _queues.Add(_queue);

            var server = Environment.GetEnvironmentVariable($"{Prefix}SERVER");
            if (!string.IsNullOrEmpty(server))
            {
                var port = Environment.GetEnvironmentVariable($"{Prefix}PORT");
                var username = Environment.GetEnvironmentVariable($"{Prefix}USERNAME");
                var password = Environment.GetEnvironmentVariable($"{Prefix}PASSWORD");

                var configuration = new TrakHoundServiceConfiguration { Id = "rabbitmq-tests", Name = "rabbitmq-tests" };
                configuration.SetParameter("hostname", server);
                configuration.SetParameter("port", port ?? "0");
                configuration.SetParameter("username", username);
                configuration.SetParameter("password", password);
                _service = new RabbitMqMessageQueueService(configuration);

                // A separate Connection to set up and delete Queues
                _connectionFactory = new ConnectionFactory { HostName = server };
                if (int.TryParse(port, out var p) && p > 0) _connectionFactory.Port = p;
                if (!string.IsNullOrEmpty(username)) _connectionFactory.UserName = username;
                if (!string.IsNullOrEmpty(password)) _connectionFactory.Password = password;
            }
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            if (_service != null)
            {
                _service.Dispose();

                await using var connection = await _connectionFactory.CreateConnectionAsync();
                await using var channel = await connection.CreateChannelAsync();
                foreach (var queue in _queues) await channel.QueueDeleteAsync(queue);
            }
        }

        private RabbitMqMessageQueueService Service
        {
            get
            {
                Skip.If(_service == null, $"Set {Prefix}SERVER to run the RabbitMQ tests");
                return _service;
            }
        }

        private async Task Publish(string content, string queue = null)
        {
            var response = await Service.Publish(queue ?? _queue, TrakHoundQueueContent.FromString(content));
            Assert.True(response.Success, response.Message);
        }


        [SkippableFact]
        public async Task A_message_the_broker_rejects_is_not_reported_as_published()
        {
            var queue = $"trakhound-tests-{Guid.NewGuid():N}";
            _queues.Add(queue);

            // A full Queue set to reject-publish makes the Broker nack the Message (only seen with publisher confirms)
            Skip.If(_service == null, $"Set {Prefix}SERVER to run the RabbitMQ tests");
            await using (var connection = await _connectionFactory.CreateConnectionAsync())
            await using (var channel = await connection.CreateChannelAsync())
            {
                await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object> { ["x-max-length"] = 1, ["x-overflow"] = "reject-publish" });
            }

            var first = await Service.Publish(queue, TrakHoundQueueContent.FromString("a"));
            var second = await Service.Publish(queue, TrakHoundQueueContent.FromString("b"));

            Assert.True(first.Success, first.Message);
            Assert.False(second.Success);
            Assert.Equal(500, second.StatusCode);
        }

        [SkippableFact]
        public async Task An_existing_queue_with_other_settings_is_used_as_it_is()
        {
            var queue = $"trakhound-tests-{Guid.NewGuid():N}";
            _queues.Add(queue);

            // Declared with an argument the Service doesn't use (declaring it again without it is refused by the Broker)
            Skip.If(_service == null, $"Set {Prefix}SERVER to run the RabbitMQ tests");
            await using (var connection = await _connectionFactory.CreateConnectionAsync())
            await using (var channel = await connection.CreateChannelAsync())
            {
                await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object> { ["x-max-length"] = 100 });
            }

            await Publish("a", queue);

            var response = await Service.Pull(queue, 10, true);
            Assert.True(response.Success, response.Message);
            Assert.Equal("a", Assert.Single(response.Result).GetContentUtf8String());
        }
    }
}
