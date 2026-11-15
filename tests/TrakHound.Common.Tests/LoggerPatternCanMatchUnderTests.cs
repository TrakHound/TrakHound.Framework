// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Logging;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// TrakHoundLoggerPattern.CanMatchUnder : whether a Pattern could match an Entry Sender under a Logger Name (ex. "{loggerName}/Devices/d-1")
    /// </summary>
    public class LoggerPatternCanMatchUnderTests
    {
        private const string Logger = "workers/TrakHound.Package/0e1b/Engine";

        [Theory]
        [InlineData("workers/**/Devices/d-1")]
        [InlineData("workers/TrakHound.Package/**/Devices/d-1/**")]
        [InlineData("workers/TrakHound.Package/*/Engine/Devices/d-1")]
        [InlineData("WORKERS/trakhound.package/0E1B/engine/devices/*")]
        [InlineData("workers/TrakHound.Package/0e1b/Engine/Devices/d-1")]
        [InlineData("**")]
        public void PatternThatReachesUnderTheLogger_CanMatch(string pattern)
        {
            Assert.True(TrakHoundLoggerPattern.CanMatchUnder(pattern, Logger));
        }

        [Theory]
        [InlineData("workers/Other.Package/**/Devices/d-1")]
        [InlineData("workers/TrakHound.Package/*/Main/Devices/d-1")]
        [InlineData("workers/TrakHound.Package/0e1b/Engine")]
        [InlineData("workers/TrakHound.Package/*")]
        [InlineData("api/**")]
        public void PatternThatEndsAtOrAwayFromTheLogger_CantMatch(string pattern)
        {
            Assert.False(TrakHoundLoggerPattern.CanMatchUnder(pattern, Logger));
        }

        [Fact]
        public void LoggerWithoutName_CantMatch()
        {
            Assert.False(TrakHoundLoggerPattern.CanMatchUnder("workers/**", null));
        }
    }
}
