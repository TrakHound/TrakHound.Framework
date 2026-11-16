// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Api;
using TrakHound.Functions;
using TrakHound.Packages;
using TrakHound.Services.Tables;
using TrakHound.Testing;

namespace TrakHound.Tables.Conformance.Tests
{
    /// <summary>
    /// The first-party modules that use Tables, run from end to end on each database. This is what proves their
    /// "ansi" queries and per-Dialect scripts work there: a Service can't tell whether SQL is really portable
    /// </summary>
    public abstract class ModuleConformanceTests<TDatabase> : IDisposable where TDatabase : IModuleDatabase, new()
    {
        public class TestFunction : TrakHoundFunction
        {
            public TestFunction(TrakHound.Functions.ITrakHoundFunctionConfiguration configuration, TrakHound.Clients.ITrakHoundClient client, TrakHound.Volumes.ITrakHoundVolume volume) : base(configuration, client, volume) { }

            protected override Task<TrakHoundFunctionResponse> OnRun(IReadOnlyDictionary<string, string> parameters)
            {
                Log(TrakHoundLogLevel.Information, "Ran the Test Function");
                return Task.FromResult(Ok("größe"));
            }
        }


        private const string FunctionId = "Test.Function";
        private const string Organization = "Test";
        private const string PackageId = "Test_Package";

        private static readonly JsonSerializerOptions _json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly TDatabase _database = new TDatabase();
        private readonly TrakHoundTestContext _context = new TrakHoundTestContext();


        public void Dispose()
        {
            _context.Dispose();
            _database.Dispose();
        }


        // The Instance sets the Run ID and times. The test Client only runs the Function, so they are set here
        private void RegisterFunction()
        {
            _context.Client.Functions.Register(FunctionId, async parameters =>
            {
                var response = await _context.RunFunction<TestFunction>(parameters);
                response.Id = parameters["run"];
                response.Completed = DateTime.UtcNow.ToUnixTime();
                response.Logs = _context.GetLogs(TrakHoundLogLevel.Information).ToList();
                return response;
            });
        }

        private async Task<TrakHound.Functions.Controller> CreateFunctionsController()
        {
            _context.Client.Services.Use(_database.Create());
            RegisterFunction();
            return await _context.CreateApiController<TrakHound.Functions.Controller>();
        }

        private async Task<TrakHound.Packages.Api.Controller> CreatePackagesController()
        {
            _context.Client.Services.Use(_database.Create());
            return await _context.CreateApiController<TrakHound.Packages.Api.Controller>();
        }

        private static void AssertOk(TrakHoundApiResponse response)
        {
            if (response.StatusCode != 200) Assert.Fail($"{response.StatusCode} : {Text(response)}");
        }

        private static T Read<T>(TrakHoundApiResponse response)
        {
            AssertOk(response);
            return JsonSerializer.Deserialize<T>(response.GetContentBytes(), _json);
        }

        private static string Text(TrakHoundApiResponse response) => System.Text.Encoding.UTF8.GetString(response.GetContentBytes() ?? Array.Empty<byte>());

        private static string Date(DateTime value) => value.ToString("o");

        private async Task<string> Run(TrakHound.Functions.Controller controller, string runId, DateTime? timestamp = null, string trigger = "manual")
        {
            var response = await controller.RunFunction(FunctionId, new Dictionary<string, string> { ["name"] = "value", ["run"] = runId }, runId, timestamp.HasValue ? Date(timestamp.Value) : null, null, trigger);
            AssertOk(response);
            var errors = _context.GetLogs(TrakHoundLogLevel.Error).Select(o => o.Message).ToList();
            Assert.True(errors.Count == 0, string.Join(" | ", errors));
            return runId;
        }


