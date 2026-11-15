// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.ScheduledTasks.Tests
{
    public class ScheduledTaskImportTests
    {
        private static ScheduledTaskConfiguration Task(string id, string schedule = "0 6 * * *", string functionId = "Reports.Daily", string jobId = null)
        {
            return new ScheduledTaskConfiguration { Id = id, Enabled = true, FunctionId = functionId, Schedule = schedule, JobId = jobId, TimeZone = "UTC" };
        }

        private static Dictionary<string, ScheduledTaskConfiguration> Existing(params ScheduledTaskConfiguration[] tasks)
        {
            return tasks.ToDictionary(o => o.Id, o => o);
        }

        private static ScheduledTaskImportResult Plan(IEnumerable<ScheduledTaskConfiguration> tasks, Dictionary<string, ScheduledTaskConfiguration> existing = null, ScheduledTaskImportMode mode = ScheduledTaskImportMode.Fail)
        {
            return ScheduledTaskImport.Plan(tasks, existing, mode, dryRun: false);
        }


        [Fact]
        public void NewTask_IsCreated()
        {
            var result = Plan(new[] { Task("daily") });

            Assert.True(result.Success);
            Assert.Equal(1, result.Created);
            Assert.Equal(ScheduledTaskImportAction.Create, result.Tasks[0].Action);
            Assert.Equal("daily", result.Tasks[0].FileId);
            Assert.NotNull(result.Tasks[0].Configuration);
        }

        [Fact]
        public void SameTask_IsUnchanged()
        {
            var incoming = Task(" daily ");
            incoming.Description = "  ";

            var result = Plan(new[] { incoming }, Existing(Task("daily")));

            Assert.True(result.Success);
            Assert.Equal(ScheduledTaskImportAction.Unchanged, result.Tasks[0].Action);
            Assert.Null(result.Tasks[0].Configuration);
        }

        [Fact]
        public void ExistingTaskWithoutId_UsesFileId()
        {
            var existing = Task(null);
            var result = Plan(new[] { Task("daily") }, new Dictionary<string, ScheduledTaskConfiguration> { ["daily"] = existing });

            Assert.Equal(ScheduledTaskImportAction.Unchanged, result.Tasks[0].Action);
        }

        [Fact]
        public void DifferentTask_ConflictsByDefault()
        {
            var result = Plan(new[] { Task("daily", "0 7 * * *") }, Existing(Task("daily")));

            Assert.False(result.Success);
            Assert.Equal(1, result.Conflicts);
            Assert.Null(result.Tasks[0].Configuration);
        }

        [Fact]
        public void DifferentTask_IsSkipped()
        {
            var result = Plan(new[] { Task("daily", "0 7 * * *") }, Existing(Task("daily")), ScheduledTaskImportMode.Skip);

            Assert.True(result.Success);
            Assert.Equal(1, result.Skipped);
            Assert.Null(result.Tasks[0].Configuration);
        }

        [Fact]
        public void DifferentTask_IsOverwrittenInItsFile()
        {
            var result = Plan(new[] { Task("DAILY", "0 7 * * *") }, Existing(Task("daily")), ScheduledTaskImportMode.Overwrite);

            Assert.True(result.Success);
            Assert.Equal(1, result.Updated);
            Assert.Equal("daily", result.Tasks[0].FileId);
            Assert.Equal("0 7 * * *", result.Tasks[0].Configuration.Schedule);
        }

        [Theory]
        [InlineData("../daily", "0 6 * * *", "Reports.Daily", "UTC")]
        [InlineData("daily", "not a schedule", "Reports.Daily", "UTC")]
        [InlineData("daily", "0 6 * * *", null, "UTC")]
        [InlineData("daily", "0 6 * * *", "Reports.Daily", "Mars/Olympus_Mons")]
        public void InvalidTask_FailsTheImport(string id, string schedule, string functionId, string timeZone)
        {
            var task = Task(id, schedule, functionId);
            task.TimeZone = timeZone;

            var result = Plan(new[] { task, Task("other") });

            Assert.False(result.Success);
            Assert.Equal(1, result.Invalid);
            Assert.Equal(ScheduledTaskImportAction.Invalid, result.Tasks[0].Action);
            Assert.NotEmpty(result.Tasks[0].Messages);
        }

        [Fact]
        public void DuplicateIds_AreInvalid()
        {
            var result = Plan(new[] { Task("daily"), Task("Daily") });

            Assert.False(result.Success);
            Assert.Equal(ScheduledTaskImportAction.Create, result.Tasks[0].Action);
            Assert.Equal(ScheduledTaskImportAction.Invalid, result.Tasks[1].Action);
        }

        [Fact]
        public void SixFieldSchedule_IsValid()
        {
            Assert.True(Plan(new[] { Task("fast", "*/30 * * * * *") }).Success);
        }

        [Fact]
        public void Parameters_AreDroppedWithAWarning()
        {
            var task = Task("daily");
            task.Parameters = new Dictionary<string, string> { ["site"] = "Plant1" };

            var result = Plan(new[] { task });

            Assert.True(result.Success);
            Assert.Null(result.Tasks[0].Configuration.Parameters);
            Assert.Single(result.Tasks[0].Messages);
        }

        [Fact]
        public void Document_IsValidated()
        {
            Assert.NotNull(ScheduledTaskImport.ValidateDocument(null));
            Assert.NotNull(ScheduledTaskImport.ValidateDocument(new ScheduledTaskExport { Tasks = new List<ScheduledTaskConfiguration>() }));
            Assert.NotNull(ScheduledTaskImport.ValidateDocument(new ScheduledTaskExport { Format = "something-else", Tasks = new List<ScheduledTaskConfiguration> { Task("a") } }));
            Assert.NotNull(ScheduledTaskImport.ValidateDocument(new ScheduledTaskExport { Version = ScheduledTaskExport.CurrentVersion + 1, Tasks = new List<ScheduledTaskConfiguration> { Task("a") } }));

            // Written offline without the header
            Assert.Null(ScheduledTaskImport.ValidateDocument(new ScheduledTaskExport { Tasks = new List<ScheduledTaskConfiguration> { Task("a") } }));
        }

        [Theory]
        [InlineData(null, ScheduledTaskImportMode.Fail)]
        [InlineData("skip", ScheduledTaskImportMode.Skip)]
        [InlineData("Overwrite", ScheduledTaskImportMode.Overwrite)]
        public void Mode_IsParsed(string value, ScheduledTaskImportMode expected)
        {
            Assert.True(ScheduledTaskImport.TryParseMode(value, out var mode));
            Assert.Equal(expected, mode);
        }

        [Theory]
        [InlineData("replace")]
        [InlineData("5")]
        public void UnknownMode_IsRejected(string value)
        {
            Assert.False(ScheduledTaskImport.TryParseMode(value, out _));
        }
    }
}
