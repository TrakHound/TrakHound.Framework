// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    /// <summary>
    /// What made an API Request
    /// </summary>
    public enum TrakHoundApiRequestSourceType
    {
        /// <summary>
        /// The Instance itself (ex. the Admin Interface or an Identity Provider)
        /// </summary>
        Instance,

        /// <summary>
        /// An App
        /// </summary>
        App,
        /// <summary>
        /// Another Api
        /// </summary>
        Api,
        /// <summary>
        /// A Function
        /// </summary>
        Function,
        /// <summary>
        /// A Worker
        /// </summary>
        Worker,

        /// <summary>
        /// An external HTTP (or WebSocket) Request
        /// </summary>
        Http
    }

    /// <summary>
    /// Identifies what made an API Request (used for API Metrics, and to check calls made by Modules against their Zones)
    /// </summary>
    public class TrakHoundApiRequestSource
    {
        /// <summary>
        /// The Source used for Requests made by the Instance itself
        /// </summary>
        public static readonly TrakHoundApiRequestSource Instance = new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Instance);


        /// <summary>
        /// What type of Source made the Request
        /// </summary>
        public TrakHoundApiRequestSourceType Type { get; }

        /// <summary>
        /// The Configuration ID of the App, Api, Function, or Worker (or the remote address for Http)
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// A display name for the Source (the Configuration Name, or Package ID if not named)
        /// </summary>
        public string Name { get; }


        /// <summary>
        /// Creates a Source. The Name defaults to the ID when empty
        /// </summary>
        public TrakHoundApiRequestSource(TrakHoundApiRequestSourceType type, string id = null, string name = null)
        {
            Type = type;
            Id = id;
            Name = !string.IsNullOrEmpty(name) ? name : id;
        }

        /// <summary>
        /// Creates a Source for a Configuration, named by the Configuration Name (or the Package ID if not named)
        /// </summary>
        public static TrakHoundApiRequestSource Create(TrakHoundApiRequestSourceType type, string configurationId, string configurationName, string packageId)
        {
            return new TrakHoundApiRequestSource(type, configurationId, !string.IsNullOrEmpty(configurationName) ? configurationName : packageId);
        }
    }
}
