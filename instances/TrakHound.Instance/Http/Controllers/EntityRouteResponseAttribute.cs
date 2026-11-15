// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Controllers.Http
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    public class EntityRouteResponseAttribute : RouteResponseAttribute
    {
        public EntityRouteResponseAttribute(int statusCode, Type responseType) : base(statusCode, responseType) { }
    }
}
