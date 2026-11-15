// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services
{
    /// <summary>
    /// Identifying information for a Service Router
    /// </summary>
    public class TrakHoundServiceRouterInformation : ITrakHoundServiceRouterInformation
    {
        /// <summary>
        /// The unique Identifier of the Router
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// A friendly name for the Router
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// A description of the Router
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }


        /// <summary>
        /// Creates the information for the specified Router (null if the Router or its Configuration is null)
        /// </summary>
        public static TrakHoundServiceRouterInformation Create(TrakHoundServiceRouter router)
        {
            if (router != null && router.Configuration != null)
            {
                var information = new TrakHoundServiceRouterInformation();
                information.Id = router.Configuration.Id;
                information.Name = router.Configuration.Name;
                information.Description = router.Configuration.Description;

                return information;
            }

            return null;
        }
    }
}
