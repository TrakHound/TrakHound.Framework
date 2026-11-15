// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Services.TimeSeries
{
    internal static class TrakHoundTimeSeriesRoutes
    {
        public const string TimeSeries = "TimeSeries";


        public static readonly Dictionary<Type, string> _routes = new Dictionary<Type, string>
        {
            { typeof(ITrakHoundTimeSeriesService), TimeSeries }
        };
    }
}
