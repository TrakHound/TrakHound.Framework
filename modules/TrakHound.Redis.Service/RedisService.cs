// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Services;

namespace TrakHound.Redis.Driver
{
    public abstract class RedisService : TrakHoundService, IDisposable
    {
        public RedisService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }
    }
}
