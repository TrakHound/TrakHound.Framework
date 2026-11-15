// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services.MessageQueues
{
    /// <summary>
    /// The result of Acknowledging or Rejecting one delivery of a Message
    /// </summary>
    public class TrakHoundQueueDeliveryResult
    {
        /// <summary>
        /// The Delivery ID that was Acknowledged or Rejected
        /// </summary>
        [JsonPropertyName("deliveryId")]
        public string DeliveryId { get; set; }

        /// <summary>
        /// True if the Broker accepted the Acknowledgement or Rejection
        /// </summary>
        [JsonPropertyName("success")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] // Written when false too, so HTTP callers don't have to treat a missing value as a failure
        public bool Success { get; set; }

        /// <summary>
        /// Why it failed (ex. the Channel the Message was delivered on has closed, so the Broker returned it to the Queue)
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }


        public TrakHoundQueueDeliveryResult() { }

        public TrakHoundQueueDeliveryResult(string deliveryId, bool success, string message = null)
        {
            DeliveryId = deliveryId;
            Success = success;
            Message = message;
        }
    }
}
