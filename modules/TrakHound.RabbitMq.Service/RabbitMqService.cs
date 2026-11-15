using TrakHound.Services;

namespace TrakHound.RabbitMq.Service
{
    public abstract class RabbitMqService : TrakHoundService
    {
        public RabbitMqService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }
    }
}
