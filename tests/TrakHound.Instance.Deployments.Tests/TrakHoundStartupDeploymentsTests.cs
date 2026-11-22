// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Deployments;

namespace TrakHound.Instance.Deployments.Tests
{
    public class TrakHoundStartupDeploymentsTests : IDisposable
    {
        private readonly string _root;
        private readonly string _configurationPath;
        private readonly string _packagesPath;
        private readonly string _sourcePath;


        public TrakHoundStartupDeploymentsTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "trakhound-startup-deployments-" + Guid.NewGuid().ToString("N"));
            _configurationPath = Path.Combine(_root, "target", "config");
            _packagesPath = Path.Combine(_root, "target", "packages");
            _sourcePath = Path.Combine(_root, "deployments");

            Directory.CreateDirectory(Path.Combine(_configurationPath, "default"));
            Directory.CreateDirectory(_packagesPath);
            Directory.CreateDirectory(_sourcePath);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }


        [Fact]
        public void Install_PendingDirectory_InstallsOnceAndMovesFile()
        {
            var file = CreateDeployment("site-a.thd", "1.0.0");
            var startup = new TrakHoundStartupDeployments(_sourcePath, _configurationPath);

            var results = startup.Install(CreateOptions());

            Assert.Single(results);
            Assert.True(results[0].Success, string.Join("; ", results[0].Errors));
            Assert.Equal("default", results[0].ProfileId);
            Assert.NotNull(results[0].BackupPath);
            Assert.True(File.Exists(Path.Combine(_configurationPath, "default", "api", "test.config.yaml")));
            Assert.False(File.Exists(file));
            Assert.Single(Directory.GetFiles(Path.Combine(_sourcePath, TrakHoundStartupDeployments.InstalledDirectory)));
            Assert.Single(startup.ReadRecords());

            // The same file again is not installed a second time
            File.Copy(Directory.GetFiles(Path.Combine(_sourcePath, TrakHoundStartupDeployments.InstalledDirectory))[0], file);
            Assert.Empty(startup.GetPendingFiles());
            Assert.Empty(startup.Install(CreateOptions()));
            Assert.False(File.Exists(file));
        }

        [Fact]
        public void InformationPath_IsTheConfigurationDirectory()
        {
            // Kept with the configuration (ex. a Docker volume), not in the program directory that a new image replaces
            var startup = new TrakHoundStartupDeployments(_sourcePath, _configurationPath);

            Assert.Equal(_configurationPath, startup.InformationPath);
            Assert.Equal(Path.GetDirectoryName(startup.RecordPath), startup.InformationPath);
        }

        [Fact]
        public void DefaultVariablePath_IsBesideTheConfigurationDirectory()
        {
            Assert.Equal(Path.Combine(_root, "target", "variables"), InstanceApplication.GetDefaultVariablePath(_configurationPath));
            Assert.Equal(Path.Combine(_root, "target", "variables"), InstanceApplication.GetDefaultVariablePath(_configurationPath + Path.DirectorySeparatorChar));
        }

        [Fact]
        public void Install_InvalidFile_RecordsFailureAndMovesToFailed()
        {
            File.WriteAllText(Path.Combine(_sourcePath, "broken.thd"), "not a deployment");
            var startup = new TrakHoundStartupDeployments(_sourcePath, _configurationPath);

            var results = startup.Install(CreateOptions());

            Assert.Single(results);
            Assert.False(results[0].Success);
            Assert.NotEmpty(results[0].Errors);
            Assert.Single(Directory.GetFiles(Path.Combine(_sourcePath, TrakHoundStartupDeployments.FailedDirectory)));
            Assert.Empty(startup.Install(CreateOptions()));
        }

        [Fact]
        public void Install_SingleFile_IsLeftInPlaceAndInstalledOnce()
        {
            var file = CreateDeployment("site-a.thd", "1.0.0");
            var startup = new TrakHoundStartupDeployments(file, _configurationPath);

            Assert.True(startup.Install(CreateOptions()).Single().Success);
            Assert.True(File.Exists(file));
            Assert.Empty(startup.Install(CreateOptions()));
        }

        [Fact]
        public void Install_MultipleFiles_InstallsInNameOrder()
        {
            CreateDeployment("02-second.thd", "2.0.0");
            CreateDeployment("01-first.thd", "1.0.0");
            var startup = new TrakHoundStartupDeployments(_sourcePath, _configurationPath);

            var results = startup.Install(CreateOptions());

            Assert.Equal(new[] { "1.0.0", "2.0.0" }, results.Select(o => o.Version));
            Assert.Equal("2.0.0", TrakHoundDeployment.ReadInformation(Path.Combine(_root, "target"))?.Version);
        }


        private TrakHoundDeploymentInstallOptions CreateOptions()
        {
            return new TrakHoundDeploymentInstallOptions
            {
                ConfigurationPath = _configurationPath,
                PackagesPath = _packagesPath,
                InformationPath = Path.Combine(_root, "target"),
                BackupPath = Path.Combine(_root, "target", "backup")
            };
        }

        /// <summary>
        /// Create a Deployment of a "default" Profile containing one Api configuration (the Version is in the file, so each Version has a different hash)
        /// </summary>
        private string CreateDeployment(string filename, string version)
        {
            var sourceConfiguration = Path.Combine(_root, "source", version, "config");
            var sourcePackages = Path.Combine(_root, "source", version, "packages");
            Directory.CreateDirectory(Path.Combine(sourceConfiguration, "default", "api"));
            Directory.CreateDirectory(sourcePackages);
            File.WriteAllText(Path.Combine(sourceConfiguration, "default", "api", "test.config.yaml"), $"id: test\nversion: {version}\n");

            var deployment = new TrakHoundDeployment
            {
                ProfileId = "default",
                Version = version,
                BuildDate = DateTime.UtcNow
            };

            var outputPath = Path.Combine(_sourcePath, filename);
            var result = deployment.CreateFile(outputPath, new TrakHoundDeploymentCreateOptions
            {
                ConfigurationPath = sourceConfiguration,
                PackagesPath = sourcePackages,
                Profiles = new[] { "default" }
            });

            Assert.True(result.Success, string.Join("; ", result.Errors));
            return outputPath;
        }
    }
}
