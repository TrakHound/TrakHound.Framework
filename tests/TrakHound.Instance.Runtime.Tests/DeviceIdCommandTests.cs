// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Licenses;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// The device-id command shows the Device ID that Licenses are issued for, so a License can be requested for an Instance without an Admin interface
    /// </summary>
    public class DeviceIdCommandTests
    {
        private static readonly TrakHoundDeviceIdInformation _machineName = TrakHoundDeviceId.Resolve("PLANT-PC-04", null, false, null);
        private static readonly TrakHoundDeviceIdInformation _ignoredVariable = TrakHoundDeviceId.Resolve("PLANT-PC-04", "line-1", false, null);
        private static readonly TrakHoundDeviceIdInformation _container = TrakHoundDeviceId.Resolve("3f2a9c", "line-1", true, "/.dockerenv");


        [Fact]
        public void IsCommand_OnlyTheFirstArgument()
        {
            Assert.True(TrakHoundDeviceIdCommand.IsCommand(new[] { "device-id" }));
            Assert.True(TrakHoundDeviceIdCommand.IsCommand(new[] { "device-id", "--json" }));
            Assert.False(TrakHoundDeviceIdCommand.IsCommand(new[] { "default", "device-id" }));
            Assert.False(TrakHoundDeviceIdCommand.IsCommand(Array.Empty<string>()));
        }

        [Fact]
        public void Run_WritesOnlyTheDeviceIdToOutput()
        {
            var (code, output, messages) = Run(_machineName, "device-id");

            Assert.Equal(0, code);
            Assert.Equal(TrakHoundDeviceId.Compute("PLANT-PC-04") + Environment.NewLine, output);
            Assert.Contains("machine name 'PLANT-PC-04'", messages);
        }

        [Fact]
        public void Run_ShowsWhyTheVariableWasIgnored()
        {
            var (code, output, messages) = Run(_ignoredVariable, "device-id");

            Assert.Equal(0, code);
            Assert.Equal(TrakHoundDeviceId.Compute("PLANT-PC-04"), output.Trim());
            Assert.Contains("Warning", messages);
            Assert.Contains(TrakHoundDeviceId.Variable, messages);
        }

        [Fact]
        public void Run_InAContainer_UsesTheVariable()
        {
            var (_, output, messages) = Run(_container, "device-id");

            Assert.Equal(TrakHoundDeviceId.Compute("line-1"), output.Trim());
            Assert.Contains(TrakHoundDeviceId.Variable, messages);
            Assert.DoesNotContain("Warning", messages);
        }

        [Fact]
        public void Run_Json_WritesOneObject()
        {
            var (code, output, messages) = Run(_ignoredVariable, "device-id", "--json");

            Assert.Equal(0, code);
            Assert.Empty(messages);

            using var json = JsonDocument.Parse(output);
            Assert.Equal(TrakHoundDeviceId.Compute("PLANT-PC-04"), json.RootElement.GetProperty("deviceId").GetString());
            Assert.Equal("machineName", json.RootElement.GetProperty("source").GetString());
            Assert.Equal("PLANT-PC-04", json.RootElement.GetProperty("machineName").GetString());
            Assert.NotNull(json.RootElement.GetProperty("warning").GetString());
        }

        [Theory]
        [InlineData("--unknown")]
        [InlineData("extra")]
        public void Run_RejectsOtherArguments(string argument)
        {
            var (code, output, messages) = Run(_machineName, "device-id", argument);

            Assert.Equal(2, code);
            Assert.Empty(output);
            Assert.Contains(TrakHoundDeviceIdCommand.Usage, messages);
        }


        private static (int Code, string Output, string Messages) Run(TrakHoundDeviceIdInformation information, params string[] args)
        {
            var output = new StringWriter();
            var messages = new StringWriter();
            var code = TrakHoundDeviceIdCommand.Run(args, output, messages, information, "PLANT-PC-04");
            return (code, output.ToString(), messages.ToString());
        }
    }
}
