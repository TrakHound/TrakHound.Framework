// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services
{
    interface IRouteTarget
    {
        string Id { get; }

        TrakHoundServiceRouteConfiguration RouteConfiguration { get; }

        TrakHoundServiceTargetConfiguration TargetConfiguration { get; }

        TrakHoundServiceRouteTargetType Type { get; }
    }
}
