// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// The result of a delivered item that is waiting to be Acknowledged
    /// </summary>
    public struct TrakHoundAcknowledgeResult<T>
    {
        /// <summary>
        /// Returns true if the result has a DeliveryId and Content
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(DeliveryId) && Content != null;

        /// <summary>
        /// The Identifier of the delivery, used to Acknowledge it
        /// </summary>
        public string DeliveryId { get; set; }

        /// <summary>
        /// The sequence number of the delivery
        /// </summary>
        public long Sequence { get; set; }

        /// <summary>
        /// The content that was delivered
        /// </summary>
        public T Content { get; set; }
    }
}
