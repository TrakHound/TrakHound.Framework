// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.IO.Compression;
using TrakHound.Deployments;

namespace TrakHound.Instance.Deployments.Tests
{
    /// <summary>
    /// Deployments created from a source directory (as "th deployments create" does)
    /// </summary>
    public class TrakHoundDeploymentSourceTests : IDisposable
    {
        private readonly string _root;
        private readonly string _source;
        private readonly string _target;


        public TrakHoundDeploymentSourceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "trakhound-deployment-source-" + Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "source");
            _target = Path.Combine(_root, "target");

            Write(_source, "config/default/instance.config.yaml", "id: source");
            Write(_source, "config/default/apis/reports.yaml", "id: reports");
            Write(_source, "packages/apis/reports/1.0.0/reports.dll", "dll");
            Directory.CreateDirectory(_target);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }


        [Fact]
        public async Task Create_EntryNamesUseForwardSlashes()
        {
            foreach (var bytes in new[] { CreateDeployment(), await CreateDeploymentAsync() })
            {
                using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                {
                    Assert.Contains(archive.Entries, o => o.FullName == "config/default/apis/reports.yaml");
                    Assert.Contains(archive.Entries, o => o.FullName == "packages/apis/reports/1.0.0/reports.dll");
                    Assert.DoesNotContain(archive.Entries, o => o.FullName.Contains('\\'));
                }
            }
        }

        [Fact]
        public void Install_InstallsConfigurationsAndPackages()
        {
            var result = TrakHoundDeployment.Install(CreateDeployment(), _target);

            Assert.True(result.Success, string.Join("; ", result.Errors));
            Assert.Equal("id: source", Read(_target, "config/default/instance.config.yaml"));
            Assert.Equal("id: reports", Read(_target, "config/default/apis/reports.yaml"));
            Assert.Equal("dll", Read(_target, "packages/apis/reports/1.0.0/reports.dll"));
        }


        private byte[] CreateDeployment()
        {
            var result = NewDeployment().Create(_source);
            Assert.True(result.Success, string.Join("; ", result.Errors));
            return result.Content;
        }

        private async Task<byte[]> CreateDeploymentAsync()
        {
            var result = await NewDeployment().CreateAsync(_source);
            Assert.True(result.Success, string.Join("; ", result.Errors));
            return result.Content;
        }

        private static TrakHoundDeployment NewDeployment()
        {
            return new TrakHoundDeployment { ProfileId = "default", Version = "1.0.0", BuildDate = DateTime.UtcNow };
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
