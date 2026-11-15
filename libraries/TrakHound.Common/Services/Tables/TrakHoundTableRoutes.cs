// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Services.Tables
{
    internal static class TrakHoundTableRoutes
    {
        public const string Tables = "Tables";


        public static readonly Dictionary<Type, string> _routes = new Dictionary<Type, string>
        {
            { typeof(ITrakHoundTableService), Tables }
        };
    }
}
