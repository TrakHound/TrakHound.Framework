// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Configurations
{
    /// <summary>
    /// A change to a Configuration in a Configuration Profile
    /// </summary>
    public class TrakHoundConfigurationChangedArgs
    {
        /// <summary>
        /// How the Configuration changed
        /// </summary>
        public TrakHoundConfigurationChangeType ChangeType { get; }

        /// <summary>
        /// The Configuration (for Removed, the Configuration that was removed)
        /// </summary>
        public ITrakHoundConfiguration Configuration { get; }


        public TrakHoundConfigurationChangedArgs(TrakHoundConfigurationChangeType changeType, ITrakHoundConfiguration configuration)
        {
            ChangeType = changeType;
            Configuration = configuration;
        }
    }
}
