// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Controllers.Http
{
    public class RouteDescriptionAttribute : Attribute
    {
        public string Description { get; set; }


        public RouteDescriptionAttribute(string description)
        {
            Description = description;
        }
    }
}
