// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Api
{
    class RouteParameter
    {
        public string Name { get; set; }

        public string Value { get; set; }

        public RouteParameter(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }
}
