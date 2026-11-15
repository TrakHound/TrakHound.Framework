using RabbitMQ.Client;

namespace TrakHound.RabbitMq.Service
{
    /// <summary>
    /// A RabbitMQ Channel along with the Connection it belongs to. Operations on a Channel are made one at a time
    /// while holding Lock (shared with the Channel's owner for the long lived Pull and Publish Channels).
    /// </summary>
    internal sealed class RabbitMqChannel
    {
        public string ConnectionId { get; }

        public IChannel Channel { get; }

        public SemaphoreSlim Lock { get; }

        public int ChannelNumber => Channel.ChannelNumber;

        public bool IsOpen => Channel.IsOpen;


        public RabbitMqChannel(string connectionId, IChannel channel, SemaphoreSlim channelLock)
        {
            ConnectionId = connectionId;
            Channel = channel;
            Lock = channelLock ?? new SemaphoreSlim(1, 1);
        }


        /// <summary>
        /// Runs the action while holding Lock
        /// </summary>
        public async Task Run(Func<IChannel, Task> action, CancellationToken cancellationToken = default)
        {
            await Lock.WaitAsync(cancellationToken);
            try
            {
                await action(Channel);
            }
            finally
            {
                Lock.Release();
            }
        }
    }
}
