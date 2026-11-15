// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Deployments;

namespace TrakHound.Instance.Deployments.Tests
{
    /// <summary>
    /// Volume files and Package Variables in Deployments
    /// </summary>
    public class TrakHoundDeploymentDataTests : IDisposable
    {
        private readonly string _root;
        private readonly string _source;
        private readonly string _target;


        public TrakHoundDeploymentDataTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "trakhound-deployment-data-" + Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "source");
            _target = Path.Combine(_root, "target");

            foreach (var instance in new[] { _source, _target })
            {
                Directory.CreateDirectory(Path.Combine(instance, "config", "default"));
                Directory.CreateDirectory(Path.Combine(instance, "packages"));
                Directory.CreateDirectory(Path.Combine(instance, "volumes"));
                Directory.CreateDirectory(Path.Combine(instance, "variables"));
            }

            Write(_source, "volumes/reports/settings.json", "{\"interval\":5}");
            Write(_source, "volumes/reports/templates/daily.txt", "daily");
            Write(_source, "volumes/other/data.txt", "not selected");
            Write(_source, "variables/reports.json", "{\"lastRun\":\"source\"}");
            Write(_source, "variables/other.json", "{}");
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }


        [Fact]
        public void Create_OnlySelectedVolumesAndVariables()
        {
            var preview = TrakHoundDeployment.Preview(CreateOptions());

            Assert.Equal(2, preview.VolumeFileCount);
            Assert.Equal(1, preview.VariableCount);
        }

        [Fact]
        public void Create_NoneSelected_IncludesNoData()
        {
            var options = CreateOptions();
            options.Volumes = null;
            options.Variables = null;

            var preview = TrakHoundDeployment.Preview(options);

            Assert.Equal(0, preview.VolumeFileCount);
            Assert.Equal(0, preview.VariableCount);
        }

        [Fact]
        public void Create_IgnoresIdsOutsideTheDirectory()
        {
            var options = CreateOptions();
            options.Volumes = new[] { "..", "../config", "reports" };

            Assert.Equal(2, TrakHoundDeployment.Preview(options).VolumeFileCount);
        }

        [Fact]
        public void Install_AddsAndReplacesFiles_KeepsOtherFiles()
        {
            Write(_target, "volumes/reports/settings.json", "{\"interval\":60}");
            Write(_target, "volumes/reports/history.json", "target only");
            Write(_target, "variables/reports.json", "{\"lastRun\":\"target\"}");

            var bytes = CreateDeployment();
            var plan = TrakHoundDeployment.CreatePlan(bytes, InstallOptions(backup: false));

            Assert.Equal(1, plan.Count(TrakHoundDeploymentPlan.VolumeKind, TrakHoundDeploymentPlanAction.Update));
            Assert.Equal(1, plan.Count(TrakHoundDeploymentPlan.VolumeKind, TrakHoundDeploymentPlanAction.Add));
            Assert.Equal(1, plan.Count(TrakHoundDeploymentPlan.VariableKind, TrakHoundDeploymentPlanAction.Update));

            var result = TrakHoundDeployment.Install(bytes, InstallOptions(backup: false));

            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.Equal("{\"interval\":5}", Read(_target, "volumes/reports/settings.json"));
            Assert.Equal("daily", Read(_target, "volumes/reports/templates/daily.txt"));
            Assert.Equal("target only", Read(_target, "volumes/reports/history.json"));
            Assert.False(File.Exists(Path.Combine(_target, "volumes", "other", "data.txt")));
            Assert.Equal("{\"lastRun\":\"source\"}", Read(_target, "variables/reports.json"));
            Assert.False(File.Exists(Path.Combine(_target, "variables", "other.json")));
        }

        [Fact]
        public void Restore_ReturnsReplacedFilesAndRemovesAddedFiles()
        {
            Write(_target, "volumes/reports/settings.json", "{\"interval\":60}");

            var result = TrakHoundDeployment.Install(CreateDeployment(), InstallOptions(backup: true));
            Assert.True(result.Success, string.Join("; ", result.Errors));

            var restore = TrakHoundDeployment.Restore(result.BackupPath, InstallOptions(backup: false));

            Assert.True(restore.Success, string.Join("; ", restore.Errors));
            Assert.Equal("{\"interval\":60}", Read(_target, "volumes/reports/settings.json"));
            Assert.False(Directory.Exists(Path.Combine(_target, "volumes", "reports", "templates")));
            Assert.True(Directory.Exists(Path.Combine(_target, "volumes")));
            Assert.False(File.Exists(Path.Combine(_target, "variables", "reports.json")));
        }

        [Fact]
        public void Plan_NoVolumePath_WarnsAndSkips()
        {
            var options = InstallOptions(backup: false);
            options.VolumesPath = null;
            options.VariablesPath = null;

            var bytes = CreateDeployment();
            var plan = TrakHoundDeployment.CreatePlan(bytes, options);
            var result = TrakHoundDeployment.Install(bytes, options);

            Assert.Equal(2, plan.Warnings.Count(o => o.Contains("will not be installed")));
            Assert.DoesNotContain(plan.Items, o => o.IsVolume || o.IsVariable);
            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.False(File.Exists(Path.Combine(_target, "volumes", "reports", "settings.json")));
        }


        private TrakHoundDeploymentCreateOptions CreateOptions()
        {
            return new TrakHoundDeploymentCreateOptions
            {
                ConfigurationPath = Path.Combine(_source, "config"),
                PackagesPath = Path.Combine(_source, "packages"),
                VolumesPath = Path.Combine(_source, "volumes"),
                VariablesPath = Path.Combine(_source, "variables"),
                Profiles = new[] { "default" },
                Volumes = new[] { "reports" },
                Variables = new[] { "reports" }
            };
        }

        private TrakHoundDeploymentInstallOptions InstallOptions(bool backup)
        {
            return new TrakHoundDeploymentInstallOptions
            {
                ConfigurationPath = Path.Combine(_target, "config"),
                PackagesPath = Path.Combine(_target, "packages"),
                VolumesPath = Path.Combine(_target, "volumes"),
                VariablesPath = Path.Combine(_target, "variables"),
                BackupPath = backup ? Path.Combine(_target, "backup") : null
            };
        }

        private byte[] CreateDeployment()
        {
            var deployment = new TrakHoundDeployment { ProfileId = "default", Version = "1.0.0", BuildDate = DateTime.UtcNow };
            var outputPath = Path.Combine(_root, "data.thd");

            var result = deployment.CreateFile(outputPath, CreateOptions());
            Assert.True(result.Success, string.Join("; ", result.Errors));

            return File.ReadAllBytes(outputPath);
        }

        private static void Write(string instance, string relativePath, string contents)
        {
            var path = Path.Combine(instance, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        private static string Read(string instance, string relativePath) => File.ReadAllText(Path.Combine(instance, relativePath));
    }
}
