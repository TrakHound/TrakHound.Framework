// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients;
using TrakHound.Instances;

namespace TrakHound.Instance.Pages.Explorer
{
    public class InstanceManager
    {
        private readonly ITrakHoundInstance _instance;


        public InstanceManager(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        public ITrakHoundInstance Instance => _instance;


        public IEnumerable<TrakHoundInstanceInformation> GetInstances()
        {
            return new TrakHoundInstanceInformation[] { _instance.Information };
        }

        public TrakHoundInstanceInformation GetInstance(string instanceId)
        {
            if (instanceId == _instance.Information.Id) return _instance.Information;
            return null;
        }

        public ITrakHoundClient GetClient(string instanceId)
        {
            var instance = GetInstance(instanceId);
            if (instance != null)
            {
                return _instance.ClientProvider.GetClient();
            }

            return null;
        }
    }
}
