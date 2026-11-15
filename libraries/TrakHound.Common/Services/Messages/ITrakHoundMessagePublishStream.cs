// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Services.Messages
{
    /// <summary>
    /// A Stream that publishes each Item pushed to it as a Message. Push returns false when the Message wasn't published (or, over a WebSocket, couldn't be sent).
    /// A Publish Stream has no output, so Received is never raised
    /// </summary>
    public interface ITrakHoundMessagePublishStream<T> : ITrakHoundStream<T>
    {
        /// <summary>
        /// Raised for each Message that the Message Service could not publish
        /// </summary>
        event EventHandler<TrakHoundMessagePublishFailure> PublishFailed;
    }
}
