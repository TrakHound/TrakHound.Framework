// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Net.Http;

namespace TrakHound.Api
{
    /// <summary>
    /// Converts between TrakHoundApiMethod and HttpMethod
    /// </summary>
    public static class TrakHoundApiMethodExtensions
    {
        /// <summary>
        /// Converts a TrakHoundApiMethod to an HttpMethod
        /// </summary>
        public static HttpMethod ToHttpMethod(this TrakHoundApiMethod apiMethod)
        {
            switch (apiMethod)
            {
                // CONNECT and PATCH are created by name: HttpMethod.Connect and HttpMethod.Patch are not in .NET Standard 2.0
                case TrakHoundApiMethod.CONNECT: return new HttpMethod("CONNECT");
                case TrakHoundApiMethod.DELETE: return HttpMethod.Delete;
                case TrakHoundApiMethod.GET: return HttpMethod.Get;
                case TrakHoundApiMethod.HEAD: return HttpMethod.Head;
                case TrakHoundApiMethod.OPTIONS: return HttpMethod.Options;
                case TrakHoundApiMethod.PATCH: return new HttpMethod("PATCH");
                case TrakHoundApiMethod.POST: return HttpMethod.Post;
                case TrakHoundApiMethod.PUT: return HttpMethod.Put;
                case TrakHoundApiMethod.TRACE: return HttpMethod.Trace;
            }

            return null;
        }
    }
}
