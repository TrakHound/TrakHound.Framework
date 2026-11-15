// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Services.MessageQueues
{
    /// <summary>
    /// The properties a Message is Published with. They are delivered with the Message (see TrakHoundQueueMessage)
    /// </summary>
    public class TrakHoundQueuePublishOptions
    {
        /// <summary>
        /// The Identifier of the Message (ex. to detect duplicates). The Service creates one when not set
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// The media type of the Content (ex. application/json)
        /// </summary>
        public string ContentType { get; set; }

        /// <summary>
        /// Application defined Headers (ex. a correlation ID)
        /// </summary>
        public Dictionary<string, string> Headers { get; set; }
    }
}
