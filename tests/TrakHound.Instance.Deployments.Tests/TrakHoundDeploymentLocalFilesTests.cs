// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Deployments;
using TrakHound.Instance.Security;

namespace TrakHound.Instance.Deployments.Tests
{
    /// <summary>
    /// A Profile's '.local' folder (the keys that protect Admin sign-ins, revoked sessions) is never deployed, backed up, restored or removed,
    /// and Users and API Keys are only deployed with secrets
    /// </summary>
    public class TrakHoundDeploymentLocalFilesTests : IDisposable
    {
        private readonly string _root;
        private readonly string _source;
        private readonly string _target;


        public TrakHoundDeploymentLocalFilesTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "trakhound-deployment-local-" + Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "source");
            _target = Path.Combine(_root, "target");

            foreach (var instance in new[] { _source, _target })
            {
                var name = Path.GetFileName(instance);
                Write(instance, "config/default/.local/data-protection/key-1.xml", $"<key>{name}</key>");
                Write(instance, "config/default/.local/admin-sessions-revoked.json", $"[\"{name}\"]");
                Write(instance, "config/default/security/admins/owner.yaml", $"id: {name}-owner\n");
                Write(instance, "config/default/security/users/admin.yaml", $"id: {name}-admin\n");
                Write(instance, "config/default/security/api-keys/key.yaml", $"id: {name}-key\n");
                Directory.CreateDirectory(Path.Combine(instance, "packages"));
            }

            Write(_source, "config/default/security/operators.config.yaml", "id: operators\n");
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }


        [Fact]
        public void Create_LeavesOutTheLocalFolder_AndUsersAndApiKeysWithoutSecrets()
        {
            var preview = TrakHoundDeployment.Preview(CreateOptions(includeSecrets: false));

            Assert.Equal(1, preview.ConfigurationFileCount); // The Security Profile
            Assert.Equal(3, preview.SecretFilesExcluded);
            Assert.DoesNotContain(TrakHoundDeployment.GetCategories(Path.Combine(_source, "config")), o => o == TrakHoundDeployment.LocalDirectory);
        }

        [Fact]
        public void Create_WithSecrets_IncludesUsersAndApiKeys_NeverTheLocalFolder()
        {
            var preview = TrakHoundDeployment.Preview(CreateOptions(includeSecrets: true));

            Assert.Equal(4, preview.ConfigurationFileCount);
        }

        [Fact]
        public void Install_KeepsTheTargetsLocalFolderUsersAndApiKeys()
        {
            var result = TrakHoundDeployment.Install(CreateDeployment(includeSecrets: false), InstallOptions(backup: true));
            Assert.True(result.Success, string.Join("; ", result.Errors));

            Assert.Equal("<key>target</key>", Read(_target, "config/default/.local/data-protection/key-1.xml"));
            Assert.Equal("[\"target\"]", Read(_target, "config/default/.local/admin-sessions-revoked.json"));
            Assert.Equal("id: target-admin\n", Read(_target, "config/default/security/users/admin.yaml"));
            Assert.Equal("id: target-key\n", Read(_target, "config/default/security/api-keys/key.yaml"));
            Assert.True(File.Exists(Path.Combine(_target, "config", "default", "security", "operators.config.yaml")));

            // The Backup doesn't hold them either, and restoring it doesn't change them
            Assert.False(Directory.Exists(Path.Combine(result.BackupPath, "config", "default", TrakHoundDeployment.LocalDirectory)));
            Write(_target, "config/default/.local/admin-sessions-revoked.json", "[\"after\"]");
            Assert.True(TrakHoundDeployment.Restore(result.BackupPath, InstallOptions(backup: false)).Success);
            Assert.Equal("[\"after\"]", Read(_target, "config/default/.local/admin-sessions-revoked.json"));
            Assert.Equal("<key>target</key>", Read(_target, "config/default/.local/data-protection/key-1.xml"));
        }

        [Fact]
        public void Install_WithSecrets_ReplacesUsers_KeepsTheLocalFolder()
        {
            var result = TrakHoundDeployment.Install(CreateDeployment(includeSecrets: true), InstallOptions(backup: false));
            Assert.True(result.Success, string.Join("; ", result.Errors));

            Assert.Equal("id: source-admin\n", Read(_target, "config/default/security/users/admin.yaml"));
            Assert.Equal("<key>target</key>", Read(_target, "config/default/.local/data-protection/key-1.xml"));
        }

        [Fact]
        public void LocalFiles_MoveFromTheSecurityFolder_Once()
        {
            var profile = Path.Combine(_root, "upgraded");
            Write(profile, "security/data-protection/key-1.xml", "<key>old</key>");
            Write(profile, "security/admin-sessions-revoked.json", "[\"old\"]");

            var moved = TrakHoundInstanceLocalFiles.MoveFromPreviousLocation(profile);

            Assert.Equal(2, moved.Count);
            Assert.Equal("<key>old</key>", File.ReadAllText(Path.Combine(TrakHoundInstanceLocalFiles.GetDataProtectionPath(profile), "key-1.xml")));
            Assert.Equal("[\"old\"]", File.ReadAllText(TrakHoundInstanceLocalFiles.GetRevokedSessionsPath(profile)));
            Assert.False(Directory.Exists(Path.Combine(profile, "security", "data-protection")));
            Assert.Empty(TrakHoundInstanceLocalFiles.MoveFromPreviousLocation(profile));
        }


        private TrakHoundDeploymentCreateOptions CreateOptions(bool includeSecrets)
        {
            return new TrakHoundDeploymentCreateOptions
            {
                ConfigurationPath = Path.Combine(_source, "config"),
                PackagesPath = Path.Combine(_source, "packages"),
                Profiles = new[] { "default" },
                IncludeSecrets = includeSecrets
            };
        }

        private TrakHoundDeploymentInstallOptions InstallOptions(bool backup)
        {
            return new TrakHoundDeploymentInstallOptions
            {
                ConfigurationPath = Path.Combine(_target, "config"),
                PackagesPath = Path.Combine(_target, "packages"),
                BackupPath = backup ? Path.Combine(_target, "backup") : null
            };
        }

        private byte[] CreateDeployment(bool includeSecrets)
        {
            var deployment = new TrakHoundDeployment { ProfileId = "default", Version = "1.0.0", BuildDate = DateTime.UtcNow };
            var outputPath = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".thd");

            var result = deployment.CreateFile(outputPath, CreateOptions(includeSecrets));
            Assert.True(result.Success, string.Join("; ", result.Errors));

            return File.ReadAllBytes(outputPath);
        }

        private static void Write(string root, string relativePath, string contents)
        {
            var path = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        private static string Read(string root, string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));
    }
}
