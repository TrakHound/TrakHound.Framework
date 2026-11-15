// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace TrakHound.Api
{
    /// <summary>
    /// Information describing a Property within an Api Schema
    /// </summary>
    public class TrakHoundApiSchemaPropertyInformation
    {
        /// <summary>
        /// The name of the Property
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Data Type of the Property (see TrakHoundApiSchemaDataType, or "Object" for complex Types)
        /// </summary>
        public string DataType { get; set; }

        /// <summary>
        /// A description of the Property (from its XML Documentation)
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The child Properties (only set when DataType is "Object")
        /// </summary>
        public IEnumerable<TrakHoundApiSchemaPropertyInformation> Properties { get; set; }


        /// <summary>
        /// Creates the Information for the specified Property, including child Properties for complex Types. Returns null if the Property is null
        /// </summary>
        public static TrakHoundApiSchemaPropertyInformation Create(PropertyInfo propertyInfo)
        {
            if (propertyInfo != null)
            {
                var information = new TrakHoundApiSchemaPropertyInformation();
                information.Name = propertyInfo.Name;

                var description = propertyInfo.GetXmlDocumentation();
                if (!string.IsNullOrEmpty(description)) information.Description = description;

                if (propertyInfo.PropertyType.IsPrimitive || propertyInfo.PropertyType == typeof(string) || propertyInfo.PropertyType == typeof(DateTime))
                {
                    information.DataType = TrakHoundApiSchemaDataType.Get(propertyInfo.PropertyType);
                }
                else
                {
                    information.DataType = "Object";

                    // Add Properties
                    var objProperties = propertyInfo.PropertyType.GetProperties();
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
                }

                return information;
            }

            return null;
        }
    }
}
