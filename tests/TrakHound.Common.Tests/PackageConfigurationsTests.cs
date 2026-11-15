// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Functions;
using TrakHound.Packages;
using TrakHound.Workers;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// The default Configuration a Package gets when it is installed (Admin interface, or th packages install --configure)
    /// </summary>
    public class PackageConfigurationsTests
    {
        private static TrakHoundPackage CreatePackage(string category, params (string Key, object Value)[] metadata)
        {
            var builder = new TrakHoundPackageBuilder { Id = "Test.Package", Version = "1.0.0", Category = category };
            foreach (var (key, value) in metadata) builder.SetMetadata(key, value);
            return builder.Build();
        }


        [Fact]
        public void App_UsesDefaultNameRouteAndVolume()
        {
            var package = CreatePackage("app", (".defaultName", "Test"), (".defaultRoute", "test"), (".defaultVolumeId", "test.volume"));

            var configuration = Assert.IsType<TrakHoundAppConfiguration>(TrakHoundPackageConfigurations.CreateDefault(package));

            Assert.False(string.IsNullOrEmpty(configuration.Id));
            Assert.Equal("Test", configuration.Name);
            Assert.Equal("Test.Package", configuration.PackageId);
            Assert.Equal("*", configuration.PackageVersion);
            Assert.Equal("test", configuration.Route);
            Assert.Equal("test.volume", configuration.VolumeId);
            Assert.Null(configuration.RouterId); // Empty, so the Group's Router (or the Default Router) is used
        }

        [Fact]
        public void Api_UsesDefaultRouteAndVolume()
        {
            var package = CreatePackage("api", (".defaultRoute", "test/api"), (".defaultVolumeId", "test.volume"));

            var configuration = Assert.IsType<TrakHoundApiConfiguration>(TrakHoundPackageConfigurations.CreateDefault(package));

            Assert.Equal("test/api", configuration.Route);
            Assert.Equal("test.volume", configuration.VolumeId);
        }

        [Fact]
        public void Function_UsesPackageIdAsFunctionId()
        {
            var configuration = Assert.IsType<TrakHoundFunctionConfiguration>(TrakHoundPackageConfigurations.CreateDefault(CreatePackage("function")));

            Assert.Equal("Test.Package", configuration.FunctionId);
            Assert.Equal("Test.Package", TrakHoundPackageConfigurations.GetPackageId(configuration));
        }

        [Fact]
        public void Worker_GetsConfiguration()
        {
            var configuration = Assert.IsType<TrakHoundWorkerConfiguration>(TrakHoundPackageConfigurations.CreateDefault(CreatePackage("worker", (".defaultVolumeId", "test.volume"))));

            Assert.Equal("test.volume", configuration.VolumeId);
        }

        [Theory]
        [InlineData("service")]
        [InlineData("identity")]
        public void OtherCategories_GetNoConfiguration(string category)
        {
            Assert.Null(TrakHoundPackageConfigurations.CreateDefault(CreatePackage(category)));
        }

        [Theory]
        [InlineData("app")]
        [InlineData("api")]
        [InlineData("function")]
        [InlineData("worker")]
        public void SetGroupId_PutsTheConfigurationInTheGroup(string category)
        {
            var configuration = TrakHoundPackageConfigurations.CreateDefault(CreatePackage(category));

            TrakHoundPackageConfigurations.SetGroupId(configuration, "line-1");

            Assert.Equal("line-1", configuration.GetType().GetProperty("GroupId")?.GetValue(configuration));
        }

        [Fact]
        public void WithDefaultVolumeId_FillsAnEmptyVolumeIdInACopy()
        {
            var package = CreatePackage("function", (".defaultVolumeId", "test.volume"));
            var existing = new TrakHoundFunctionConfiguration { Id = "config-1", FunctionId = "fn-1", GroupId = "line-1", PackageId = "Test.Package", PackageVersion = "*" };
            var hash = existing.Hash;

            var updated = Assert.IsType<TrakHoundFunctionConfiguration>(TrakHoundPackageConfigurations.WithDefaultVolumeId(existing, package));

            Assert.Equal("test.volume", updated.VolumeId);
            Assert.Equal("config-1", updated.Id);
            Assert.Equal("fn-1", updated.FunctionId);
            Assert.Equal("line-1", updated.GroupId);

            // The Configuration Profile only saves a Configuration whose Hash differs from the one it holds
            Assert.Null(existing.VolumeId);
            Assert.Equal(hash, existing.Hash);
            Assert.NotEqual(hash, updated.Hash);
        }

        [Fact]
        public void WithDefaultVolumeId_CopiesEveryAppProperty()
        {
            var package = CreatePackage("app", (".defaultVolumeId", "test.volume"));
            var existing = new TrakHoundAppConfiguration { Id = "config-1", Path = "app.yaml", Name = "Name", Description = "Description", Route = "route", RouterId = "router", GroupId = "line-1", PackageId = "Test.Package", PackageVersion = "1.0.0", Parameters = new Dictionary<string, object> { { "a", "b" } } };

            var updated = Assert.IsType<TrakHoundAppConfiguration>(TrakHoundPackageConfigurations.WithDefaultVolumeId(existing, package));

            existing.VolumeId = "test.volume";
            Assert.Equal(existing.Hash, updated.Hash);
            Assert.Equal("app.yaml", updated.Path);
        }

        [Theory]
        [InlineData("existing.volume", "Test.Package", "*")] // Keeps a Volume ID that is set
        [InlineData(null, "Other.Package", "*")] // Another Package
        [InlineData(null, "Test.Package", "0.9.0")] // Pinned to another version
        public void WithDefaultVolumeId_LeavesOtherConfigurations(string volumeId, string packageId, string packageVersion)
        {
            var package = CreatePackage("worker", (".defaultVolumeId", "test.volume"));
            var existing = new TrakHoundWorkerConfiguration { Id = "config-1", VolumeId = volumeId, PackageId = packageId, PackageVersion = packageVersion };

            Assert.Null(TrakHoundPackageConfigurations.WithDefaultVolumeId(existing, package));
        }

        [Fact]
        public void WithDefaultVolumeId_ReturnsNullWithoutADefault()
        {
            var existing = new TrakHoundWorkerConfiguration { Id = "config-1", PackageId = "Test.Package", PackageVersion = "*" };

            Assert.Null(TrakHoundPackageConfigurations.WithDefaultVolumeId(existing, CreatePackage("worker")));
        }
    }
}