        [SkippableFact]
        public async Task Functions_RecordRunsAndReadThemBack()
        {
            var controller = await CreateFunctionsController();

            await Run(controller, "run1");
            await Run(controller, "run2", trigger: null);

            var start = Date(DateTime.UtcNow.AddHours(-1));
            var stop = Date(DateTime.UtcNow.AddHours(1));

            var all = await controller.GetFunctionRuns(start, stop);
            AssertOk(all);

            var runs = await controller.GetFunctionRuns(FunctionId, start, stop, skip: 1, take: 1);
            AssertOk(runs);
            var page = JsonDocument.Parse(runs.GetContentBytes()).RootElement;
            Assert.Equal(1, page.GetArrayLength());

            var run = JsonDocument.Parse((await controller.GetFunctionRun(FunctionId, "run1")).GetContentBytes()).RootElement;
            Assert.Contains("manual", run.GetRawText());

            AssertOk(await controller.GetFunctionRunStatus(FunctionId, "run1"));

            var input = await controller.GetFunctionRunInput(FunctionId, "run1");
            AssertOk(input);
            Assert.Contains("value", Text(input));

            var output = await controller.GetFunctionRunOutput(FunctionId, "run1");
            AssertOk(output);
            Assert.Contains("größe", JsonSerializer.Deserialize<Dictionary<string, string>>(output.GetContentBytes()).Values);

            var log = await controller.GetFunctionRunLog(FunctionId, "run1");
            AssertOk(log);
            Assert.Contains("Ran the Test Function", Text(log));
        }

        [SkippableFact]
        public async Task Functions_Summary_CountsRunsEachWay()
        {
            var controller = await CreateFunctionsController();

            await Run(controller, "a");
            await Run(controller, "b");
            await Run(controller, "c", trigger: null);

            var response = await controller.GetSummary(Date(DateTime.UtcNow.AddHours(-1)), Date(DateTime.UtcNow.AddHours(1)), FunctionId, 12);
            AssertOk(response);

            var text = Text(response);
            var summaries = JsonDocument.Parse(text).RootElement;
            var summary = summaries.ValueKind == JsonValueKind.Array ? summaries.EnumerateArray().First(o => GetString(o, "functionId") == FunctionId) : summaries;

            Assert.Equal(3, GetInt(summary, "runCount"));
            Assert.Equal(3, GetInt(summary, "successCount"));
            Assert.Equal(2, GetInt(summary, "manualRunCount"));
        }

        private static int GetInt(JsonElement element, string name)
        {
            var property = element.EnumerateObject().First(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            return property.Value.GetInt32();
        }

        private static string GetString(JsonElement element, string name)
        {
            var property = element.EnumerateObject().FirstOrDefault(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));
            return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
        }

        [SkippableFact]
        public async Task Functions_ScheduleColumns_AddAndList()
        {
            var controller = await CreateFunctionsController();

            AssertOk(await controller.AddScheduleColumn("0 6 * * *", "Morning"));
            AssertOk(await controller.AddScheduleColumn("0 6 * * *", "Renamed"));

            var response = await controller.GetScheduleColumns();
            AssertOk(response);
            Assert.Contains("Renamed", Text(response));
            Assert.DoesNotContain("Morning", Text(response));
        }

        [SkippableFact]
        public async Task Functions_TaskLastRun_IsNullWithoutRuns()
        {
            var controller = await CreateFunctionsController();

            AssertOk(await controller.GetTaskLastRun("no-such-task"));
        }

        [SkippableFact]
        public async Task Retention_DeletesExpiredRunsAndKeepsOthers()
        {
            var service = _database.Create();
            _context.Client.Services.Use(service);
            RegisterFunction();
            var controller = await _context.CreateApiController<TrakHound.Functions.Controller>();

            await Run(controller, "old", DateTime.UtcNow.AddDays(-60));
            await Run(controller, "new");

            var result = await _context.RunFunction<global::Function>(new Dictionary<string, string> { ["days"] = "30" });
            Assert.Equal(200, result.StatusCode);

            async Task<int> Count(string table)
            {
                var response = await service.Execute(TrakHoundTableRequest.Ansi($"select count(*) from \"trakhound\".\"{table}\";"));
                Assert.True(response.Success, response.Message);
                return int.Parse(response.Result[0].Results[0].ReadSingleString());
            }

            Assert.Equal(1, await Count("function_run"));
            Assert.Equal(1, await Count("function_run_trigger"));
            Assert.Equal(1, await Count("function_run_detail"));
            Assert.Equal(2, await Count("function_run_input")); // Two Parameters per Run
            Assert.Equal(1, await Count("function_run_output"));

            Assert.Equal(404, (await controller.GetFunctionRunInput(FunctionId, "old")).StatusCode);
            AssertOk(await controller.GetFunctionRunInput(FunctionId, "new"));
        }

