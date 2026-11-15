// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Services.Blobs
{
    internal static class TrakHoundBlobRoutes
    {
        public const string Blobs = "Blobs";


        public static readonly Dictionary<Type, string> _routes = new Dictionary<Type, string>
        {
            { typeof(ITrakHoundBlobService), Blobs }
        };
    }
}
