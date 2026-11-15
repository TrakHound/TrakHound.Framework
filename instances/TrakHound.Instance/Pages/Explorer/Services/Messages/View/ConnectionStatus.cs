namespace TrakHound.Instance.Pages.Explorer.Services.Messages.View
{
    public enum ConnectionStatus
    {
        /// <summary>
        /// No Subscription is active
        /// </summary>
        NotSubscribed,

        /// <summary>
        /// A Subscribe request is in progress
        /// </summary>
        Connecting,

        /// <summary>
        /// Subscribed and the Message Service reports its Broker as available
        /// </summary>
        Connected,

        /// <summary>
        /// Subscribed but the Message Service reports its Broker as unavailable
        /// </summary>
        Disconnected,

        /// <summary>
        /// The last Subscribe request failed
        /// </summary>
        Error
    }
}
