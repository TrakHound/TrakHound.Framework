// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Instances;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Instance Settings can be Variable references (ex. "httpPort: ${port}"), so a deployment for many devices can give each its own values.
    /// References are resolved when read and written back while their settings keep their values
    /// </summary>
    public class InstanceConfigurationReferenceTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "trakhound-instance-references-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, string> _variables = new Dictionary<string, string> { ["port"] = "9100", ["siteName"] = "Plant B", ["bad"] = "not-a-number" };

        private string ConfigurationPath => Path.Combine(_root, TrakHoundInstanceConfiguration.Filename);


        public InstanceConfigurationReferenceTests()
        {
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }


        [Fact]
        public void Read_ResolvesReferences()
        {
            var configuration = Read("httpPort: ${port}\nname: ${siteName} Instance\nrateLimits:\n  api:\n    permitLimit: ${port}\n");

            Assert.Equal(9100, configuration.HttpPort);
            Assert.Equal("Plant B Instance", configuration.Name);
            Assert.Equal(9100, configuration.RateLimits.Api.PermitLimit);
            Assert.Equal("${port}", configuration.References["httpPort"]);
            Assert.Equal("${port}", configuration.References["rateLimits.api.permitLimit"]);
            Assert.Empty(configuration.UnresolvedReferences);
        }

        [Fact]
        public void Save_WritesReferencesBack_WhileTheirValuesAreUnchanged()
        {
            var configuration = Read("httpPort: ${port}\nname: ${siteName}\n");
            configuration.Description = "Changed";
            configuration.Save(ConfigurationPath, false);

            var yaml = File.ReadAllText(ConfigurationPath);
            Assert.Contains("httpPort: \"${port}\"", yaml);
            Assert.Contains("name: \"${siteName}\"", yaml);
            Assert.DoesNotContain("9100", yaml);
            Assert.Contains("description: Changed", yaml);
        }

        [Fact]
        public void Save_AChangedValue_ReplacesItsReference()
        {
            var configuration = Read("httpPort: ${port}\n");
            configuration.HttpPort = 9200;
            configuration.Save(ConfigurationPath, false);

            var yaml = File.ReadAllText(ConfigurationPath);
            Assert.Contains("httpPort: 9200", yaml);
            Assert.DoesNotContain("${port}", yaml);
        }

        [Fact]
        public void Read_AnUndefinedVariable_UsesTheDefault_AndKeepsTheReference()
        {
            var configuration = Read("httpPort: ${missing}\nname: ${alsoMissing}\n");

            Assert.Equal(new TrakHoundInstanceConfiguration().HttpPort, configuration.HttpPort);
            Assert.Null(configuration.Name);
            Assert.Equal(2, configuration.UnresolvedReferences.Count);

            configuration.Save(ConfigurationPath, false);
            var yaml = File.ReadAllText(ConfigurationPath);
            Assert.Contains("httpPort: \"${missing}\"", yaml);
            Assert.Contains("name: \"${alsoMissing}\"", yaml);
        }

        [Fact]
        public void Read_AValueThatIsntValid_UsesTheDefaults()
        {
            var configuration = Read("httpPort: ${bad}\nname: ${siteName}\n");

            Assert.NotNull(configuration);
            Assert.Equal(new TrakHoundInstanceConfiguration().HttpPort, configuration.HttpPort);
            Assert.Contains(configuration.UnresolvedReferences, o => o.Contains("${bad}"));
        }

        [Fact]
        public void Read_WithoutAResolver_UsesTheDefaults()
        {
            File.WriteAllText(ConfigurationPath, "httpPort: ${port}\nsystemApiEnabled: false\n");
            var configuration = TrakHoundInstanceConfiguration.Read(ConfigurationPath);

            Assert.Equal(new TrakHoundInstanceConfiguration().HttpPort, configuration.HttpPort);
            Assert.False(configuration.SystemApiEnabled);
            Assert.Equal("${port}", configuration.References["httpPort"]);
        }

        [Fact]
        public void SetReference_IsWritten_RemoveReference_WritesTheValue()
        {
            var configuration = Read("httpPort: 8472\n");
            configuration.SetReference("httpPort", "${port}");
            configuration.SetReference("name", "${siteName}");
            Assert.Contains("httpPort: \"${port}\"", configuration.ToYaml());
            Assert.Contains("name: \"${siteName}\"", configuration.ToYaml());

            configuration.RemoveReference("httpPort");
            Assert.Contains("httpPort: 8472", configuration.ToYaml());
        }

        [Fact]
        public void InstanceId_IsReadFromAnOlderFile_AndNotSaved()
        {
            var configuration = Read("instanceId: i-legacy0001\nhttpPort: 8472\n");

            Assert.Equal("i-legacy0001", configuration.FileInstanceId);
            Assert.Equal("i-legacy0001", configuration.InstanceId);
            Assert.DoesNotContain("instanceId", configuration.ToYaml());
        }

        [Fact]
        public void Identity_KeepsTheIdInItsOwnFile()
        {
            // An older settings file : its ID moves to the file
            var configuration = Read("instanceId: i-legacy0001\n");
            Assert.Equal("i-legacy0001", TrakHoundInstanceIdentity.Load(_root, configuration));
            Assert.Equal("i-legacy0001", File.ReadAllText(TrakHoundInstanceIdentity.GetPath(_root)));

            // The file wins over a settings file from another device (ex. deployed with an older format)
            var deployed = Read("instanceId: i-otherdevice\n");
            Assert.Equal("i-legacy0001", TrakHoundInstanceIdentity.Load(_root, deployed));
            Assert.Equal("i-legacy0001", deployed.InstanceId);
        }

        [Fact]
        public void Identity_ANewInstance_GetsItsOwnId()
        {
            var first = Path.Combine(_root, "first");
            var second = Path.Combine(_root, "second");

            var a = TrakHoundInstanceIdentity.Load(first, Read("httpPort: 8472\n"));
            var b = TrakHoundInstanceIdentity.Load(second, Read("httpPort: 8472\n"));

            Assert.StartsWith("i-", a);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Variables_Resolve()
        {
            Environment.SetEnvironmentVariable("TRAKHOUND_TEST_RESOLVE", "from-env");
            try
            {
                Assert.Equal("9100", TrakHoundConfigurationVariables.Resolve("${port}", _variables));
                Assert.Equal("from-env/Plant B", TrakHoundConfigurationVariables.Resolve("${env:TRAKHOUND_TEST_RESOLVE}/${siteName}", _variables));
                Assert.Equal("${literal}", TrakHoundConfigurationVariables.Resolve("$${literal}", _variables));
                Assert.Null(TrakHoundConfigurationVariables.Resolve("${missing}", _variables));
                Assert.Equal("plain", TrakHoundConfigurationVariables.Resolve("plain", _variables));
            }
            finally
            {
                Environment.SetEnvironmentVariable("TRAKHOUND_TEST_RESOLVE", null);
            }
        }


        private TrakHoundInstanceConfiguration Read(string yaml)
        {
            File.WriteAllText(ConfigurationPath, yaml);
            var configuration = TrakHoundInstanceConfiguration.Read(ConfigurationPath, value => TrakHoundConfigurationVariables.Resolve(value, _variables));
            Assert.NotNull(configuration);
            return configuration;
        }
    }
}
