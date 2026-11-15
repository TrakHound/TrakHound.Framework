// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Licenses;

namespace TrakHound.Common.Tests
{
    public class LicenseCodeTests : IDisposable
    {
        private const string ProductId = "contoso.widget";
        private const string DeviceId = "b5dqt63re2q665x6gfb9wjwqfc90w89zh8qnndd2cpdqhhby61eg";
        private static readonly DateTime Now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly TrakHoundLicenseSigner _signer = TrakHoundLicenseSigner.Generate();

        public void Dispose() => _signer.Dispose();


        private TrakHoundLicenseValidationResult Verify(string licenseCode, string productId = ProductId, string deviceId = DeviceId, DateTime? now = null, string publicKey = null)
        {
            return TrakHoundLicenseVerifier.Verify(new[] { publicKey ?? _signer.ExportPublicKeyPem() }, productId, licenseCode, deviceId, now ?? Now);
        }


        [Fact]
        public void Signed_Code_Is_Valid()
        {
            var code = _signer.Sign(ProductId, DeviceId, 5, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var result = Verify(code);

            Assert.True(result.IsValid, result.Message);
            Assert.Equal(5, result.AssignmentLimit);
            Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), result.ExpirationDate);
            Assert.False(result.IsPermanent);
            Assert.False(result.IsAnyDevice);
        }

        [Fact]
        public void Code_Is_About_170_Characters()
        {
            var code = _signer.Sign(ProductId, DeviceId, 1, null);

            Assert.Equal(169, code.Length);
            Assert.Matches("^[0-9A-HJKMNP-TV-Z]{5}(-[0-9A-HJKMNP-TV-Z]{1,5})+$", code);
        }

        [Fact]
        public void Code_Reads_However_It_Is_Typed()
        {
            var code = _signer.Sign(ProductId, DeviceId, 1, null);
            var typed = code.Replace("-", "").ToLowerInvariant().Replace('1', 'l').Replace('0', 'o');

            Assert.True(Verify(typed).IsValid);
            Assert.Equal(code, TrakHoundLicenseCode.Format(typed));
        }

