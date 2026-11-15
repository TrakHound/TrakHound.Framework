// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Apps
{
    /// <summary>
    /// A request to show a Message from the admin interface on a page (for one connection, or every open page of an App).
    /// Messages are not stored : only pages that are open when the Message is sent show it
    /// </summary>
    public class TrakHoundAppMessageRequest
    {
        public const int MaxLength = 1000;
        public const int MaxFromLength = 100;
        public const string DefaultFrom = "Administrator";
        public const int DefaultSeconds = 10;
        public const int MaxSeconds = 3600;

        /// <summary>
        /// The connection to show the Message on (null to show it on every open page of the App)
        /// </summary>
        public string ConnectionId { get; set; }

        /// <summary>
        /// The App whose open pages show the Message (when ConnectionId is null)
        /// </summary>
        public string AppId { get; set; }

        public string Message { get; set; }

        /// <summary>
        /// Who the Message is from (shown as "Message from ...")
        /// </summary>
        public string From { get; set; } = DefaultFrom;

        public TrakHoundAppMessageSeverity Severity { get; set; }

        /// <summary>
        /// Hide the Message automatically after this many seconds (0 to show it until the user closes it)
        /// </summary>
        public int Seconds { get; set; }
    }

    public enum TrakHoundAppMessageSeverity
    {
        Information,
        Warning
    }
}
