using TrakHound.FileSystem.Driver;
using TrakHound.Configurations;
using TrakHound.Instances;
using TrakHound.Mqtt.Driver;
using TrakHound.RabbitMQ.Drivers;
using TrakHound.Redis.Driver;
using TrakHound.Services;
using TrakHound.SqlServer.Driver;

namespace TrakHound.Instance
{
    public static class Debug
    {
        public static TrakHoundConfigurationProfile CreateConfiguration()
        {
            var configurationProfile = new TrakHoundConfigurationProfile("default");

            var routerConfiguration = new TrakHoundServiceRouterConfiguration();
            routerConfiguration.Id = "default";
            var routeConfiguration = new TrakHoundServiceRouteConfiguration();
            routeConfiguration.AddPattern("*");
            routeConfiguration.AddTarget(new TrakHoundServiceTargetConfiguration() { Type = "Driver", Source = "blobs" });
            routeConfiguration.AddTarget(new TrakHoundServiceTargetConfiguration() { Type = "Driver", Source = "keyValues" });
            routeConfiguration.AddTarget(new TrakHoundServiceTargetConfiguration() { Type = "Driver", Source = "messages" });
            routeConfiguration.AddTarget(new TrakHoundServiceTargetConfiguration() { Type = "Driver", Source = "messageQueues" });
            routeConfiguration.AddTarget(new TrakHoundServiceTargetConfiguration() { Type = "Driver", Source = "tables" });
            routerConfiguration.AddRoute(routeConfiguration);
            configurationProfile.Add(routerConfiguration);

            return configurationProfile;
        }

        public static void AddDrivers(TrakHoundInstance instance)
        {
            // Blobs Driver
            var blobsDriverConfiguration = new TrakHoundServiceDriverConfiguration();
            blobsDriverConfiguration.Id = "blobs";
            instance.ServicesProvider.Drivers.AddDriver(new FileSystemBlobDriver(blobsDriverConfiguration));

            // KeyValues Driver
            var keyValuesDriverConfiguration = new TrakHoundServiceDriverConfiguration();
            keyValuesDriverConfiguration.Id = "keyValues";
            keyValuesDriverConfiguration.SetParameter("server", "localhost");
            keyValuesDriverConfiguration.SetParameter("port", "6379");
            keyValuesDriverConfiguration.SetParameter("database", "5");
            instance.ServicesProvider.Drivers.AddDriver(new RedisKeyValueDriver(keyValuesDriverConfiguration));

            // Messages Driver
            var messagesDriverConfiguration = new TrakHoundServiceDriverConfiguration();
            messagesDriverConfiguration.Id = "messages";
            messagesDriverConfiguration.VolumeId = "trakhound.mqtt.driver.configuration";
            messagesDriverConfiguration.SetParameter("hostname", "localhost");
            messagesDriverConfiguration.SetParameter("port", "1883");
            instance.ServicesProvider.Drivers.AddDriver(new MqttMessageDriver(messagesDriverConfiguration));

            // MessageQueues Driver
            var messageQueuesDriverConfiguration = new TrakHoundServiceDriverConfiguration();
            messageQueuesDriverConfiguration.Id = "messageQueues";
            messageQueuesDriverConfiguration.SetParameter("hostname", "localhost");
            instance.ServicesProvider.Drivers.AddDriver(new RabbitMqMessageQueueDriver(messageQueuesDriverConfiguration));

            // Tables Driver
            var tablesDriverConfiguration = new TrakHoundServiceDriverConfiguration();
            tablesDriverConfiguration.Id = "tables";
            tablesDriverConfiguration.SetParameter("server", "localhost");
            tablesDriverConfiguration.SetParameter("port", "1433");
            tablesDriverConfiguration.SetParameter("user", "trakhound");
            tablesDriverConfiguration.SetParameter("password", "trakhound");
            tablesDriverConfiguration.SetParameter("database", "trakhound");
            instance.ServicesProvider.Drivers.AddDriver(new SqlServerTableService(tablesDriverConfiguration));
        }
    }
}
