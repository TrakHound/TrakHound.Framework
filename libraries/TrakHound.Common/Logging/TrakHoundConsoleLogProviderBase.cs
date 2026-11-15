// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Logging
{
    /// <summary>
    /// A Log Provider that writes each Log Entry to the Console (Log History queries are not supported)
    /// </summary>
    public class TrakHoundConsoleLogProvider : TrakHoundLogProviderBase
    {
        /// <summary>
        /// Writes the Entry to the Console as "{Logger ID} : {Code} : {Message}"
        /// </summary>
        protected override void HandleEntryReceived(ITrakHoundLogger logger, TrakHoundLogItem item)
        {
            Console.WriteLine($"{logger.Id} : {item.Code} : {item.Message}");
        }
    }
}
