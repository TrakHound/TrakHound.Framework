// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using TrakHound.Clients;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Apps
{
    /// <summary>
    /// The resources used to create an App Injection Service of a specific Type for an App
    /// </summary>
    public class TrakHoundAppInjectionServiceConfiguration
    {
        /// <summary>
        /// The ID of the App Configuration that the Service belongs to
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The App Configuration (injected into the Service as ITrakHoundAppConfiguration)
        /// </summary>
        public ITrakHoundAppConfiguration Configuration { get; set; }

        /// <summary>
        /// The Client for the App's Router (injected into the Service as ITrakHoundClient)
        /// </summary>
        public ITrakHoundClient Client { get; set; }

        /// <summary>
        /// The App's Volume (injected into the Service as ITrakHoundVolume)
        /// </summary>
        public ITrakHoundVolume Volume { get; set; }

        /// <summary>
        /// The App's Variables (injected into the Service as ITrakHoundVariableCollection)
        /// </summary>
        public ITrakHoundVariableCollection Variables { get; set; }

        /// <summary>
        /// The Type of the Service to create (loaded from the App Package)
        /// </summary>
        public Type ServiceType { get; set; }


        /// <summary>
        /// Creates a new Service Configuration (Id is set to the App Configuration's Id)
        /// </summary>
        public TrakHoundAppInjectionServiceConfiguration(
            ITrakHoundAppConfiguration configuration,
            ITrakHoundClient client,
            ITrakHoundVolume volume, 
            ITrakHoundVariableCollection variables, 
            Type serviceType
            )
        {
            Id = configuration?.Id;
            Configuration = configuration;
            Client = client;
            Volume = volume;
            Variables = variables;
            ServiceType = serviceType;
        }


        /// <summary>
        /// Creates an ID from an MD5 Hash of the Service Type's Assembly location and full Name (null if the Configuration or Type is null)
        /// </summary>
        public static string CreateId(ITrakHoundAppConfiguration configuration, Type serviceType)
        {
            if (configuration != null && serviceType != null && serviceType.Assembly != null)
            {
                return $"{serviceType.Assembly.Location}:{serviceType.FullName}".ToMD5Hash();
            }

            return null;
        }
    }
}
