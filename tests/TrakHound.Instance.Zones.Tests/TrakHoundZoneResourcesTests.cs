// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using TrakHound.Api;
using TrakHound.Zones;

namespace TrakHound.Instance.Zones.Tests
{
    public class TrakHoundZoneResourcesTests
    {
        [Theory]
        [InlineData(TrakHoundApiRequestSourceType.Api, "entities", "api:entities")]
        [InlineData(TrakHoundApiRequestSourceType.App, "dashboard", "app:dashboard")]
        [InlineData(TrakHoundApiRequestSourceType.Function, "retention", "function:retention")]
        [InlineData(TrakHoundApiRequestSourceType.Worker, "scheduler", "worker:scheduler")]
        [InlineData(TrakHoundApiRequestSourceType.Instance, null, null)]
        [InlineData(TrakHoundApiRequestSourceType.Http, "127.0.0.1", null)]
        public void Member_Ids_Identify_Modules(TrakHoundApiRequestSourceType type, string id, string expected)
        {
            Assert.Equal(expected, TrakHoundZoneResources.GetMemberId(new TrakHoundApiRequestSource(type, id)));
        }

        [Theory]
        [InlineData("*", "anything", true)]
        [InlineData("api:entities:*", "api:entities:/objects", true)]
        [InlineData("api:entities:*", "api:entities-2:/objects", false)]
        [InlineData("api:entities*", "api:entities-2:/objects", true)]
        [InlineData("api:entities", "api:entities", true)]
        [InlineData("api:entities", "api:entities:/objects", false)]
        [InlineData("", "api:entities", false)]
        [InlineData("api:*", "", false)]
        public void Patterns_Match_Exactly_Or_By_Prefix(string pattern, string value, bool expected)
        {
            Assert.Equal(expected, TrakHoundZoneResources.IsPatternMatch(pattern, value));
        }

        [Theory]
        [InlineData(TrakHoundApiMethod.GET, "read")]
        [InlineData(TrakHoundApiMethod.HEAD, "read")]
        [InlineData(TrakHoundApiMethod.OPTIONS, "read")]
        [InlineData(TrakHoundApiMethod.POST, "write")]
        [InlineData(TrakHoundApiMethod.PUT, "write")]
        [InlineData(TrakHoundApiMethod.DELETE, "write")]
        public void Api_Methods_Require_Read_Or_Write(TrakHoundApiMethod method, string expected)
        {
            Assert.Equal(expected, TrakHoundZoneResources.GetApiPermission(method));
        }

        [Fact]
        public void Resource_Ids_Are_Normalized()
        {
            Assert.Equal("api:entities:/", TrakHoundZoneResources.GetApiResourceId("entities", null));
            Assert.Equal("api:entities:/objects", TrakHoundZoneResources.GetApiResourceId("entities", "objects"));
            Assert.Equal("volume:shared:/data/file.json", TrakHoundZoneResources.GetVolumeResourceId("shared", "\\data\\file.json"));
            Assert.Equal("manage:configuration:service:redis", TrakHoundZoneResources.GetManageResourceId("configuration", "service:redis"));
        }

        [Fact]
        public async Task The_Zone_Context_Is_Restored_When_An_Async_Method_Returns()
        {
            var caller = new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Function, "ai-function");
            var manager = new TrakHoundZoneManager(new InMemoryConfigurationProfile(), null);

            Assert.Null(TrakHoundZoneContext.Current);

            TrakHoundApiRequestSource inside = null;
            async Task CallService()
            {
                TrakHoundZoneContext.Enter(manager, caller);
                await Task.Yield();
                inside = TrakHoundZoneContext.Current?.Caller;
            }

            await CallService();

            Assert.Same(caller, inside);
            Assert.Null(TrakHoundZoneContext.Current);
        }
    }

    public class TrakHoundPackageCapabilityScannerTests
    {
        [Fact]
        public void Finds_Code_That_Reaches_Outside_The_Client()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"trakhound-zone-scan-{Guid.NewGuid()}");
            Directory.CreateDirectory(directory);

            try
            {
                // This test assembly uses File and Process (see UsesTheFileSystemAndProcesses). It is copied under a new name,
                // because assemblies that the Instance also has are skipped
                File.Copy(typeof(TrakHoundPackageCapabilityScannerTests).Assembly.Location, Path.Combine(directory, "Sample.Module.dll"));
                File.WriteAllText(Path.Combine(directory, "NotAnAssembly.dll"), "not an assembly");

                var capabilities = TrakHoundPackageCapabilityScanner.ScanDirectory(directory);

                var fileSystem = Assert.Single(capabilities, o => o.Id == "file-system");
                Assert.Contains(fileSystem.Evidence, o => o.StartsWith("System.IO.File ") && o.EndsWith("(Sample.Module.dll)"));
                Assert.Contains(capabilities, o => o.Id == "processes");
                Assert.Contains(capabilities, o => o.Id == "environment");
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void An_Empty_Or_Missing_Directory_Has_No_Capabilities()
        {
            Assert.Empty(TrakHoundPackageCapabilityScanner.ScanDirectory(null));
            Assert.Empty(TrakHoundPackageCapabilityScanner.ScanDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
        }

        // Referenced by the scan test (never run)
        internal static void UsesTheFileSystemAndProcesses()
        {
            File.Exists("x");
            Process.GetProcesses();
            Environment.GetEnvironmentVariable("x");
        }
    }
}
