// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;

namespace TrakHound.Instance.Apps
{
    public class TrakHoundAppInjectionServiceProvider
    {
        private readonly ITrakHoundInstance _instance;


        public ITrakHoundInstance Instance => _instance;


        public IServiceProvider ServiceProvider { get; set; }


        public TrakHoundAppInjectionServiceProvider(ITrakHoundInstance instance)
        {
            _instance = instance;
        }
    }
}
