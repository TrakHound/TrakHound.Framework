// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Instance.Security;

namespace TrakHound.Instance.Runtime.Tests
{
    // TrakHoundSecrets keeps its key in static state, so these never run alongside tests that create Configuration Profiles
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class SecretsCollection
    {
        public const string Name = "Secrets";
    }


    /// <summary>
    /// Secret values are never saved as plain text without saying so, and key files are readable only by the Instance's user
    /// (on Linux and macOS; Windows relies on the data folder's permissions)
    /// </summary>
    [Collection(SecretsCollection.Name)]
    public class SecretsTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"trakhound-secrets-tests-{Guid.NewGuid()}");


        public SecretsTests()
        {
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            TrakHoundSecrets.Initialize(Path.Combine(_root, "reset")); // Don't leave the key path pointing at this test's files
            try { Directory.Delete(_root, true); } catch { }
        }


        [Fact]
        public void UnavailableKey_RaisesEncryptionFailed()
        {
            // The key's directory is a file, so the key can't be created
            var blocked = Path.Combine(_root, "blocked");
            File.WriteAllText(blocked, "");
            TrakHoundSecrets.Initialize(blocked);

            var messages = new List<string>();
            EventHandler<string> handler = (s, message) => messages.Add(message);
            TrakHoundSecrets.EncryptionFailed += handler;
            try
            {
                Assert.Equal("my-password", TrakHoundSecrets.Encrypt("my-password"));
                Assert.Equal("other-password", TrakHoundSecrets.Encrypt("other-password"));
            }
            finally
            {
                TrakHoundSecrets.EncryptionFailed -= handler;
            }

            // Once until a value is encrypted again (not once per value)
            Assert.Single(messages);
            Assert.Contains("plain text", messages[0]);
        }

        [Fact]
        public void AvailableKey_Encrypts()
        {
            TrakHoundSecrets.Initialize(_root);

            var encrypted = TrakHoundSecrets.Encrypt("my-password");

            Assert.True(TrakHoundSecrets.IsEncrypted(encrypted));
            Assert.Equal("my-password", TrakHoundSecrets.Decrypt(encrypted));
        }

        [Fact]
        public void NewKey_IsReadableOnlyByOwner()
        {
            if (OperatingSystem.IsWindows()) return;

            TrakHoundSecrets.Initialize(_root);
            TrakHoundSecrets.Encrypt("my-password");

            Assert.Equal(TrakHoundFilePermissions.OwnerFile, File.GetUnixFileMode(Path.Combine(_root, TrakHoundSecrets.KeyFilename)));
        }

        [Fact]
        public void AdminAccountFile_IsReadableOnlyByOwner()
        {
            if (OperatingSystem.IsWindows()) return;

            var store = new TrakHoundAdminAccountStore(TrakHoundAdminAccountStore.GetDirectory(_root));
            Assert.Null(store.SetOwner("admin", "secret-1", out _));

            Assert.Equal(TrakHoundFilePermissions.OwnerFile, File.GetUnixFileMode(store.GetAccountPath(store.GetAccountByUsername("admin").Id)));
        }

        [Fact]
        public void RestrictDirectory_LimitsDirectoryAndFiles()
        {
            if (OperatingSystem.IsWindows())
            {
                // Nothing to change on Windows (the data folder's permissions apply)
                Assert.True(TrakHoundFilePermissions.RestrictDirectory(_root));
                return;
            }

            var directory = Path.Combine(_root, "keys");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "key.xml");
            File.WriteAllText(file, "");
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            Assert.True(TrakHoundFilePermissions.RestrictDirectory(directory));
            Assert.Equal(TrakHoundFilePermissions.OwnerDirectory, File.GetUnixFileMode(directory));
            Assert.Equal(TrakHoundFilePermissions.OwnerFile, File.GetUnixFileMode(file));
        }
    }
}
