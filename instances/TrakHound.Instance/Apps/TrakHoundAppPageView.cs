// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Apps;

namespace TrakHound.Instance.Apps
{
    /// <summary>
    /// A completed App Page View (from when a Connection navigated to the Page until it left it or disconnected)
    /// </summary>
    public class TrakHoundAppPageView
    {
        /// <summary>
        /// Unique ID of the Client Connection (browser tab)
        /// </summary>
        public string Id { get; set; }

        public TrakHoundAppConnection Connection { get; set; }

        public string AppId { get; set; }

        public string AppName { get; set; }

        public string PackageId { get; set; }

        public string Route { get; set; }

        /// <summary>
        /// The requested Path (without the Query)
        /// </summary>
        public string Path { get; set; }

        public DateTime Started { get; set; }

        public DateTime Ended { get; set; }

        /// <summary>
        /// How long the Page was viewed for (in Milliseconds)
        /// </summary>
        public double Duration { get; set; }
    }
}
