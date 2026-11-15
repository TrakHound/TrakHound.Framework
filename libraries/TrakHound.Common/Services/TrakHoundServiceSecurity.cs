// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using TrakHound.Services.Blobs;
using TrakHound.Services.KeyValues;
using TrakHound.Services.MessageQueues;
using TrakHound.Services.Messages;
using TrakHound.Services.Tables;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Services
{
    /// <summary>
    /// Security Resource IDs and Permissions for Service operations.
    /// Each operation of each Service is a Resource with the ID "service:{serviceConfigurationId}:{family}:{operation}" (ex. "service:redis:key-values:publish")
    /// and requires either the "read" or "write" Permission.
    /// </summary>
    public static class TrakHoundServiceSecurity
    {
        public const string ResourcePrefix = "service";
        public const string ReadPermission = "read";
        public const string WritePermission = "write";
        public const string CheckAvailabilityOperation = "CheckAvailability";

        // Service Interface => Family (matches the System API routes, ex. "_services/key-values")
        private static readonly (Type Type, string Family)[] _families = new (Type, string)[]
        {
            (typeof(ITrakHoundBlobService), "blobs"),
            (typeof(ITrakHoundKeyValueService), "key-values"),
            (typeof(ITrakHoundMessageQueueService), "message-queues"),
            (typeof(ITrakHoundMessageService), "messages"),
            (typeof(ITrakHoundTableService), "tables"),
            (typeof(ITrakHoundTimeSeriesService), "time-series")
        };

        // Operations that change data (everything else only reads). Acknowledge and Reject are part of consuming a Message Queue so they only require "read"
        private static readonly HashSet<string> _writeOperations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Upload",
            "Delete",
            "Publish",
            "ExecuteNonQuery",
            "Write"
        };


        /// <summary>
        /// Gets the Family name of a Service type (ex. "key-values"). Null if the type is not a known Service
        /// </summary>
        public static string GetFamily(Type serviceType)
        {
            if (serviceType != null)
            {
                foreach (var (type, family) in _families)
                {
                    if (type.IsAssignableFrom(serviceType)) return family;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets every Family a Service type implements (ex. a Service can be both a Key Value and a Message Service)
        /// </summary>
        public static IEnumerable<string> GetFamilies(Type serviceType)
        {
            var families = new List<string>();
            if (serviceType != null)
            {
                foreach (var (type, family) in _families)
                {
                    if (type.IsAssignableFrom(serviceType)) families.Add(family);
                }
            }

            return families;
        }

        /// <summary>
        /// Gets the Security Resource ID of a Service operation
        /// </summary>
        public static string GetResourceId(ITrakHoundService service, Type serviceType, string operation)
        {
            return GetResourceId(service?.Configuration?.Id, GetFamily(serviceType), operation);
        }

        /// <summary>
        /// Gets the Security Resource ID of a Service operation
        /// </summary>
        public static string GetResourceId(string serviceConfigurationId, string family, string operation)
        {
            return $"{ResourcePrefix}:{serviceConfigurationId}:{family ?? "unknown"}:{operation?.ToLowerInvariant()}";
        }

        /// <summary>
        /// Gets the Permission ("read" or "write") required for a Service operation
        /// </summary>
        public static string GetPermission(string operation)
        {
            return operation != null && _writeOperations.Contains(operation) ? WritePermission : ReadPermission;
        }
    }
}
