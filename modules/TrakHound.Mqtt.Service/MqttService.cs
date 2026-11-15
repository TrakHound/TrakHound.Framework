// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services;

namespace TrakHound.Mqtt.Service
{
    public abstract class MqttService : TrakHoundService, IDisposable
    {
        public MqttService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }
    }
}
