// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Api
{
    /// <summary>
    /// The Data Type names used in Api Schema Information
    /// </summary>
    public static class TrakHoundApiSchemaDataType
    {
        /// <summary>
        /// Used for double and decimal
        /// </summary>
        public const string Float = "float";
        /// <summary>
        /// Used for long
        /// </summary>
        public const string Long = "long";
        /// <summary>
        /// Used for int
        /// </summary>
        public const string Integer = "integer";
        /// <summary>
        /// Used for string
        /// </summary>
        public const string String = "string";
        /// <summary>
        /// Used for DateTime
        /// </summary>
        public const string Timestamp = "timestamp";


        /// <summary>
        /// Gets the Data Type name for the specified Type (null if the Type is not supported)
        /// </summary>
        public static string Get(Type type)
        {
            if (type != null)
            {
                if (type == typeof(double) || type == typeof(decimal))
                {
                    return Float;
                }
                else if (type == typeof(long))
                {
                    return Long;
                }
                else if (type == typeof(int))
                {
                    return Integer;
                }
                else if (type == typeof(string))
                {
                    return String;
                }
                else if (type == typeof(DateTime))
                {
                    return Timestamp;
                }
            }

            return null;
        }
    }
}
