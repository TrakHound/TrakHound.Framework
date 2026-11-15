// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Deployments;
using TrakHound.Instance.Pages.Configuration;
using TrakHound.Instance.Pages.Deployments;
using TrakHound.Workers;

namespace TrakHound.Instance.Deployments.Tests
{
    /// <summary>
    /// A Workspace exported as a Deployment and installed as the target's Profile
    /// </summary>
    public class WorkspaceExportTests : IDisposable
    {
        private readonly string _root;
        private readonly string _workspacePath;
        private readonly string _staging;
        private readonly string _target;
        private readonly WorkspaceConfigurationTarget _workspace;


        public WorkspaceExportTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "trakhound-workspace-export-" + Guid.NewGuid().ToString("N"));
            _workspacePath = Path.Combine(_root, "workspaces", "site-b");
            _staging = Path.Combine(_root, "staging");
            _target = Path.Combine(_root, "target");

            Write(_workspacePath, "instance.config.yaml", "name: Site B\nhttpPort: 9000\n");
            Write(_workspacePath, "logs.config.yaml", "minimumLevel: Debug\n");
            Write(_workspacePath, "variables.config.yaml", "variables:\n  host: plc-b\n");

            Write(_target, "config/default/instance.config.yaml", "name: Target\nhttpPort: 8472\n");
            Write(_target, "config/default/worker/old.yaml", "id: old\npackageId: Test.Worker\n");
            Write(_target, "config/default/security/admins.yaml", "id: admins\n");
            Directory.CreateDirectory(Path.Combine(_target, "packages"));

