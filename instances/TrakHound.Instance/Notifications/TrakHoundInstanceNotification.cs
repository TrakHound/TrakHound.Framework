// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Instance.Notifications
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TrakHoundInstanceNotificationType
    {
        Information,
        Success,
        Warning,
        Error
    }

    public static class TrakHoundInstanceNotificationCategories
    {
        public const string Instance = "Instance";
        public const string Packages = "Packages";
        public const string Configuration = "Configuration";
        public const string Health = "Health";
    }

    /// <summary>
    /// A Notification shown in the Admin Interface Notifications sidebar
    /// </summary>
    public class TrakHoundInstanceNotification
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// Identifies what the Notification is about (ex. "package-update:{packageId}:{version}").
        /// Only one Notification is kept per Key and a dismissed Key is not notified again.
        /// </summary>
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("type")]
        public TrakHoundInstanceNotificationType Type { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        /// <summary>
        /// Admin Interface link to view more (ex. "_admin/packages")
        /// </summary>
        [JsonPropertyName("link")]
        public string Link { get; set; }

        [JsonPropertyName("linkText")]
        public string LinkText { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("read")]
        public bool Read { get; set; }

        /// <summary>
        /// How many times this has occurred, for a Notification that is updated instead of added again (ex. Unclean Shutdowns)
        /// </summary>
        [JsonPropertyName("count")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int Count { get; set; }
    }
}
