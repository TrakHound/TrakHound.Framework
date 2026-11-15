// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Deployments;
using TrakHound.Licenses;

namespace TrakHound.Instance.Deployments.Tests
{
    /// <summary>
    /// A Deployment for one Device (Target Device ID) : it can only be installed on that Device, and carries that Device's Licenses
    /// </summary>
    public class TrakHoundDeploymentLicenseTests : IDisposable
    {
        private const string ProductId = "TrakHound.Test.Product";

        private readonly string _root;
        private readonly string _source;
        private readonly string _target;
        private readonly TrakHoundLicenseSigner _signer = TrakHoundLicenseSigner.Generate();

        // Licenses are checked against this machine's Device ID (the License Manager reads only its own)
        private readonly string _deviceId = TrakHoundDeviceId.Get();
        private readonly string _otherDeviceId = TrakHoundDeviceId.Compute("another-device");


        public TrakHoundDeploymentLicenseTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "trakhound-deployment-licenses-" + Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "source");
            _target = Path.Combine(_root, "target");

            Write(_source, "config/default/worker/w1.yaml", "id: w1\npackageId: Test.Worker\n");
            Write(_target, "config/default/worker/old.yaml", "id: old\npackageId: Test.Worker\n");
            Directory.CreateDirectory(Path.Combine(_source, "packages"));
            Directory.CreateDirectory(Path.Combine(_target, "packages"));
        }

        public void Dispose()
        {
            _signer.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        [Fact]
        public void Create_IncludesOnlyTheTargetDevicesLicenses()
        {
            var options = CreateOptions(_deviceId);
            var preview = TrakHoundDeployment.Preview(options);

            // This Device's and the any-Device License (not the other Device's)
            Assert.Equal(2, preview.LicenseCount);
            Assert.Equal(1, preview.LicensesExcluded);
        }

        [Fact]
        public void Create_WithoutATargetDeviceId_IncludesNoLicenses()
        {
            var preview = TrakHoundDeployment.Preview(CreateOptions(null));

            Assert.Equal(0, preview.LicenseCount);
            Assert.Equal(3, preview.LicensesExcluded);

            var plan = TrakHoundDeployment.CreatePlan(CreateDeployment((string)null), InstallOptions());
            Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
            Assert.Null(plan.Information.TargetDeviceId);
            Assert.DoesNotContain(plan.Items, o => o.IsLicense);
        }

        [Fact]
        public void Create_RejectsAnInvalidDeviceId()
        {
            var deployment = new TrakHoundDeployment { ProfileId = "site-b", Version = "1.0.0", BuildDate = DateTime.UtcNow };
            var result = deployment.CreateFile(Path.Combine(_root, "invalid.thd"), CreateOptions("not a device id!"));

            Assert.False(result.Success);
        }

        [Fact]
        public void Install_OnAnotherDevice_FailsWithoutChangingAnything()
        {
            var result = TrakHoundDeployment.Install(CreateDeployment(_otherDeviceId), InstallOptions());

            Assert.False(result.Success);
            Assert.Contains(result.Errors, o => o.Contains(_otherDeviceId));
            Assert.True(File.Exists(Path.Combine(_target, "config", "default", "worker", "old.yaml")));
            Assert.False(File.Exists(Path.Combine(_target, "config", "default", "worker", "w1.yaml")));
            Assert.False(Directory.Exists(Path.Combine(_target, "config", "default", "licenses")));
        }

        [Fact]
        public void Install_OnTheTargetDevice_AddsItsLicenses_KeepsTheTargetsOwn()
        {
            // A License the target already has (for another Product)
            var existingCode = _signer.Sign("TrakHound.Test.Existing", _deviceId, 1, null);
            new TrakHoundLicenseManager(TrakHoundLicenseManagers.DefaultPublisherId, LicensesPath).AddLicense("TrakHound.Test.Existing", _deviceId, existingCode);

            var result = TrakHoundDeployment.Install(CreateDeployment(_deviceId), InstallOptions());
            Assert.True(result.Success, string.Join("; ", result.Errors));

            var licenses = new TrakHoundLicenseManager(TrakHoundLicenseManagers.DefaultPublisherId, LicensesPath).GetLicenses().ToList();
            Assert.Equal(3, licenses.Count);
            Assert.Contains(licenses, o => o.ProductId == "TrakHound.Test.Existing");
            Assert.Equal(2, licenses.Count(o => o.ProductId == ProductId));
            Assert.True(File.Exists(Path.Combine(_target, "config", "default", "worker", "w1.yaml")));
        }

        [Fact]
        public void Plan_ALicenseTheTargetHas_IsUnchanged()
        {
            var bytes = CreateDeployment(_deviceId);
            Assert.True(TrakHoundDeployment.Install(bytes, InstallOptions()).Success);

            var plan = TrakHoundDeployment.CreatePlan(bytes, InstallOptions());
            Assert.Equal(2, plan.Count(TrakHoundDeploymentPlan.LicenseKind, TrakHoundDeploymentPlanAction.Unchanged));
            Assert.Equal(0, plan.Count(TrakHoundDeploymentPlan.LicenseKind, TrakHoundDeploymentPlanAction.Add));
        }

        [Fact]
        public void FullDeployment_NeverIncludesOrRemovesLicensesFolders()
        {
            // The source's own Licenses aren't deployed as configuration
            Write(_source, "config/default/licenses/trakhound.json", "{\"licenses\":[]}");

            // The target's Licenses are kept by a Deployment of every Category
            var existingCode = _signer.Sign("TrakHound.Test.Existing", _deviceId, 1, null);
            new TrakHoundLicenseManager(TrakHoundLicenseManagers.DefaultPublisherId, LicensesPath).AddLicense("TrakHound.Test.Existing", _deviceId, existingCode);

            var options = CreateOptions(null);
            options.Licenses = null;
            Assert.DoesNotContain(TrakHoundDeployment.GetCategories(options.ConfigurationPath, new[] { "default" }), o => o == "licenses");

            var plan = TrakHoundDeployment.CreatePlan(CreateDeployment(options), InstallOptions());
            Assert.DoesNotContain(plan.Items, o => o.RelativePath != null && o.RelativePath.Contains("/licenses/"));

            Assert.True(TrakHoundDeployment.Install(CreateDeployment(options), InstallOptions()).Success);
            Assert.Single(new TrakHoundLicenseManager(TrakHoundLicenseManagers.DefaultPublisherId, LicensesPath).GetLicenses());
        }


        private string LicensesPath => Path.Combine(_target, "config", "default", TrakHoundLicenseManagers.DirectoryName);

        private TrakHoundDeploymentCreateOptions CreateOptions(string targetDeviceId)
        {
            var licenses = new TrakHoundLicenseFile();
            licenses.Licenses.Add(new TrakHoundLicenseFileEntry { ProductId = ProductId, LicenseCode = _signer.Sign(ProductId, _deviceId, 1, null) });
            licenses.Licenses.Add(new TrakHoundLicenseFileEntry { ProductId = ProductId, LicenseCode = _signer.Sign(ProductId, _otherDeviceId, 1, null) });
            licenses.Licenses.Add(new TrakHoundLicenseFileEntry { ProductId = ProductId, LicenseCode = _signer.SignForAnyDevice(ProductId, 1, null) });

            return new TrakHoundDeploymentCreateOptions
            {
                ConfigurationPath = Path.Combine(_source, "config"),
                PackagesPath = Path.Combine(_source, "packages"),
                Profiles = new[] { "default" },
                TargetDeviceId = targetDeviceId,
                Licenses = licenses
            };
        }

        private TrakHoundDeploymentInstallOptions InstallOptions()
        {
            return new TrakHoundDeploymentInstallOptions
            {
                ConfigurationPath = Path.Combine(_target, "config"),
                PackagesPath = Path.Combine(_target, "packages")
            };
        }

        private byte[] CreateDeployment(string targetDeviceId) => CreateDeployment(CreateOptions(targetDeviceId));

        private byte[] CreateDeployment(TrakHoundDeploymentCreateOptions options)
        {
            var deployment = new TrakHoundDeployment { ProfileId = "site-b", Version = "1.0.0", BuildDate = DateTime.UtcNow };
            var outputPath = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".thd");

            var result = deployment.CreateFile(outputPath, options);
            Assert.True(result.Success, string.Join("; ", result.Errors));

            return File.ReadAllBytes(outputPath);
        }

        private static void Write(string root, string relativePath, string contents)
        {
            var path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }
    }
}
