// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Apps
{
    public class TrakHoundSingletonAppInjectionServices : TrakHoundAppInjectionServicesBase<ITrakHoundSingletonAppInjectionService>
    {
        public TrakHoundSingletonAppInjectionServices(ITrakHoundAppInjectionServiceManager serviceManager) : base(serviceManager) { }
    }
}
