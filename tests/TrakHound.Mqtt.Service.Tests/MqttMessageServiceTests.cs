// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services;

namespace TrakHound.Mqtt.Service.Tests
{
    /// <summary>
    /// The connection options and the errors that don't need a Broker
    /// </summary>
    public class MqttMessageServiceTests
    {
        private static MqttConnectionConfiguration Connection(bool cleanSession) => new MqttConnectionConfiguration { Hostname = "localhost", CleanSession = cleanSession };


        [Fact]
        public void CleanSession_False_IsSent()
        {
            // MQTTnet starts with a Clean Session, so false has to be set explicitly
            Assert.False(MqttClientFactory.CreateOptions(Connection(false), "fixed-client", clientIdIsGenerated: false).CleanSession);
        }

        [Fact]
        public void CleanSession_True_IsSent()
        {
            Assert.True(MqttClientFactory.CreateOptions(Connection(true), "fixed-client", clientIdIsGenerated: false).CleanSession);
        }

        [Fact]
        public void GeneratedClientId_AlwaysUsesACleanSession()
        {
            // Nothing would resume a Persistent Session for a new GUID, so the Broker would keep it for nothing
            Assert.True(MqttClientFactory.CreateOptions(Connection(false), Guid.NewGuid().ToString(), clientIdIsGenerated: true).CleanSession);
        }

        [Fact]
        public void CleanSession_DefaultsToTrue()
        {
            Assert.True(new MqttConnectionConfiguration().CleanSession);
        }

        [Fact]
        public async Task NoHostname_Returns503()
        {
            using var service = new MqttMessageService(new TrakHoundServiceConfiguration { Id = "mqtt-empty", Name = "mqtt-empty" });

            using var content = new MemoryStream(new byte[] { 1 });
            Assert.Equal(503, (await service.Publish("test", content, false, 0)).StatusCode);
            Assert.Equal(503, (await service.Subscribe(null, new[] { "test" }, 0)).StatusCode);
        }

        [Fact]
        public async Task UnreachableBroker_Publish_Returns503()
        {
            // Nothing listens on port 1, so the connection is refused straight away
            var configuration = new TrakHoundServiceConfiguration { Id = "mqtt-unavailable", Name = "mqtt-unavailable" };
            configuration.SetParameter("hostname", "127.0.0.1");
            configuration.SetParameter("port", "1");
            configuration.SetParameter("timeout", "2000");
            using var service = new MqttMessageService(configuration);

            using var content = new MemoryStream(new byte[] { 1 });
            Assert.Equal(503, (await service.Publish("test", content, false, 0)).StatusCode);
        }
    }
}
