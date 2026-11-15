// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Services;
using TrakHound.Workers;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// How Configurations are identified (by Category and Id, and by the file they were read from) and how errors are reported
    /// </summary>
    public class ConfigurationProfileTests : IDisposable
    {
        private const string WorkerCategory = TrakHoundWorkerConfiguration.ConfigurationCategory;
        private const string ServiceCategory = TrakHoundServiceConfiguration.ConfigurationCategory;

        // The watcher checks for changes every 2 seconds
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private readonly string _root;
        private readonly string _profilePath;
        private readonly TrakHoundConfigurationProfile _profile;
        private readonly List<string> _errors = new List<string>();


        public ConfigurationProfileTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trakhound-profile-tests-{Guid.NewGuid()}");
            _profilePath = Path.Combine(_root, "test");
            Directory.CreateDirectory(_profilePath);

            _profile = new TrakHoundConfigurationProfile("test", null, _profilePath);
            _profile.ErrorReceived += (s, message) => { lock (_errors) _errors.Add(message); };
        }

        public void Dispose()
        {
            _profile.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        private string WriteWorkerFile(string fileName, string id, string name)
        {
            var directory = Path.Combine(_profilePath, WorkerCategory);
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, fileName);
            File.WriteAllText(path, $"id: {id}\nname: {name}\npackageId: Test.Worker\n");
            return path;
        }

        private string GetWorkerName(string id) => _profile.Get<TrakHoundWorkerConfiguration>(WorkerCategory, id)?.Name;

        private static async Task<bool> WaitFor(Func<bool> condition)
        {
            var stop = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < stop)
            {
                if (condition()) return true;
                await Task.Delay(100);
            }

            return condition();
        }


        [Fact]
        public void The_Same_Id_In_Different_Categories_Are_Separate_Configurations()
        {
            _profile.Add(new TrakHoundWorkerConfiguration { Id = "mqtt", Name = "Worker", PackageId = "Test.Worker" }, true);
            _profile.Add(new TrakHoundServiceConfiguration { Id = "mqtt", Name = "Service", PackageId = "Test.Service" }, true);

            Assert.Equal("Worker", _profile.Get<TrakHoundWorkerConfiguration>(WorkerCategory, "mqtt")?.Name);
            Assert.Equal("Service", _profile.Get<TrakHoundServiceConfiguration>(ServiceCategory, "mqtt")?.Name);
            Assert.Equal(2, _profile.Configurations.Count());

            _profile.Remove(WorkerCategory, "mqtt");

            Assert.Null(_profile.Get<TrakHoundWorkerConfiguration>(WorkerCategory, "mqtt"));
            Assert.Equal("Service", _profile.Get<TrakHoundServiceConfiguration>(ServiceCategory, "mqtt")?.Name);
            Assert.True(File.Exists(Path.Combine(_profilePath, ServiceCategory, "mqtt.yaml")));
        }

        [Fact]
        public void An_Edit_Is_Saved_To_The_File_It_Was_Read_From()
        {
            var path = WriteWorkerFile("mqtt-worker.yaml", "mqtt", "Before");
            _profile.Load<TrakHoundWorkerConfiguration>(WorkerCategory);

            // Built in code (ex. by the UI), so it has no Path
            _profile.Add(new TrakHoundWorkerConfiguration { Id = "mqtt", Name = "After", PackageId = "Test.Worker" }, true);

            Assert.Contains("After", File.ReadAllText(path));
            Assert.False(File.Exists(Path.Combine(_profilePath, WorkerCategory, "mqtt.yaml")), "A second file was written");
        }

        [Fact]
        public void Removing_Deletes_The_File_It_Was_Read_From()
        {
            var path = WriteWorkerFile("mqtt-worker.yaml", "mqtt", "Worker");
            _profile.Load<TrakHoundWorkerConfiguration>(WorkerCategory);

            _profile.Remove(WorkerCategory, "mqtt");

            Assert.Null(GetWorkerName("mqtt"));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public async Task Deleting_A_File_Whose_Name_Is_Not_The_Id_Unloads_It()
        {
            var path = WriteWorkerFile("mqtt-worker.yaml", "mqtt", "Worker");
            _profile.Load<TrakHoundWorkerConfiguration>(WorkerCategory);
            Assert.Equal("Worker", GetWorkerName("mqtt"));

            File.Delete(path);

            Assert.True(await WaitFor(() => GetWorkerName("mqtt") == null), "The Configuration was not unloaded");
        }

        [Fact]
        public void Loading_A_Category_Again_Does_Not_Reload_It()
        {
            WriteWorkerFile("worker-1.yaml", "worker-1", "Worker");

            var added = 0;
            _profile.ConfigurationAdded += (s, configuration) => Interlocked.Increment(ref added);

            _profile.Load<TrakHoundWorkerConfiguration>(WorkerCategory);
            _profile.Load<TrakHoundWorkerConfiguration>(WorkerCategory);

            Assert.Equal(1, added);
        }

        [Fact]
        public void Invalid_Yaml_Is_Reported()
        {
            WriteWorkerFile("good.yaml", "good", "Good");
            var badPath = Path.Combine(_profilePath, WorkerCategory, "bad.yaml");
            File.WriteAllText(badPath, "id: bad\nname: [unclosed\n");

            _profile.Load<TrakHoundWorkerConfiguration>(WorkerCategory);

            Assert.Equal("Good", GetWorkerName("good"));
            lock (_errors) Assert.Contains(_errors, o => o.Contains(badPath));
        }

        [Fact]
        public void Configurations_Is_A_Snapshot()
        {
            _profile.Add(new TrakHoundWorkerConfiguration { Id = "worker-1", Name = "Worker", PackageId = "Test.Worker" });

            foreach (var configuration in _profile.Configurations)
            {
                // Would throw "Collection was modified" if enumerating the live collection
                _profile.Add(new TrakHoundWorkerConfiguration { Id = "worker-2", Name = "Worker", PackageId = "Test.Worker" });
            }

            Assert.Equal(2, _profile.Configurations.Count());
        }

        [Fact]
        public async Task ConfigurationChanged_Tells_Loaded_Added_Updated_And_Removed_Apart()
        {
            var path = WriteWorkerFile("worker-1.yaml", "worker-1", "Worker");

            var changes = new List<(TrakHoundConfigurationChangeType, string)>();
            _profile.ConfigurationChanged += (s, args) => { lock (changes) changes.Add((args.ChangeType, args.Configuration.Id)); };

            _profile.Load<TrakHoundWorkerConfiguration>(WorkerCategory);
            _profile.Add(new TrakHoundWorkerConfiguration { Id = "worker-2", Name = "Worker", PackageId = "Test.Worker" }, true);
            _profile.Add(new TrakHoundWorkerConfiguration { Id = "worker-2", Name = "Changed", PackageId = "Test.Worker" }, true);
            _profile.Remove(WorkerCategory, "worker-2");

            // An edit to a file outside of the Profile
            File.WriteAllText(path, File.ReadAllText(path).Replace("name: Worker", "name: Edited"));
            Assert.True(await WaitFor(() => GetWorkerName("worker-1") == "Edited"), "The edit was not loaded");

            lock (changes)
            {
                Assert.Equal(new[]
                {
                    (TrakHoundConfigurationChangeType.Loaded, "worker-1"),
                    (TrakHoundConfigurationChangeType.Added, "worker-2"),
                    (TrakHoundConfigurationChangeType.Updated, "worker-2"),
                    (TrakHoundConfigurationChangeType.Removed, "worker-2"),
                    (TrakHoundConfigurationChangeType.Updated, "worker-1")
                }, changes);
            }
        }

        [Fact]
        public void A_Configuration_Of_Another_Type_Is_Not_Added_To_A_Loaded_Category()
        {
            _profile.Load<TrakHoundWorkerConfiguration>(WorkerCategory);

            _profile.Add(new OtherWorkerConfiguration { Id = "other" }, true);

            Assert.Null(_profile.Get(WorkerCategory, "other"));
            Assert.False(File.Exists(Path.Combine(_profilePath, WorkerCategory, "other.yaml")));
            lock (_errors) Assert.Contains(_errors, o => o.Contains("OtherWorkerConfiguration"));
        }

        [Fact]
        public void Loading_A_Category_As_Another_Type_Is_Reported()
        {
            _profile.Load<TrakHoundWorkerConfiguration>(WorkerCategory);
            _profile.Load<OtherWorkerConfiguration>(WorkerCategory);

            lock (_errors) Assert.Contains(_errors, o => o.Contains("already Loaded as TrakHoundWorkerConfiguration"));
        }

        [Fact]
        public void Read_By_Id_Reads_The_File_Without_Loading_It()
        {
            WriteWorkerFile("worker-1.yaml", "worker-1", "Worker");
            WriteWorkerFile("mqtt-worker.yaml", "mqtt", "Mqtt");
            _profile.Load<TrakHoundWorkerConfiguration>(WorkerCategory);
            WriteWorkerFile("worker-2.yaml", "worker-2", "Not Loaded");

            // A loaded Configuration is read from its file (even if the file isn't named by its Id)
            Assert.Equal("Mqtt", _profile.Read<TrakHoundWorkerConfiguration>(WorkerCategory, "mqtt")?.Name);
            Assert.Equal("Not Loaded", _profile.Read<TrakHoundWorkerConfiguration>(WorkerCategory, "worker-2")?.Name);
            Assert.Null(_profile.Read<TrakHoundWorkerConfiguration>(WorkerCategory, "missing"));
        }

        [Fact]
        public void Get_Returns_Empty_For_A_Category_Without_Configurations()
        {
            Assert.Empty(_profile.Get<TrakHoundWorkerConfiguration>(WorkerCategory));
        }

        [Fact]
        public void Saving_Leaves_No_Temporary_File()
        {
            _profile.Add(new TrakHoundWorkerConfiguration { Id = "worker-1", Name = "Worker", PackageId = "Test.Worker" }, true);

            var files = Directory.GetFiles(Path.Combine(_profilePath, WorkerCategory)).Select(Path.GetFileName);
            Assert.Equal(new[] { "worker-1.yaml" }, files);
        }


        private class OtherWorkerConfiguration : ITrakHoundConfiguration
        {
            public string Id { get; set; }
            public string Category => WorkerCategory;
            public string Path { get; set; }
            public string Hash => Id;
        }
    }
}