            _workspace = new WorkspaceConfigurationTarget("site-b", _workspacePath, null);
            _workspace.Profile.Add(new TrakHoundWorkerConfiguration { Id = "w1", Name = "Line 1", PackageId = "Test.Worker", PackageVersion = "1.0.0" }, true);
        }

        public void Dispose()
        {
            _workspace.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        [Fact]
        public async Task Stage_LeavesOutInstanceSettings()
        {
            var errors = await new WorkspaceExport(null, null).Stage(_workspace, Array.Empty<WorkspacePackage>(), _staging, "default", includeSettings: false);

            Assert.Empty(errors);
            var profile = Path.Combine(WorkspaceExport.GetConfigurationPath(_staging), "default");
            Assert.True(File.Exists(Path.Combine(profile, "worker", "w1.yaml")));
            Assert.True(File.Exists(Path.Combine(profile, "variables.config.yaml")));
            Assert.False(File.Exists(Path.Combine(profile, "instance.config.yaml")));
            Assert.False(File.Exists(Path.Combine(profile, "logs.config.yaml")));
        }

        [Fact]
        public async Task Stage_IncludesInstanceSettings_WhenAsked()
        {
            await new WorkspaceExport(null, null).Stage(_workspace, Array.Empty<WorkspacePackage>(), _staging, "default", includeSettings: true);

            var profile = Path.Combine(WorkspaceExport.GetConfigurationPath(_staging), "default");
            Assert.True(File.Exists(Path.Combine(profile, "instance.config.yaml")));
            Assert.True(File.Exists(Path.Combine(profile, "logs.config.yaml")));
        }

        [Fact]
        public async Task Install_ReplacesTheWorkspaceCategories_KeepsTheTargetsSettingsAndSecurity()
        {
            var result = TrakHoundDeployment.Install(await Export(includeSettings: false), InstallOptions());
            Assert.True(result.Success, string.Join("; ", result.Errors));

            var profile = Path.Combine(_target, "config", "default");
            Assert.True(File.Exists(Path.Combine(profile, "worker", "w1.yaml")));
            Assert.False(File.Exists(Path.Combine(profile, "worker", "old.yaml")));
            Assert.True(File.Exists(Path.Combine(profile, "security", "admins.yaml")));
            Assert.Contains("plc-b", File.ReadAllText(Path.Combine(profile, "variables.config.yaml")));
            Assert.Contains("httpPort: 8472", File.ReadAllText(Path.Combine(profile, "instance.config.yaml")));
            Assert.False(Directory.Exists(Path.Combine(_target, "config", "site-b")));
        }

        [Fact]
        public async Task Install_WithInstanceSettings_ReplacesTheTargetsSettings()
        {
            var result = TrakHoundDeployment.Install(await Export(includeSettings: true), InstallOptions());
            Assert.True(result.Success, string.Join("; ", result.Errors));

            Assert.Contains("httpPort: 9000", File.ReadAllText(Path.Combine(_target, "config", "default", "instance.config.yaml")));
        }

        [Fact]
        public async Task GetPackages_WithoutAnInstalledVersionOrPackageSource_IsMissing()
        {
            var packages = await new WorkspaceExport(null, null).GetPackages(_workspace);

            var package = Assert.Single(packages);
            Assert.Equal("Test.Worker", package.PackageId);
            Assert.Equal(WorkspacePackageSource.Missing, package.Source);
            Assert.Single(package.UsedBy);
        }

        [Fact]
        public async Task Stage_ReportsMissingPackages()
        {
            var export = new WorkspaceExport(null, null);
            var errors = await export.Stage(_workspace, await export.GetPackages(_workspace), _staging, "default", includeSettings: false);

            Assert.Contains(errors, o => o.Contains("Test.Worker"));
        }

        [Fact]
        public void StageConfiguration_UsesTheDevicesVariables_LeavesTheWorkspaceUnchanged()
        {
            var device = new WorkspaceDevice { Label = "Line 3", DeviceId = TrakHound.Licenses.TrakHoundDeviceId.Compute("line-3"), Variables = { ["plcAddress"] = "10.0.3.10" } };
            Write(_workspacePath, "variables.config.yaml", "variables:\n  host: plc-b\n  plcAddress: 10.0.0.1\n");

            WorkspaceExport.StageConfiguration(_workspace, WorkspaceExport.GetConfigurationPath(_staging), "default", false, device);

            var staged = TrakHound.Configurations.TrakHoundConfigurationVariables.Read(Path.Combine(WorkspaceExport.GetConfigurationPath(_staging), "default"));
            Assert.Equal("10.0.3.10", staged["plcAddress"]);
            Assert.Equal("plc-b", staged["host"]);
            Assert.Equal("10.0.0.1", TrakHound.Configurations.TrakHoundConfigurationVariables.Read(_workspacePath)["plcAddress"]);
        }

        [Fact]
        public void CreateDeviceDeployments_OneFilePerDevice_WithItsDeviceIdVariablesAndLicenses()
        {
            using var signer = TrakHound.Licenses.TrakHoundLicenseSigner.Generate();
            var line3 = new WorkspaceDevice { Label = "Line 3", DeviceId = TrakHound.Licenses.TrakHoundDeviceId.Compute("line-3"), Variables = { ["host"] = "plc-3" } };
            var line4 = new WorkspaceDevice { Label = "Line 4", DeviceId = TrakHound.Licenses.TrakHoundDeviceId.Compute("line-4"), Variables = { ["host"] = "plc-4" } };

            var licenses = new TrakHound.Licenses.TrakHoundLicenseFile();
            licenses.Licenses.Add(new TrakHound.Licenses.TrakHoundLicenseFileEntry { ProductId = "Test.Product", LicenseCode = signer.Sign("Test.Product", line3.DeviceId, 1, null) });
            licenses.Licenses.Add(new TrakHound.Licenses.TrakHoundLicenseFileEntry { ProductId = "Test.Product", LicenseCode = signer.Sign("Test.Product", line4.DeviceId, 1, null) });

            Directory.CreateDirectory(WorkspaceExport.GetPackagesPath(_staging));
            var deployment = new TrakHoundDeployment { ProfileId = "plant-a", Version = "1.0.0", BuildDate = DateTime.UtcNow };
            var output = Path.Combine(_root, "output");

            var results = WorkspaceExport.CreateDeviceDeployments(_workspace, new[] { line3, line4 }, deployment,
                new WorkspaceDeviceExportOptions { ProfileName = "default", IncludeSecrets = true, Licenses = licenses }, _staging, output);

            Assert.Equal(2, results.Count);
            Assert.All(results, o => Assert.True(o.Result.Success, string.Join("; ", o.Result.Errors)));
            Assert.True(File.Exists(Path.Combine(output, "plant-a-Line-3-1.0.0.thd")));

            foreach (var (device, filePath, result) in results)
            {
                var bytes = File.ReadAllBytes(filePath);
                Assert.Equal(device.DeviceId, TrakHoundDeployment.ReadInformationFromDeployment(bytes).TargetDeviceId);
                Assert.Equal(1, result.Contents.LicenseCount);

                using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
                using var reader = new StreamReader(archive.GetEntry("config/default/variables.config.yaml")!.Open());
                Assert.Contains(device.Variables["host"], reader.ReadToEnd());
            }
        }

        [Fact]
        public void GetDeviceFilename_ReplacesCharactersThatCantBeInAFilename()
        {
            var deployment = new TrakHoundDeployment { ProfileId = "plant-a", Version = "1.0.0" };

            Assert.Equal("plant-a-Line-3-Press-1.0.0.thd", WorkspaceExport.GetDeviceFilename(deployment, new WorkspaceDevice { Label = "Line 3 Press" }));
            Assert.Equal("plant-a-a-b-1.0.0.thd", WorkspaceExport.GetDeviceFilename(deployment, new WorkspaceDevice { Label = "a/b" }));
        }

        [Theory]
        [InlineData("default", true)]
        [InlineData("site-b", true)]
        [InlineData("..", false)]
        [InlineData("a/b", false)]
        [InlineData("a\\b", false)]
        [InlineData(" ", false)]
        [InlineData(null, false)]
        public void IsValidProfileName_AllowsADirectoryName(string name, bool expected)
        {
            Assert.Equal(expected, WorkspaceExport.IsValidProfileName(name));
        }


        private async Task<byte[]> Export(bool includeSettings)
        {
            var errors = await new WorkspaceExport(null, null).Stage(_workspace, Array.Empty<WorkspacePackage>(), _staging, "default", includeSettings);
            Assert.Empty(errors);

            var deployment = new TrakHoundDeployment { ProfileId = "site-b", Version = "1.0.0", BuildDate = DateTime.UtcNow };
            var outputPath = Path.Combine(_root, "site-b.thd");
            var result = deployment.CreateFile(outputPath, new TrakHoundDeploymentCreateOptions
            {
                ConfigurationPath = WorkspaceExport.GetConfigurationPath(_staging),
                PackagesPath = WorkspaceExport.GetPackagesPath(_staging),
                Profiles = new[] { "default" },
                Categories = WorkspaceExport.GetCategories(_staging, "default"),
                IncludeSecrets = true
            });
            Assert.True(result.Success, string.Join("; ", result.Errors));

            return File.ReadAllBytes(outputPath);
        }

        private TrakHoundDeploymentInstallOptions InstallOptions()
        {
            return new TrakHoundDeploymentInstallOptions
            {
                ConfigurationPath = Path.Combine(_target, "config"),
                PackagesPath = Path.Combine(_target, "packages")
            };
        }

        private static void Write(string root, string relativePath, string contents)
        {
            var path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }
    }
}
