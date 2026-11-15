// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Workers;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Changes made to Configuration files outside of the Profile (ex. by an editor) while the Profile is watching them
    /// </summary>
    public class ConfigurationProfileWatcherTests : IDisposable
    {
        private const string Category = TrakHoundWorkerConfiguration.ConfigurationCategory;

        // The watcher checks for changes every 2 seconds
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private readonly string _root;
        private readonly string _profilePath;
        private readonly TrakHoundConfigurationProfile _profile;


        public ConfigurationProfileWatcherTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trakhound-profile-tests-{Guid.NewGuid()}");
            _profilePath = Path.Combine(_root, "test");
            Directory.CreateDirectory(_profilePath);

            _profile = new TrakHoundConfigurationProfile("test", null, _profilePath);
        }

        public void Dispose()
        {
            _profile.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        private string FilePath(string id) => Path.Combine(_profilePath, Category, $"{id}.yaml");

        private void AddAndWatch(string id, string name)
        {
            _profile.Add(new TrakHoundWorkerConfiguration { Id = id, Name = name, PackageId = "Test.Worker" }, true);
            _profile.Load<TrakHoundWorkerConfiguration>(Category);
            Assert.True(File.Exists(FilePath(id)));
        }

        private string GetName(string id) => _profile.Get<TrakHoundWorkerConfiguration>(Category, id)?.Name;

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
        public async Task A_Rename_Save_Keeps_The_File_And_Reloads_It()
        {
            AddAndWatch("worker-1", "Before");
            var path = FilePath("worker-1");

            // Save the way "sed -i" and many editors do: write a temporary file, then rename it over the original
            var tempPath = Path.Combine(Path.GetDirectoryName(path), "sedA1b2C3");
            File.WriteAllText(tempPath, File.ReadAllText(path).Replace("Before", "After"));
            File.Move(tempPath, path, true);

            Assert.True(await WaitFor(() => GetName("worker-1") == "After"), "The new content was not loaded");

            // Give a (wrong) removal time to happen
            await Task.Delay(TimeSpan.FromSeconds(5));
            Assert.True(File.Exists(path), "The file was deleted");
            Assert.Contains("After", File.ReadAllText(path));
            Assert.Equal("After", GetName("worker-1"));
        }

        [Fact]
        public async Task A_File_Deleted_And_Written_Again_Is_Kept()
        {
            AddAndWatch("worker-2", "Before");
            var path = FilePath("worker-2");
            var content = File.ReadAllText(path);

            File.Delete(path);
            File.WriteAllText(path, content.Replace("Before", "After"));

            Assert.True(await WaitFor(() => GetName("worker-2") == "After"), "The new content was not loaded");

            await Task.Delay(TimeSpan.FromSeconds(5));
            Assert.True(File.Exists(path), "The file was deleted");
            Assert.Equal("After", GetName("worker-2"));
        }

        [Fact]
        public async Task A_File_Deleted_Outside_The_Profile_Is_Unloaded()
        {
            AddAndWatch("worker-3", "Worker");
            var removed = new List<string>();
            _profile.ConfigurationRemoved += (s, configuration) => { lock (removed) removed.Add(configuration.Id); };

            File.Delete(FilePath("worker-3"));

            Assert.True(await WaitFor(() => GetName("worker-3") == null), "The Configuration was not unloaded");
            lock (removed) Assert.Contains("worker-3", removed);
        }

        [Fact]
        public void Removing_From_The_Profile_Deletes_The_File()
        {
            AddAndWatch("worker-4", "Worker");

            _profile.Remove(Category, "worker-4");

            Assert.Null(GetName("worker-4"));
            Assert.False(File.Exists(FilePath("worker-4")));
        }
    }
}
