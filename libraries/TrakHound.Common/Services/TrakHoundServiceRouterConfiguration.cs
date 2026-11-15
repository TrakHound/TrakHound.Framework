// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using TrakHound.Configurations;
using YamlDotNet.Serialization;

namespace TrakHound.Services
{
    /// <summary>
    /// A Configuration used to configure a Services Router
    /// </summary>
    public class TrakHoundServiceRouterConfiguration : ITrakHoundConfiguration
    {
        /// <summary>
        /// The Category of Router Configurations ("router")
        /// </summary>
        public const string ConfigurationCategory = "router";


        /// <summary>
        /// A unique Identifier for the Router
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// A friendly identifier for the Router
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A description of the Router
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Configurations used to configure Routes to Services and Routers
        /// </summary>
        public IEnumerable<TrakHoundServiceRouteConfiguration> Routes { get; set; }


        /// <summary>
        /// The Category of the Configuration (always "router")
        /// </summary>
        [YamlIgnore]
        public string Category => ConfigurationCategory;

        /// <summary>
        /// The path of the file the Configuration was read from
        /// </summary>
        [YamlIgnore]
        public string Path { get; set; }

        /// <summary>
        /// A hash of the Configuration's properties, used to detect changes
        /// </summary>
        [YamlIgnore]
        public string Hash => GenerateHash();


        private string GenerateHash()
        {
            return $"{Id}:{Name}:{Description}:{Routes?.ToJson()}".ToSHA256Hash();
        }


        /// <summary>
        /// Adds the specified Route
        /// </summary>
        public void AddRoute(TrakHoundServiceRouteConfiguration configuration)
        {
            if (configuration != null)
            {
                var routers = Routes?.ToList();
                if (routers == null) routers = new List<TrakHoundServiceRouteConfiguration>();
                routers.Add(configuration);
                Routes = routers;
            }
        }

        /// <summary>
        /// Removes the Route with the specified Id (Routes is set to null when none remain)
        /// </summary>
        public void RemoveRoute(string id)
        {
            if (!string.IsNullOrEmpty(id) && !Routes.IsNullOrEmpty())
            {
                var routers = Routes.ToList();
                routers.RemoveAll(o => o.Id == id);
                if (routers.Count > 0)
                {
                    Routes = routers;
                }
                else Routes = null;
            }
        }

        /// <summary>
        /// Removes the Target with the specified Id from each Route
        /// </summary>
        public void RemoveTarget(string id)
        {
            if (!string.IsNullOrEmpty(id) && !Routes.IsNullOrEmpty())
            {
                foreach (var route in Routes)
                {
                    route.RemoveTarget(id);
                }
            }
        }
    }
}
