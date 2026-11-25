// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Buffers;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Changes the process-wide Data Directory, so these tests don't run alongside others
    /// </summary>
    [CollectionDefinition(nameof(DataDirectoryTests), DisableParallelization = true)]
    public class DataDirectoryCollection { }

    /// <summary>
    /// What a process writes as it runs goes in the Data Directory, so a host (ex. the Instance) can keep its program directory read-only
    /// </summary>
    [Collection(nameof(DataDirectoryTests))]
    public class DataDirectoryTests : IDisposable
    {
        private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "trakhound-data-directory-" + Guid.NewGuid().ToString("N"));


        public DataDirectoryTests()
        {
            TrakHoundDataDirectory.Path = _dataPath;
        }

        public void Dispose()
        {
            TrakHoundDataDirectory.Path = null;
            try { Directory.Delete(_dataPath, true); } catch { }
        }


        [Fact]
        public void Defaults_To_The_Base_Directory()
        {
            TrakHoundDataDirectory.Path = null;

            Assert.Equal(AppContext.BaseDirectory, TrakHoundDataDirectory.Path);
        }

        [Fact]
        public void Paths_Are_In_The_Data_Directory()
        {
            Assert.Equal(Path.Combine(_dataPath, "backup", "deployments"), TrakHoundDataDirectory.GetPath("backup", "deployments"));

            // A rooted path is kept as it is (ex. a Log History path set to another drive)
            var rooted = Path.Combine(Path.GetTempPath(), "elsewhere");
            Assert.Equal(rooted, TrakHoundDataDirectory.GetPath(rooted));
        }

        [Fact]
        public void Temp_Follows_The_Data_Directory()
        {
            Assert.Equal(Path.Combine(_dataPath, "_temp"), TrakHoundTemp.GetDirectory());

            var directory = TrakHoundTemp.CreateDirectory();
            Assert.StartsWith(Path.Combine(_dataPath, "_temp"), directory);
        }

        [Fact]
        public void Buffers_Follow_The_Data_Directory()
        {
            var buffer = new OperationFileBuffer<string>("Data Directory Test", bytes => string.Empty, (stream, item) => 0);

            // Checking for the directory and creating it use the same place
            Assert.False(buffer.GetDirectoryExists("Data Directory Test"));
            var directory = buffer.GetDirectory("Data Directory Test");
            Assert.Equal(Path.Combine(_dataPath, OperationFileBuffer<string>.DirectoryBuffer, "data_directory_test"), directory);
            Assert.True(buffer.GetDirectoryExists("Data Directory Test"));
        }
    }
}
