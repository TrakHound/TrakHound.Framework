// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using RabbitMQ.Client;
using TrakHound.Memory.Service;
using TrakHound.RabbitMq.Service;
using TrakHound.Services;
using TrakHound.Services.MessageQueues;

namespace TrakHound.MessageQueues.Conformance.Tests
{
    /// <summary>
    /// The memory Service always runs, since it needs no setup
    /// </summary>
    public class MemoryConformanceTests : MessageQueueServiceConformanceTests, IDisposable
    {
        private readonly MemoryMessageQueueService _service = new MemoryMessageQueueService(new TrakHoundServiceConfiguration { Id = "memory-conformance", Name = "memory-conformance" });

        protected override ITrakHoundMessageQueueService Service => _service;

        protected override string NewQueue() => $"conformance-{Guid.NewGuid():N}";

        public void Dispose() => _service.Dispose();
    }


    /// <summary>
    /// Runs when TRAKHOUND_RABBITMQ_TEST_SERVER is set (with TRAKHOUND_RABBITMQ_TEST_PORT, _USERNAME and _PASSWORD if needed).
    /// Each test's Queues are deleted afterwards, so any Broker can be used
    /// </summary>
    public class RabbitMqConformanceTests : MessageQueueServiceConformanceTests, IAsyncLifetime
    {
        public const string Prefix = "TRAKHOUND_RABBITMQ_TEST_";

        private readonly List<string> _queues = new List<string>();
        private readonly RabbitMqMessageQueueService _service;
        private readonly ConnectionFactory _connectionFactory;

        protected override ITrakHoundMessageQueueService Service => _service ?? throw new SkipException($"Set {Prefix}SERVER to run the RabbitMQ tests");


        public RabbitMqConformanceTests()
        {
            var server = Environment.GetEnvironmentVariable($"{Prefix}SERVER");
            if (string.IsNullOrEmpty(server)) return;

            var port = Environment.GetEnvironmentVariable($"{Prefix}PORT");
            var username = Environment.GetEnvironmentVariable($"{Prefix}USERNAME");
            var password = Environment.GetEnvironmentVariable($"{Prefix}PASSWORD");

            var configuration = new TrakHoundServiceConfiguration { Id = "rabbitmq-conformance", Name = "rabbitmq-conformance" };
            configuration.SetParameter("hostname", server);
            configuration.SetParameter("port", port ?? "0");
            configuration.SetParameter("username", username);
            configuration.SetParameter("password", password);
            _service = new RabbitMqMessageQueueService(configuration);

            // A separate Connection to delete the Queues
            _connectionFactory = new ConnectionFactory { HostName = server };
            if (int.TryParse(port, out var p) && p > 0) _connectionFactory.Port = p;
            if (!string.IsNullOrEmpty(username)) _connectionFactory.UserName = username;
            if (!string.IsNullOrEmpty(password)) _connectionFactory.Password = password;
        }

        protected override string NewQueue()
        {
            var queue = $"trakhound-conformance-{Guid.NewGuid():N}";
            _queues.Add(queue);
            return queue;
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            if (_service == null) return;

            _service.Dispose();

            await using var connection = await _connectionFactory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();
            foreach (var queue in _queues) await channel.QueueDeleteAsync(queue);
        }
    }
}
