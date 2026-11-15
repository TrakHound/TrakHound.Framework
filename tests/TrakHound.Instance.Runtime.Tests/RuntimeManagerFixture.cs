// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients;
using TrakHound.Configurations;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Modules;
using TrakHound.Packages;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// A Configuration Profile and Package Manager in a temporary directory, and stand-ins for the rest of what the Managers are created with.
    /// Every Package resolves to a Module whose only Type is ModuleType
    /// </summary>
    public sealed class RuntimeManagerFixture : IDisposable
    {
        private readonly string _root;


        public TrakHoundConfigurationProfile ConfigurationProfile { get; }

        public TrakHoundPackageManager PackageManager { get; }

        public ITrakHoundInstance Instance { get; } = Fake.Create<ITrakHoundInstance>(o => o.On("get_Id", _ => "test-instance"));

        public ITrakHoundModuleProvider ModuleProvider { get; }

        public ITrakHoundClientProvider ClientProvider { get; } = Fake.Create<ITrakHoundClientProvider>(o => o.On("GetClient", _ => Fake.Create<ITrakHoundClient>()));

        public ITrakHoundVolumeProvider VolumeProvider { get; } = Fake.Create<ITrakHoundVolumeProvider>();

        public ITrakHoundVariableProvider VariableProvider { get; } = Fake.Create<ITrakHoundVariableProvider>();

        public ITrakHoundLogProvider LogProvider { get; } = Fake.Create<ITrakHoundLogProvider>(o => o.On("GetLoggerByName", _ => Fake.Create<ITrakHoundLogger>()));


        public RuntimeManagerFixture(Type moduleType, string packageCategory)
        {
            _root = Path.Combine(Path.GetTempPath(), $"trakhound-runtime-tests-{Guid.NewGuid()}");
            var profilePath = Path.Combine(_root, "config", "test");
            Directory.CreateDirectory(profilePath);

            PackageManager = new TrakHoundPackageManager(Path.Combine(_root, "packages"));
            ConfigurationProfile = new TrakHoundConfigurationProfile("test", null, profilePath);

            var package = new TrakHoundPackageBuilder { Id = "Test.Package", Version = "1.0.0", Category = packageCategory, BuildDate = DateTime.UtcNow }.Build();
            var module = Fake.Create<ITrakHoundModule>(o => o
                .On("get_PackageId", _ => package.Id)
                .On("get_PackageVersion", _ => package.Version)
                .On("get_Package", _ => package)
                .On("get_ModuleTypes", _ => new[] { moduleType }));

            var moduleManager = Fake.Create<ITrakHoundModuleManager>(o => o.On("Get", _ => module));
            ModuleProvider = Fake.Create<ITrakHoundModuleProvider>(o => o.On("Get", _ => moduleManager));
        }

        public void Dispose()
        {
            ConfigurationProfile.Dispose();
            PackageManager.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }
    }
}
