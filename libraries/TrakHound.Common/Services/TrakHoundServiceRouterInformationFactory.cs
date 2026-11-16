// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services
{
    /// <summary>
    /// Creates the Information for a Service Router. Kept apart from TrakHoundServiceRouterInformation so the Information class doesn't depend on the Router itself
    /// </summary>
    public static class TrakHoundServiceRouterInformationFactory
    {
        /// <summary>
        /// Creates the information for the specified Router (null if the Router or its Configuration is null)
        /// </summary>
        public static TrakHoundServiceRouterInformation Create(TrakHoundServiceRouter router)
        {
            if (router != null && router.Configuration != null)
            {
                var information = new TrakHoundServiceRouterInformation();
                information.Id = router.Configuration.Id;
                information.Name = router.Configuration.Name;
                information.Description = router.Configuration.Description;

                return information;
            }

            return null;
        }
    }
}
