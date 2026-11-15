// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Apps
{
    public class TrakHoundScopedAppInjectionServices : TrakHoundAppInjectionServicesBase<ITrakHoundScopedAppInjectionService>
    {
        public TrakHoundScopedAppInjectionServices(ITrakHoundAppInjectionServiceManager serviceManager) : base(serviceManager) { }
    }
}
