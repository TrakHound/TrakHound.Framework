// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;

namespace TrakHound.Apps
{
    public class TrakHoundScopedAppInjectionServices : TrakHoundAppInjectionServicesBase<ITrakHoundScopedAppInjectionService>
    {
        public TrakHoundScopedAppInjectionServices(ITrakHoundInstance instance) : base(instance) { }
    }
}