        [SkippableFact]
        public async Task Retention_KeepsTheRunsOfOtherInstances()
        {
            var service = _database.Create();
            _context.Client.Services.Use(service);
            RegisterFunction();

            // Instances can share the database, so an expired Run of another Instance is left for that Instance to delete
            var instanceId = _context.InstanceId;
            _context.InstanceId = "other-instance";
            var otherController = await _context.CreateApiController<TrakHound.Functions.Controller>();
            await Run(otherController, "other", DateTime.UtcNow.AddDays(-60));
            _context.InstanceId = instanceId;

            var controller = await _context.CreateApiController<TrakHound.Functions.Controller>();
            await Run(controller, "old", DateTime.UtcNow.AddDays(-60));

            var result = await _context.RunFunction<global::Function>(new Dictionary<string, string> { ["days"] = "30" });
            Assert.Equal(200, result.StatusCode);

            Assert.Equal(404, (await controller.GetFunctionRunInput(FunctionId, "old")).StatusCode);
            AssertOk(await otherController.GetFunctionRunInput(FunctionId, "other"));
        }

        [SkippableFact]
        public async Task Retention_SkipsTablesThatDontExist()
        {
            _context.Client.Services.Use(_database.Create());

            var result = await _context.RunFunction<global::Function>();

            Assert.Equal(200, result.StatusCode);
        }


        private byte[] Pack(string version)
        {
            var source = Path.Combine(_context.Directory, $"source-{version}");
            Directory.CreateDirectory(Path.Combine(source, "dist"));
            File.WriteAllText(Path.Combine(source, "dist", "module.dll"), "module");

            var builder = new TrakHoundPackageBuilder { Category = "function", Id = PackageId, Version = version };
            builder.SetMetadata("description", "Test");
            return builder.Build().Package(source, null);
        }

        private static async Task<TrakHoundPackageJson> Upload(TrakHound.Packages.Api.Controller controller, byte[] package)
        {
            using var stream = new MemoryStream(package);
            return Read<TrakHoundPackageJson>(await controller.Upload(Organization, stream));
        }

        [SkippableFact]
        public async Task Packages_UploadReadQueryAndDelete()
        {
            var controller = await CreatePackagesController();

            await Upload(controller, Pack("1.0.0"));
            var latest = await Upload(controller, Pack("1.2.0"));

            Assert.Contains(Organization, Text(await controller.ListOrganizations()));

            var version = await controller.Get(Organization, PackageId, "1.0.0");
            AssertOk(version);

            var wildcard = Read<TrakHoundPackageJson>(await controller.Get(Organization, PackageId, "1.*"));
            Assert.Equal(latest.Uuid, wildcard.Uuid);

            Assert.Equal(latest.Uuid, Read<TrakHoundPackageJson>(await controller.GetLatest(Organization, PackageId)).Uuid);

            var versions = await controller.GetVersions(Organization, PackageId);
            AssertOk(versions);
            Assert.Contains("1.2.0", Text(versions));

            AssertOk(await controller.Get(Organization, latest.Uuid));
            AssertOk(await controller.QueryLatest(Organization));
            AssertOk(await controller.QueryByCategory(Organization, "function"));

            // Matches the literal start of an ID: the _ isn't a wildcard
            AssertOk(await controller.QueryLatest(Organization, "Test_"));
            Assert.Equal(404, (await controller.QueryLatest(Organization, "TestX")).StatusCode);
            Assert.Equal(404, (await controller.QueryLatest(Organization, "Test%")).StatusCode);

            AssertOk(await controller.DeletePackageVersion(Organization, latest.Uuid));
            Assert.Equal(404, (await controller.Get(Organization, latest.Uuid)).StatusCode);
        }

        [SkippableFact]
        public async Task Packages_UploadingAgainReplacesIt()
        {
            var controller = await CreatePackagesController();
            var package = Pack("1.0.0");

            await Upload(controller, package);
            await Upload(controller, package);

            var versions = await controller.GetVersions(Organization, PackageId);
            AssertOk(versions);
            Assert.Single(JsonDocument.Parse(versions.GetContentBytes()).RootElement.EnumerateArray());
        }
    }


    [Collection("SqlServer")]
    public class SqlServerModuleTests : ModuleConformanceTests<SqlServerModuleDatabase> { }

    public class SqliteModuleTests : ModuleConformanceTests<SqliteModuleDatabase> { }

    [Collection("Postgres")]
    public class PostgresModuleTests : ModuleConformanceTests<PostgresModuleDatabase> { }
}
