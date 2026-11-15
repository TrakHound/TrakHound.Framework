// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Instance.Boots;
using TrakHound.Instances;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// Boots are downloaded from the Admin interface as JSON (everything recorded) or a CSV summary (one row per Boot)
    /// </summary>
    public class BootExportTests
    {
        private static readonly DateTime _started = new DateTime(2026, 11, 13, 8, 30, 0, DateTimeKind.Utc);


        [Fact]
        public void ToJson_KeepsEverythingRecorded()
        {
            var boot = CreateBoot();
            boot.Starts = new List<TrakHoundInstanceRuntimeStart>
            {
                new TrakHoundInstanceRuntimeStart
                {
                    Started = _started.AddSeconds(2),
                    Success = true,
                    Steps = new List<TrakHoundInstanceBootStep> { new TrakHoundInstanceBootStep { Name = "Load Packages", DurationMs = 120 } }
                }
            };

            var boots = JsonSerializer.Deserialize<List<TrakHoundInstanceBoot>>(TrakHoundBootExport.ToJson(new[] { boot }));

            var read = Assert.Single(boots);
            Assert.Equal(boot.Id, read.Id);
            Assert.Equal(TrakHoundInstanceBootStatus.Crashed, read.Status);
            Assert.Equal(boot.Details, read.Details);
            Assert.Equal("Load Packages", read.Starts.Single().Steps.Single().Name);
            Assert.Equal("Something failed", read.Log.Entries.Single().Message);
        }

        [Fact]
        public void ToJson_OneBootIsAnObject()
        {
            using var document = JsonDocument.Parse(TrakHoundBootExport.ToJson(CreateBoot()));
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        }

        [Fact]
        public void ToCsv_WritesAHeaderAndOneRowPerBoot()
        {
            var running = new TrakHoundInstanceBoot { Id = "2", ProcessId = 2, Status = TrakHoundInstanceBootStatus.Running, Started = _started.AddHours(1) };

            var lines = ReadLines(TrakHoundBootExport.ToCsv(new[] { running, CreateBoot() }, _started.AddHours(2)));

            Assert.Equal(3, lines.Count);
            Assert.Equal(string.Join(",", TrakHoundBootExport.CsvColumns), lines[0]);

            var crashed = Columns(lines[2]);
            Assert.Equal("Crashed", crashed["status"]);
            Assert.Equal("2026-11-13T08:30:00.000Z", crashed["started"]);
            Assert.Equal("1500", crashed["bootDurationMs"]);
            Assert.Equal("600000", crashed["uptimeMs"]);
            Assert.Equal("1", crashed["warningCount"]);
            Assert.Equal("3", crashed["errorCount"]);
            Assert.Equal("2", crashed["runtimeStarts"]);
            Assert.Equal("1", crashed["failedRuntimeStarts"]);
            Assert.Equal("-532462766", crashed["exitCode"]);

            // A running Boot's uptime is until now, and it has no end
            var current = Columns(lines[1]);
            Assert.Equal("3600000", current["uptimeMs"]);
            Assert.Equal("", current["ended"]);
            Assert.Equal("", current["bootDurationMs"]);
        }

        [Fact]
        public void ToCsv_QuotesValuesWithCommasQuotesAndNewLines()
        {
            var boot = CreateBoot();
            boot.Reason = "Failed to bind \"0.0.0.0:8472\", the port is in use\r\nat Kestrel";

            var csv = TrakHoundBootExport.ToCsv(new[] { boot });

            Assert.Contains("\"Failed to bind \"\"0.0.0.0:8472\"\", the port is in use\r\nat Kestrel\"", csv);
        }

        [Fact]
        public void ToCsv_KeepsSpreadsheetsFromReadingTextAsAFormula()
        {
            var boot = CreateBoot();
            boot.Reason = "=HYPERLINK(\"x\")";

            var csv = TrakHoundBootExport.ToCsv(new[] { boot });

            Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", csv);
        }


        private static TrakHoundInstanceBoot CreateBoot() => new TrakHoundInstanceBoot
        {
            Id = "20261113083000000-1",
            ProcessId = 1,
            Status = TrakHoundInstanceBootStatus.Crashed,
            Started = _started,
            Ready = _started.AddMilliseconds(1500),
            Ended = _started.AddMinutes(10),
            Reason = "Unhandled Exception",
            Details = "System.InvalidOperationException: Something failed\n   at Somewhere()",
            ExitCode = -532462766,
            Log = new TrakHoundInstanceBootLog
            {
                WarningCount = 1,
                ErrorCount = 3,
                Entries = new List<TrakHoundInstanceBootLogEntry> { new TrakHoundInstanceBootLogEntry { Timestamp = _started, Level = "Error", Logger = "Test", Message = "Something failed" } }
            },
            Starts = new List<TrakHoundInstanceRuntimeStart>
            {
                new TrakHoundInstanceRuntimeStart { Started = _started, Success = true },
                new TrakHoundInstanceRuntimeStart { Started = _started.AddMinutes(5), Success = false, Error = "Failed" }
            }
        };

        // The test rows have no quoted values, so they split on commas
        private static List<string> ReadLines(string csv) => csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();

        private static Dictionary<string, string> Columns(string line)
        {
            var values = line.Split(',');
            return TrakHoundBootExport.CsvColumns.Select((name, i) => (name, value: values[i])).ToDictionary(o => o.name, o => o.value);
        }
    }
}