        [Fact]
        public void Changed_Code_Is_Invalid()
        {
            var code = _signer.Sign(ProductId, DeviceId, 1, new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Replace("-", "");

            // Change each character of the signed fields (ex. the Assignment Limit or the Expiration)
            for (var i = 0; i < 39; i++)
            {
                var replacement = code[i] == 'Z' ? 'Y' : 'Z';
                var changed = code.Substring(0, i) + replacement + code.Substring(i + 1);

                Assert.False(Verify(changed).IsValid, $"Character {i}");
            }
        }

        [Fact]
        public void Code_From_Another_Key_Is_Invalid()
        {
            using (var other = TrakHoundLicenseSigner.Generate())
            {
                var code = other.Sign(ProductId, DeviceId, 1, null);
                var result = Verify(code);

                Assert.False(result.IsValid);
                Assert.Contains("wasn't issued by the Publisher", result.Message);
            }
        }

        [Fact]
        public void Replaced_Key_Is_Accepted_While_Listed()
        {
            using (var previous = TrakHoundLicenseSigner.Generate())
            {
                var code = previous.Sign(ProductId, DeviceId, 1, null);
                var keys = new[] { _signer.ExportPublicKeyPem(), previous.ExportPublicKeyPem() };

                Assert.True(TrakHoundLicenseVerifier.Verify(keys, ProductId, code, DeviceId, Now).IsValid);
            }
        }

        [Fact]
        public void Code_For_Another_Product_Is_Invalid()
        {
            var code = _signer.Sign("contoso.other", DeviceId, 1, null);

            Assert.False(Verify(code).IsValid);
            Assert.True(Verify(code, "CONTOSO.OTHER").IsValid);
        }

        [Fact]
        public void Code_For_Another_Device_Is_Invalid()
        {
            var code = _signer.Sign(ProductId, TrakHoundDeviceId.Compute("OTHER-MACHINE"), 1, null);
            var result = Verify(code);

            Assert.False(result.IsValid);
            Assert.Contains("different Device", result.Message);
        }

        [Fact]
        public void Device_ID_Is_Matched_However_It_Is_Typed()
        {
            var code = _signer.Sign(ProductId, DeviceId.ToUpperInvariant(), 1, null);

            Assert.True(Verify(code).IsValid);
            Assert.True(Verify(code, deviceId: TrakHoundBase32Group(DeviceId)).IsValid);
        }

        [Fact]
        public void Any_Device_Code_Is_Valid_On_Every_Device()
        {
            var code = _signer.SignForAnyDevice(ProductId, 3, null);
            var result = Verify(code, deviceId: TrakHoundDeviceId.Compute("ANY-MACHINE"));

            Assert.True(result.IsValid, result.Message);
            Assert.True(result.IsAnyDevice);
        }

        [Fact]
        public void Expired_Code_Is_Invalid()
        {
            var expiration = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
            var code = _signer.Sign(ProductId, DeviceId, 1, expiration);

            Assert.False(Verify(code).IsValid);
            Assert.True(Verify(code, now: expiration.AddSeconds(-1)).IsValid);
            Assert.False(Verify(code, now: expiration).IsValid);
        }

        [Fact]
        public void Permanent_Code_Never_Expires()
        {
            var code = _signer.Sign(ProductId, DeviceId, 1, null);
            var result = Verify(code, now: new DateTime(2105, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            Assert.True(result.IsValid);
            Assert.True(result.IsPermanent);
        }

        [Fact]
        public void Expiration_Without_A_Kind_Is_UTC()
        {
            var code = _signer.Sign(ProductId, DeviceId, 1, new DateTime(2027, 1, 1));

            Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), TrakHoundLicenseCode.Parse(code).ExpirationDate);
        }

        [Fact]
        public void Signer_Refuses_Bad_Values()
        {
            Assert.Throws<ArgumentException>(() => _signer.Sign(ProductId, "", 1, null));
            Assert.Throws<ArgumentException>(() => _signer.Sign(ProductId, "not a device id!", 1, null));
            Assert.Throws<ArgumentException>(() => _signer.Sign("", DeviceId, 1, null));
            Assert.Throws<ArgumentException>(() => _signer.Sign(ProductId, DeviceId, 0, null));
            Assert.Throws<ArgumentException>(() => _signer.Sign(ProductId, DeviceId, 65536, null));
            Assert.Throws<ArgumentException>(() => _signer.Sign(ProductId, DeviceId, 1, new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        }

        [Fact]
        public void Private_Key_Round_Trips()
        {
            using (var loaded = TrakHoundLicenseSigner.FromPem(_signer.ExportPrivateKeyPem()))
            {
                Assert.Equal(_signer.KeyId, loaded.KeyId);
                Assert.True(Verify(loaded.Sign(ProductId, DeviceId, 1, null)).IsValid);
            }

            Assert.Throws<ArgumentException>(() => TrakHoundLicenseSigner.FromPem(_signer.ExportPublicKeyPem()));
        }

        [Fact]
        public void Not_A_Code_Is_Invalid()
        {
            Assert.Null(TrakHoundLicenseCode.Parse(null));
            Assert.Null(TrakHoundLicenseCode.Parse("91870-12345-CD2YX"));
            Assert.Null(TrakHoundLicenseCode.Parse(new string('U', 141)));
            Assert.False(Verify("91870-12345-CD2YX").IsValid);
        }


        private static string TrakHoundBase32Group(string value) => string.Join("-", Enumerable.Range(0, (value.Length + 3) / 4).Select(i => value.Substring(i * 4, Math.Min(4, value.Length - i * 4))));
    }

    public class DeviceIdTests
    {
        [Fact]
        public void Device_ID_Never_Changes()
        {
            // Every License issued depends on these values. If this fails, the calculation changed: put it back
            Assert.Equal("b5dqt63re2q665x6gfb9wjwqfc90w89zh8qnndd2cpdqhhby61eg", TrakHoundDeviceId.Compute("TEST-MACHINE"));
            Assert.Equal("ntraadwtart3ac0g2t576njyt2d2y5nw763eqrp6739fdbhmdhgg", TrakHoundDeviceId.Compute("my-container-01"));
        }

        [Fact]
        public void Variable_Is_Used_In_A_Container()
        {
            var result = TrakHoundDeviceId.Resolve("abc123", "my-container-01", true, "/.dockerenv");

            Assert.Equal(TrakHoundDeviceId.Compute("my-container-01"), result.DeviceId);
            Assert.Equal(TrakHoundDeviceIdSource.Variable, result.Source);
            Assert.Null(result.Warning);
        }

        [Fact]
        public void Variable_Is_Ignored_Without_DOTNET_RUNNING_IN_CONTAINER()
        {
            var result = TrakHoundDeviceId.Resolve("TEST-MACHINE", "my-container-01", false, "/.dockerenv");

            Assert.Equal(TrakHoundDeviceId.Compute("TEST-MACHINE"), result.DeviceId);
            Assert.Equal(TrakHoundDeviceIdSource.MachineName, result.Source);
            Assert.Contains("DOTNET_RUNNING_IN_CONTAINER", result.Warning);
        }

        [Fact]
        public void Variable_Is_Ignored_When_No_Container_Is_Detected()
        {
            // Both variables can be set on a normal machine, so a sign from the file system is needed too
            var result = TrakHoundDeviceId.Resolve("TEST-MACHINE", "my-container-01", true, null);

            Assert.Equal(TrakHoundDeviceId.Compute("TEST-MACHINE"), result.DeviceId);
            Assert.Contains("no container was detected", result.Warning);
        }

        [Fact]
        public void Machine_Name_Is_Used_Without_The_Variable()
        {
            var result = TrakHoundDeviceId.Resolve("TEST-MACHINE", null, false, null);

            Assert.Equal(TrakHoundDeviceId.Compute("TEST-MACHINE"), result.DeviceId);
            Assert.Null(result.Warning);
        }
    }

    public class ContainerDetectionTests
    {
        private static string Detect(Dictionary<string, string> files)
        {
            return TrakHoundContainerDetection.DetectLinux(path => files.ContainsKey(path), path => files.GetValueOrDefault(path));
        }

        private const string HostMountInfo =
            "22 1 8:1 / / rw,relatime shared:1 - ext4 /dev/sda1 rw\n" +
            "30 22 0:26 / /proc rw,nosuid,nodev,noexec,relatime shared:5 - proc proc rw\n" +
            // A machine that runs Docker sees the containers' mounts too
            "400 22 0:50 / /var/lib/docker/overlay2/abc/merged rw,relatime - overlay overlay rw,lowerdir=/var/lib/docker/overlay2/l/X\n";


        [Fact]
        public void Docker() => Assert.Equal("/.dockerenv", Detect(new Dictionary<string, string> { ["/.dockerenv"] = "" }));

        [Fact]
        public void Podman() => Assert.Equal("/run/.containerenv", Detect(new Dictionary<string, string> { ["/run/.containerenv"] = "" }));

        [Fact]
        public void Kubernetes_cgroup_v1()
        {
            var files = new Dictionary<string, string> { ["/proc/1/cgroup"] = "12:memory:/kubepods/burstable/pod1234/abcdef\n" };
            Assert.Equal("/proc/1/cgroup", Detect(files));
        }

        [Fact]
        public void Containerd_cgroup_v2()
        {
            var files = new Dictionary<string, string>
            {
                ["/proc/1/cgroup"] = "0::/\n",
                ["/proc/self/mountinfo"] =
                    "800 700 0:60 / / rw,relatime - ext4 /dev/vda1 rw\n" +
                    "810 800 253:1 /var/lib/kubelet/pods/0a1b/etc-hosts /etc/hosts rw,relatime - ext4 /dev/vda1 rw\n"
            };
            Assert.Equal("/proc/self/mountinfo", Detect(files));
        }

        [Fact]
        public void Overlay_Root()
        {
            var files = new Dictionary<string, string>
            {
                ["/proc/1/cgroup"] = "0::/\n",
                ["/proc/self/mountinfo"] = "500 400 0:52 / / rw,relatime master:1 - overlay overlay rw,lowerdir=/x\n"
            };
            Assert.Equal("/proc/self/mountinfo", Detect(files));
        }

        [Fact]
        public void Normal_Machine()
        {
            var files = new Dictionary<string, string> { ["/proc/1/cgroup"] = "0::/init.scope\n", ["/proc/self/mountinfo"] = HostMountInfo };
            Assert.Null(Detect(files));
        }
    }

    public class LicenseFileTests
    {
        [Fact]
        public void Description_Is_Read_And_Written()
        {
            var file = TrakHoundLicenseFile.Parse("{ \"licenses\": [ { \"productId\": \"contoso.widget\", \"licenseCode\": \"AAAAA\", \"description\": \" PO-4471 \" }, { \"productId\": \"contoso.widget\", \"licenseCode\": \"BBBBB\" } ] }");

            Assert.Empty(file.Errors);
            Assert.Equal("PO-4471", file.Licenses[0].Description);
            Assert.Null(file.Licenses[1].Description);

            var copy = TrakHoundLicenseFile.Parse(file.ToJson());
            Assert.Equal("PO-4471", copy.Licenses[0].Description);
            Assert.Null(copy.Licenses[1].Description);
            Assert.DoesNotContain("\"description\": null", file.ToJson());
        }

        [Fact]
        public void Plain_Text_Has_No_Description()
        {
            var file = TrakHoundLicenseFile.Parse("contoso.widget AAAAA");

            Assert.Empty(file.Errors);
            Assert.Null(file.Licenses[0].Description);
        }
    }

    public class LicenseManagerTests : IDisposable
    {
        private const string ProductId = "contoso.widget";

        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"trakhound-license-tests-{Guid.NewGuid()}");
        private readonly TrakHoundLicenseSigner _signer = TrakHoundLicenseSigner.Generate();
        private readonly string _deviceId = TrakHoundDeviceId.Get();

        public void Dispose()
        {
            _signer.Dispose();
            try { Directory.Delete(_directory, true); } catch { }
        }


        private string Sign(int limit, DateTime? expiration = null) => _signer.Sign(ProductId, _deviceId, limit, expiration ?? DateTime.UtcNow.AddDays(30));


        [Fact]
        public void Assignments_Are_Limited()
        {
            var manager = new TrakHoundLicenseManager("contoso", _directory);
            var code = Sign(2);
            manager.AddLicense(ProductId, _deviceId, code);

            var a = manager.AddAssignment(ProductId, _deviceId, "a");
            var b = manager.AddAssignment(ProductId, _deviceId, "b");
            var c = manager.AddAssignment(ProductId, _deviceId, "c");

            Assert.True(manager.AssignLicense(code, a.AssignmentId).Success);
            Assert.True(manager.AssignLicense(code, b.AssignmentId).Success);
            Assert.False(manager.AssignLicense(code, c.AssignmentId).Success);
            Assert.Equal(2, manager.GetLicenseByLicenseCode(code).AssignmentCount);

            manager.UnassignLicense(a.AssignmentId);
            Assert.True(manager.AssignLicense(code, c.AssignmentId).Success);
        }

        [Fact]
        public void Licenses_And_Assignments_Are_Saved()
        {
            var code = Sign(2);
            var first = new TrakHoundLicenseManager("contoso", _directory);
            first.AddLicense(ProductId, _deviceId, code);
            var assignment = first.AddAssignment(ProductId, _deviceId, "a");
            first.AssignLicense(code, assignment.AssignmentId);

            var second = new TrakHoundLicenseManager("contoso", _directory);
            Assert.Equal(1, second.GetLicenseByLicenseCode(code).AssignmentCount);

            // A Module adds its Assignment again when it starts, and gets its License back
            Assert.Equal(code, second.AddAssignment(ProductId, _deviceId, "a").LicenseCode);
        }

        [Fact]
        public void Renew_Keeps_Assignments()
        {
            var manager = new TrakHoundLicenseManager("contoso", _directory);
            var original = Sign(2);
            manager.AddLicense(ProductId, _deviceId, original);
            var assignment = manager.AddAssignment(ProductId, _deviceId, "a");
            manager.AssignLicense(original, assignment.AssignmentId);

            var renewed = Sign(2, DateTime.UtcNow.AddDays(400));
            manager.RenewLicense(ProductId, _deviceId, original, renewed);

            Assert.Null(manager.GetLicenseByLicenseCode(original));
            Assert.Equal(renewed, manager.GetAssignment(assignment.AssignmentId).LicenseCode);
            Assert.Equal(1, manager.GetLicenseByLicenseCode(renewed).AssignmentCount);
        }

        [Fact]
        public void License_For_Another_Device_Or_Product_Is_Refused()
        {
            var manager = new TrakHoundLicenseManager("contoso", _directory);

            var otherDevice = _signer.Sign(ProductId, TrakHoundDeviceId.Compute("OTHER-MACHINE"), 1, null);
            Assert.Throws<ArgumentException>(() => manager.AddLicense(ProductId, _deviceId, otherDevice));
            Assert.Throws<ArgumentException>(() => manager.AddLicense("contoso.other", _deviceId, Sign(1)));
            Assert.Throws<ArgumentException>(() => manager.AddLicense(ProductId, _deviceId, "91870-12345-CD2YX"));
        }

        [Fact]
        public void Expired_License_Can_Not_Be_Assigned()
        {
            var manager = new TrakHoundLicenseManager("contoso", _directory);
            var code = Sign(1, DateTime.UtcNow.AddDays(-1));
            manager.AddLicense(ProductId, _deviceId, code);

            var assignment = manager.AddAssignment(ProductId, _deviceId, "a");
            var result = manager.AssignLicense(code, assignment.AssignmentId);

            Assert.False(result.Success);
            Assert.Contains("expired", result.Message);
        }

        [Fact]
        public void License_Code_Is_Found_However_It_Is_Typed()
        {
            var manager = new TrakHoundLicenseManager("contoso", _directory);
            var code = Sign(1);
            manager.AddLicense(ProductId, _deviceId, code.Replace("-", "").ToLowerInvariant());

            Assert.Equal(code, manager.GetLicenseByLicenseCode(code).LicenseCode);
        }

        [Fact]
        public void Description_Is_Saved_And_Kept_On_Renew()
        {
            var original = Sign(1);
            var first = new TrakHoundLicenseManager("contoso", _directory);
            first.AddLicense(ProductId, _deviceId, original, "  Line 3\r\nmachines  ");

            // Kept on one line, so it shows in a table cell
            Assert.Equal("Line 3  machines", first.GetLicenseByLicenseCode(original).Description);

            var second = new TrakHoundLicenseManager("contoso", _directory);
            Assert.Equal("Line 3  machines", second.GetLicenseByLicenseCode(original).Description);

            var renewed = Sign(1, DateTime.UtcNow.AddDays(400));
            second.RenewLicense(ProductId, _deviceId, original, renewed);
            Assert.Equal("Line 3  machines", second.GetLicenseByLicenseCode(renewed).Description);
        }

        [Fact]
        public void Adding_A_License_Again_Keeps_Its_Description()
        {
            var manager = new TrakHoundLicenseManager("contoso", _directory);
            var code = Sign(1);

            // An empty Description is filled in, but one that was set (ex. edited on the Licenses page) isn't replaced
            manager.AddLicense(ProductId, _deviceId, code);
            manager.AddLicense(ProductId, _deviceId, code, "From the License File");
            Assert.Equal("From the License File", manager.GetLicenseByLicenseCode(code).Description);

            manager.SetLicenseDescription(code, "Edited");
            manager.AddLicense(ProductId, _deviceId, code, "From the License File");
            Assert.Equal("Edited", manager.GetLicenseByLicenseCode(code).Description);
        }

        [Fact]
        public void Description_Can_Be_Changed_And_Cleared()
        {
            var manager = new TrakHoundLicenseManager("contoso", _directory);
            var code = Sign(1);
            manager.AddLicense(ProductId, _deviceId, code, "First");

            Assert.Equal("Second", manager.SetLicenseDescription(code.ToLowerInvariant(), "Second").Description);
            Assert.Null(manager.SetLicenseDescription(code, "   ").Description);
            Assert.Null(new TrakHoundLicenseManager("contoso", _directory).GetLicenseByLicenseCode(code).Description);

            Assert.Null(manager.SetLicenseDescription(Sign(2), "Not added"));
            Assert.Equal(TrakHoundLicenseInformation.MaxDescriptionLength, manager.SetLicenseDescription(code, new string('x', 500)).Description.Length);
        }

        [Fact]
        public void Verifier_Checks_The_Assignment()
        {
            var manager = new TrakHoundLicenseManager("contoso", _directory);
            var code = Sign(1);
            manager.AddLicense(ProductId, _deviceId, code);
            var assignment = manager.AddAssignment(ProductId, _deviceId, "a");

            Assert.False(TrakHoundLicenseVerifier.VerifyAssignment(_signer.ExportPublicKeyPem(), ProductId, manager, assignment.AssignmentId).IsValid);

            manager.AssignLicense(code, assignment.AssignmentId);
            Assert.True(TrakHoundLicenseVerifier.VerifyAssignment(_signer.ExportPublicKeyPem(), ProductId, manager, assignment.AssignmentId).IsValid);

            using (var other = TrakHoundLicenseSigner.Generate())
            {
                Assert.False(TrakHoundLicenseVerifier.VerifyAssignment(other.ExportPublicKeyPem(), ProductId, manager, assignment.AssignmentId).IsValid);
            }
        }
    }
}
