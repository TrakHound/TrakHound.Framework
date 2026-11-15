// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Audit;

namespace TrakHound.Instance.Runtime.Tests
{
    public class AuditLogTests : IDisposable
    {
        private readonly string _root;
        private readonly string _configuration;
        private readonly string _packages;
        private readonly TrakHoundAuditLog _log;
        private readonly TrakHoundAuditMonitor _monitor;


        public AuditLogTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "trakhound-audit-tests", Guid.NewGuid().ToString("N"));
            _configuration = Path.Combine(_root, "config");
            _packages = Path.Combine(_root, "packages");
            Directory.CreateDirectory(_configuration);
            Directory.CreateDirectory(_packages);

            _log = new TrakHoundAuditLog(_configuration);
            _monitor = new TrakHoundAuditMonitor(_log, _configuration, _packages);
        }

        public void Dispose()
        {
            _monitor.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        private string WriteConfiguration(string name, string contents)
        {
            var path = Path.Combine(_configuration, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, contents);
            return path;
        }

        private void WritePackage(string version)
        {
            var directory = Path.Combine(_packages, "function", "My.Package", version);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "package.json"), version);
        }


        [Fact]
        public void FirstCompare_AddsOneBaseline()
        {
            WriteConfiguration("instance.config.yaml", "a");

            _monitor.Compare();

            var record = Assert.Single(_log.GetRecords());
            Assert.Equal(TrakHoundAuditActions.Baseline, record.Action);
        }

        [Fact]
        public void Compare_RecordsAddedChangedAndRemoved_AsDetected()
        {
            var changed = WriteConfiguration("changed.yaml", "a");
            var removed = WriteConfiguration("removed.yaml", "a");
            _monitor.Compare();

            File.WriteAllText(changed, "b");
            File.Delete(removed);
            WriteConfiguration("worker/added.yaml", "a");
            WritePackage("1.0.0");
            _monitor.Compare();

            var records = _log.GetRecords();
            Assert.Contains(records, o => o.Target == "configuration/changed.yaml" && o.Action == TrakHoundAuditActions.Changed);
            Assert.Contains(records, o => o.Target == "configuration/removed.yaml" && o.Action == TrakHoundAuditActions.Removed);
            Assert.Contains(records, o => o.Target == "configuration/worker/added.yaml" && o.Action == TrakHoundAuditActions.Added);
            Assert.Contains(records, o => o.Target == "package/function/My.Package/1.0.0" && o.Action == TrakHoundAuditActions.Added);
            Assert.All(records, o => Assert.Equal(TrakHoundAuditSources.Detected, o.Source));
        }

        [Fact]
        public void Compare_WithNoChanges_AddsNothing()
        {
            WriteConfiguration("a.yaml", "a");
            _monitor.Compare();
            _monitor.Compare();

            Assert.Single(_log.GetRecords());
        }

        [Fact]
        public void Compare_IgnoresStateFiles()
        {
            _monitor.Compare();

            WriteConfiguration("boots.json", "a");
            WriteConfiguration(".local/admin-sessions-revoked.json", "a");
            WriteConfiguration(".local/data-protection/key.xml", "a");
            WriteConfiguration("instance.id", "a");
            _monitor.Compare();

            Assert.Single(_log.GetRecords());
        }

        [Fact]
        public void RecordFile_UsesTheContext_AndIsNotRecordedAgainByCompare()
        {
            _monitor.Compare();

            var path = WriteConfiguration("instance.config.yaml", "a");
            using (TrakHoundAuditContext.Begin(TrakHoundAuditSources.Admin, "alice", "10.0.0.5"))
            {
                _monitor.RecordFile(path);
            }
            _monitor.Compare();

            var record = _log.GetRecords().First();
            Assert.Equal(2, _log.GetRecords().Count);
            Assert.Equal(TrakHoundAuditSources.Admin, record.Source);
            Assert.Equal("alice", record.User);
            Assert.Equal("10.0.0.5", record.ClientAddress);
        }

        [Fact]
        public async Task Context_FlowsThroughAsyncCalls_AndEndsWithItsScope()
        {
            using (TrakHoundAuditContext.Begin(TrakHoundAuditSources.SystemApi, "api-user", null))
            {
                await Task.Yield();
                Assert.Equal("api-user", TrakHoundAuditContext.Current.User);
            }

            Assert.Null(TrakHoundAuditContext.Current);
        }

        [Fact]
        public void Verify_PassesForAnUnchangedLog()
        {
            var path = WriteConfiguration("a.yaml", "1");
            _monitor.Compare();
            File.WriteAllText(path, "2");
            _monitor.Compare();

            var result = _log.Verify();
            Assert.True(result.Valid);
            Assert.Equal(2, result.Records);
        }

        [Fact]
        public void Verify_FindsAnEditedRecord()
        {
            var path = WriteConfiguration("a.yaml", "1");
            _monitor.Compare();
            File.WriteAllText(path, "2");
            _monitor.Compare();

            File.WriteAllText(_log.Path, File.ReadAllText(_log.Path).Replace("\"action\":\"Changed\"", "\"action\":\"Added\""));

            var result = _log.Verify();
            Assert.False(result.Valid);
            Assert.Equal(2, result.FailedLine);
        }

        [Fact]
        public void Verify_FindsARemovedRecord()
        {
            var path = WriteConfiguration("a.yaml", "1");
            _monitor.Compare();
            File.WriteAllText(path, "2");
            _monitor.Compare();
            File.WriteAllText(path, "3");
            _monitor.Compare();

            var lines = File.ReadAllLines(_log.Path).ToList();
            lines.RemoveAt(1);
            File.WriteAllLines(_log.Path, lines);

            Assert.False(_log.Verify().Valid);
        }

        [Fact]
        public void ALineCutShort_IsReported_AndLaterRecordsStillAppend()
        {
            var path = WriteConfiguration("a.yaml", "1");
            _monitor.Compare();
            File.AppendAllText(_log.Path, "{\"sequence\":2,\"tim");

            File.WriteAllText(path, "2");
            _monitor.Compare();

            Assert.Equal(2, _log.GetRecords().Count);
            Assert.Equal(2, _log.Verify().FailedLine);
        }
    }
}
