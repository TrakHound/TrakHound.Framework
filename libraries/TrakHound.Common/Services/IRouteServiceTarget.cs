// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Services
{
    interface IRouteServiceTarget : IRouteTarget
    {
        IEnumerable<ITrakHoundService> Services { get; set; }

        IEnumerable<TService> GetServices<TService>() where TService : ITrakHoundService;
    }
}
