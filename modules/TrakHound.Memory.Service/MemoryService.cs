// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Services;

namespace TrakHound.Memory.Service
{
    public abstract class MemoryService : TrakHoundService, IDisposable
    {
        public MemoryService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }
    }
}
