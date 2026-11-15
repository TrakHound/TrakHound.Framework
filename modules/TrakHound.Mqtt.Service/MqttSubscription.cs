namespace TrakHound.Mqtt.Service
{
    class MqttSubscription
    {
        public string Topic { get; set; }

        public int Qos { get; set; }


        public MqttSubscription(string topic, int qos)
        {
            Topic = topic;
            Qos = qos;
        }
    }
}
