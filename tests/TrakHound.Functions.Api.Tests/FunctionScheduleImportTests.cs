// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.ScheduledTasks;

namespace TrakHound.Functions.Tests
{
    public class FunctionScheduleImportTests
    {
        private static FunctionJob Job(string id, string name = null, string functionId = "Reports.Daily", Dictionary<string, string> parameters = null)
        {
            return new FunctionJob { Id = id, FunctionId = functionId, Name = name ?? id, Parameters = parameters };
        }

        private static ScheduledTaskConfiguration Task(string id, string jobId, string functionId = "Reports.Daily")
        {
            return new ScheduledTaskConfiguration { Id = id, FunctionId = functionId, JobId = jobId, Schedule = "0 6 * * *" };
        }


        [Fact]
        public void NewJob_IsCreated()
        {
            var items = FunctionScheduleImport.PlanJobs(new[] { Job("plant1") }, new FunctionJob[0], ScheduledTaskImportMode.Fail);

            Assert.Equal(ScheduledTaskImportAction.Create, items[0].Action);
            Assert.NotNull(items[0].Job);
            Assert.False(FunctionScheduleImport.HasErrors(items));
        }

        [Fact]
        public void SameJob_IsUnchanged()
        {
            var existing = Job("plant1", "Plant 1", parameters: new Dictionary<string, string> { ["site"] = "Plant1" });
            var incoming = Job("plant1", " Plant 1 ", parameters: new Dictionary<string, string> { [" site "] = "Plant1" });

            var items = FunctionScheduleImport.PlanJobs(new[] { incoming }, new[] { existing }, ScheduledTaskImportMode.Fail);

            Assert.Equal(ScheduledTaskImportAction.Unchanged, items[0].Action);
            Assert.Null(items[0].Job);
        }

        [Theory]
        [InlineData(ScheduledTaskImportMode.Fail, "conflict", true)]
        [InlineData(ScheduledTaskImportMode.Skip, "skip", false)]
        [InlineData(ScheduledTaskImportMode.Overwrite, "update", false)]
        public void DifferentJob_FollowsMode(ScheduledTaskImportMode mode, string action, bool hasErrors)
        {
            var existing = Job("plant1", parameters: new Dictionary<string, string> { ["site"] = "Plant1" });
            var incoming = Job("plant1", parameters: new Dictionary<string, string> { ["site"] = "Plant2" });

            var items = FunctionScheduleImport.PlanJobs(new[] { incoming }, new[] { existing }, mode);

            Assert.Equal(action, items[0].Action);
            Assert.Equal(hasErrors, FunctionScheduleImport.HasErrors(items));
            Assert.Equal(mode == ScheduledTaskImportMode.Overwrite, items[0].Job != null);
        }

        [Fact]
        public void OverwrittenJob_KeepsExistingIdCase()
        {
            var items = FunctionScheduleImport.PlanJobs(new[] { Job("PLANT1", "Changed") }, new[] { Job("plant1") }, ScheduledTaskImportMode.Overwrite);

            Assert.Equal("plant1", items[0].Id);
            Assert.Equal("plant1", items[0].Job.Id);
        }

        [Fact]
        public void JobWithoutId_IsInvalid()
        {
            var items = FunctionScheduleImport.PlanJobs(new[] { Job(null, "Plant 1") }, new FunctionJob[0], ScheduledTaskImportMode.Fail);

            Assert.Equal(ScheduledTaskImportAction.Invalid, items[0].Action);
            Assert.True(FunctionScheduleImport.HasErrors(items));
        }

        [Fact]
        public void DuplicateJob_IsInvalid()
        {
            var items = FunctionScheduleImport.PlanJobs(new[] { Job("plant1"), Job("Plant1", "Other") }, new FunctionJob[0], ScheduledTaskImportMode.Fail);

            Assert.Equal(ScheduledTaskImportAction.Create, items[0].Action);
            Assert.Equal(ScheduledTaskImportAction.Invalid, items[1].Action);
        }

