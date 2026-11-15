// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections;
using System.Collections.Generic;

namespace TrakHound.Api
{
    /// <summary>
    /// Information describing the Schema of an Object Type
    /// </summary>
    public class TrakHoundApiSchemaInformation
    {
        /// <summary>
        /// The name of the Type
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The public Properties of the Type
        /// </summary>
        public IEnumerable<TrakHoundApiSchemaPropertyInformation> Properties { get; set; }


        /// <summary>
        /// Creates the Schema Information for the specified Type using reflection. Returns null if the Type is null
        /// </summary>
        public static TrakHoundApiSchemaInformation Create(Type objectType)
        {
            if (objectType != null)
            {
                var information = new TrakHoundApiSchemaInformation();
                information.Name = objectType.Name;

                //if (typeof(IEnumerable).IsAssignableFrom(objectType))
                //{
                //    var types = objectType.GetGenericArguments();
                //    if (!types.IsNullOrEmpty())
                //    {

                //    }
                //}
                //else
                //{

                //}

                // Add Properties
                var objProperties = objectType.GetProperties();
                if (!objProperties.IsNullOrEmpty())
                {
                    var properties = new List<TrakHoundApiSchemaPropertyInformation>();

                    foreach (var objProperty in objProperties)
                    {
                        var property = TrakHoundApiSchemaPropertyInformation.Create(objProperty);
                        if (property != null) properties.Add(property);
                    }

                    information.Properties = properties;
                }

                return information;
            }

            return null;
        }
    }
}