        [Fact]
        public void SameIdForAnotherFunction_IsAnotherJob()
        {
            var items = FunctionScheduleImport.PlanJobs(new[] { Job("default", functionId: "A"), Job("default", functionId: "B") }, new FunctionJob[0], ScheduledTaskImportMode.Fail);

            Assert.All(items, o => Assert.Equal(ScheduledTaskImportAction.Create, o.Action));
        }

        [Fact]
        public void NameUsedByAnotherJob_IsInvalid()
        {
            var items = FunctionScheduleImport.PlanJobs(new[] { Job("plant-1", "Plant 1") }, new[] { Job("3f2b1c4e", "plant 1") }, ScheduledTaskImportMode.Fail);

            Assert.Equal(ScheduledTaskImportAction.Invalid, items[0].Action);
            Assert.Null(items[0].Job);
        }

        [Fact]
        public void NameFreedByTheImport_IsValid()
        {
            // "Plant 1" is renamed by the Import, so the new Job can use the Name
            var existing = new[] { Job("old", "Plant 1") };
            var items = FunctionScheduleImport.PlanJobs(new[] { Job("old", "Plant 1 (Old)"), Job("plant-1", "Plant 1") }, existing, ScheduledTaskImportMode.Overwrite);

            Assert.False(FunctionScheduleImport.HasErrors(items));
        }

        [Fact]
        public void TaskJobs_AreFoundInTheImportOrOnTheInstance()
        {
            var existing = new[] { Job("existing") };
            var items = FunctionScheduleImport.PlanJobs(new[] { Job("imported"), Job("bad id!") }, existing, ScheduledTaskImportMode.Fail);
            var available = FunctionScheduleImport.GetAvailableJobs(existing, items);

            var tasks = new[]
            {
                Task("a", "existing"),
                Task("b", "imported", functionId: "REPORTS.DAILY"), // Function IDs ignore case
                Task("c", null),
                Task("d", "missing"),
                Task("e", "Existing"), // Job IDs are exact (the same as when the Task runs)
                Task("f", "bad id!"),
                Task("g", "existing", functionId: "Other")
            };

            var errors = FunctionScheduleImport.CheckTaskJobs(tasks, available);

            Assert.Equal(new[] { "d", "e", "f", "g" }, errors.Keys.OrderBy(o => o));
        }

        [Fact]
        public void TaskErrors_FailTheTasksResult()
        {
            var result = ScheduledTaskImport.Plan(new[] { Task("a", "missing"), Task("b", null) }, null, ScheduledTaskImportMode.Fail, dryRun: true);
            Assert.True(result.Success);

            FunctionScheduleImport.ApplyTaskErrors(result, new Dictionary<string, string> { ["A"] = "Job Not Found" });

            Assert.False(result.Success);
            Assert.Equal(1, result.Invalid);
            Assert.Equal(ScheduledTaskImportAction.Invalid, result.Tasks[0].Action);
            Assert.Equal(ScheduledTaskImportAction.Create, result.Tasks[1].Action);
        }

        [Fact]
        public void Document_IsValidated()
        {
            Assert.NotNull(FunctionScheduleImport.ValidateDocument(null));
            Assert.NotNull(FunctionScheduleImport.ValidateDocument(new FunctionScheduleExport()));
            Assert.NotNull(FunctionScheduleImport.ValidateDocument(new FunctionScheduleExport { Format = "other", Jobs = new List<FunctionJob> { Job("a") } }));
            Assert.NotNull(FunctionScheduleImport.ValidateDocument(new FunctionScheduleExport { Version = 99, Jobs = new List<FunctionJob> { Job("a") } }));

            Assert.Null(FunctionScheduleImport.ValidateDocument(new FunctionScheduleExport { Jobs = new List<FunctionJob> { Job("a") } }));

            // A Scheduled Tasks Api Export (Tasks only)
            Assert.Null(FunctionScheduleImport.ValidateDocument(new FunctionScheduleExport { Format = ScheduledTaskExport.FormatName, Tasks = new List<ScheduledTaskConfiguration> { Task("a", null) } }));
        }
    }
}
