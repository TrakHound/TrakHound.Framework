// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Services;
using TrakHound.Services.Tables;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Tables.Conformance.Tests
{
    /// <summary>
    /// What every SQL Table Service must do. A Service that supports the "ansi" Dialect has to pass all of these
    /// </summary>
    public abstract class TableServiceConformanceTests
    {
        public const string Schema = "trakhound_conformance";

        public enum ItemStatus
        {
            Running,
            Stopped
        }

        [Table("item", Schema)]
        [TableIndex("ix_conformance_item_count", "item_count desc", Include = new[] { "name" })]
        public class Item
        {
            [PrimaryKey]
            public string Id { get; set; }

            [Column(Length = TrakHoundTableColumnDefinition.MaxLength, Unicode = true)]
            [Update]
            public string Name { get; set; }

            public double Value { get; set; }

            public decimal Amount { get; set; }

            public DateTime Timestamp { get; set; }

            public TimeSpan Duration { get; set; }

            public ItemStatus? Status { get; set; }

            public Guid? Uuid { get; set; }

            public bool Enabled { get; set; }

            [Column("item_count")]
            public int Count { get; set; }
        }

        [Table("ignore_item", Schema)]
        [IgnoreDuplicateKey]
        public class IgnoreItem
        {
            [PrimaryKey]
            public string Id { get; set; }

            public string Name { get; set; }
        }

        [Table("compound_item", Schema)]
        public class CompoundItem
        {
            [PrimaryKey]
            public string A { get; set; }

            [PrimaryKey]
            public string B { get; set; }
        }

        [Table("unique_item", Schema)]
        [TableIndex("ix_conformance_unique_name", "name", IsUnique = true)]
        public class UniqueItem
        {
            [PrimaryKey]
            public string Id { get; set; }

            public string Name { get; set; }
        }


        protected abstract ITrakHoundTableService Service { get; }

        /// <summary>
        /// The name of the Dialect the Service prefers
        /// </summary>
        protected abstract string Dialect { get; }

        /// <summary>
        /// Removes the Tables of an earlier test
        /// </summary>
        protected abstract Task Reset();


        private string Item_ => "\"trakhound_conformance\".\"item\"";

        private async Task<TrakHoundTableStepResult[]> Run(TrakHoundTableRequest request)
        {
            var response = await Service.Execute(request);
            Assert.True(response.Success, $"{response.StatusCode} : {response.Message}");
            return response.Result;
        }

        private async Task<List<Item>> ReadItems()
        {
            var results = await Run(TrakHoundTableRequest.Ansi($"select * from {Item_} order by \"id\";"));
            return results[0].Results[0].ReadMultiple<Item>()?.ToList() ?? new List<Item>();
        }

        private async Task CreateItems()
        {
            await Reset();

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendEnsureTable<Item>();
            await Run(builder.Build());
        }

        private async Task InsertItems(params Item[] items)
        {
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(items);
            await Run(builder.Build());
        }

        private static Item NewItem(string id, int count = 0, string name = "name") => new Item { Id = id, Count = count, Name = name, Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };


        [SkippableFact]
        public async Task Dialects_IncludeAnsi()
        {
            var response = await Service.GetDialects();

            Assert.Equal(new[] { Dialect, TrakHoundTableDialects.Ansi }, response.Result);
        }

        [SkippableFact]
        public async Task EnsureTable_CanRunAgain()
        {
            await CreateItems();

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendEnsureTable<Item>();
            await Run(builder.Build());
        }

        [SkippableFact]
        public async Task Values_RoundTrip()
        {
            await CreateItems();

            var uuid = Guid.NewGuid();
            var timestamp = new DateTime(2026, 5, 6, 7, 8, 9, 123, DateTimeKind.Utc);

            await InsertItems(new Item
            {
                Id = "a",
                Name = "Größe 測定 'quoted'",
                Value = 1.5,
                Amount = 12345.125m,
                Timestamp = timestamp,
                Duration = TimeSpan.FromMinutes(90),
                Status = ItemStatus.Stopped,
                Uuid = uuid,
                Enabled = true,
                Count = 42
            });

            var item = Assert.Single(await ReadItems());

            Assert.Equal("Größe 測定 'quoted'", item.Name);
            Assert.Equal(1.5, item.Value);
            Assert.Equal(12345.125m, item.Amount);
            Assert.Equal(timestamp, item.Timestamp);
            Assert.Equal(TimeSpan.FromMinutes(90), item.Duration);
            Assert.Equal(ItemStatus.Stopped, item.Status);
            Assert.Equal(uuid, item.Uuid);
            Assert.True(item.Enabled);
            Assert.Equal(42, item.Count);
        }

        [SkippableFact]
        public async Task Values_RoundTripThroughJson()
        {
            await CreateItems();

            // As a Request arrives over HTTP
            var uuid = Guid.NewGuid();
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { new Item { Id = "a", Name = "n", Value = 2.5, Amount = 7.25m, Uuid = uuid, Enabled = true, Count = 3, Status = ItemStatus.Running } });
            var request = JsonSerializer.Deserialize<TrakHoundTableRequest>(JsonSerializer.Serialize(builder.Build()));

            await Run(request);

            var item = Assert.Single(await ReadItems());
            Assert.Equal(uuid, item.Uuid);
            Assert.Equal(7.25m, item.Amount);
            Assert.True(item.Enabled);
        }

        [SkippableFact]
        public async Task Nulls_RoundTrip()
        {
            await CreateItems();
            await InsertItems(new Item { Id = "a" });

            var item = Assert.Single(await ReadItems());
            Assert.Null(item.Name);
            Assert.Null(item.Status);
            Assert.Null(item.Uuid);
        }

        [SkippableFact]
        public async Task Insert_ManyRows()
        {
            await CreateItems();

            await InsertItems(Enumerable.Range(0, 2500).Select(i => NewItem($"id{i:0000}", i)).ToArray());

            var results = await Run(TrakHoundTableRequest.Ansi($"select count(*) from {Item_};"));
            Assert.Equal("2500", results[0].Results[0].ReadSingleString());
        }

        [SkippableFact]
        public async Task Insert_DuplicateKeyIsAConflict()
        {
            await CreateItems();
            await InsertItems(NewItem("a"));

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { NewItem("a") });
            var response = await Service.Execute(builder.Build());

            Assert.Equal(409, response.StatusCode);
        }

        [SkippableFact]
        public async Task Insert_IgnoreDuplicateKeySkipsRows()
        {
            await Reset();

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendEnsureTable<IgnoreItem>();
            builder.AppendInsert(new[] { new IgnoreItem { Id = "a", Name = "first" } });
            await Run(builder.Build());

            builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { new IgnoreItem { Id = "a", Name = "second" }, new IgnoreItem { Id = "b", Name = "new" } });
            await Run(builder.Build());

            var results = await Run(TrakHoundTableRequest.Ansi("select \"name\" from \"trakhound_conformance\".\"ignore_item\" order by \"id\";"));
            Assert.Equal(new[] { "first", "new" }, results[0].Results[0].ReadMultipleString());
        }

        [SkippableFact]
        public async Task Insert_OverwriteReplacesRows()
        {
            await CreateItems();
            await InsertItems(NewItem("a", 1), NewItem("b", 2));

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { NewItem("a", 10) }, true);
            await Run(builder.Build());

            var items = await ReadItems();
            Assert.Equal(new[] { 10, 2 }, items.Select(o => o.Count));
        }

        [SkippableFact]
        public async Task Insert_OverwriteWithCompoundKey()
        {
            await Reset();

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendEnsureTable<CompoundItem>();
            builder.AppendInsert(new[] { new CompoundItem { A = "a", B = "1" }, new CompoundItem { A = "a", B = "2" } });
            await Run(builder.Build());

            builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { new CompoundItem { A = "a", B = "1" } }, true);
            await Run(builder.Build());

            var results = await Run(TrakHoundTableRequest.Ansi("select count(*) from \"trakhound_conformance\".\"compound_item\";"));
            Assert.Equal("2", results[0].Results[0].ReadSingleString());
        }

        [SkippableFact]
        public async Task Merge_UpdatesAndInserts()
        {
            await CreateItems();
            await InsertItems(NewItem("a", 1, "old"));

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendMerge(new[] { NewItem("a", 2, "new"), NewItem("b", 3, "added") });
            await Run(builder.Build());

            var items = await ReadItems();
            Assert.Equal(new[] { "new", "added" }, items.Select(o => o.Name));
            Assert.Equal(new[] { 2, 3 }, items.Select(o => o.Count));
        }

        [SkippableFact]
        public async Task Merge_OnlyUpdatesWhenAnUpdateColumnChanged()
        {
            await CreateItems();
            await InsertItems(NewItem("a", 1, "same"));

            // Count isn't an Update Column, so it is left alone when Name didn't change
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendMerge(new[] { NewItem("a", 99, "same") });
            await Run(builder.Build());

            Assert.Equal(1, Assert.Single(await ReadItems()).Count);
        }

        [SkippableFact]
        public async Task Merge_UpdatesAnUpdateColumnToNull()
        {
            await CreateItems();
            await InsertItems(NewItem("a", 1, "old"));

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendMerge(new[] { NewItem("a", 2, null) });
            await Run(builder.Build());

            var item = Assert.Single(await ReadItems());
            Assert.Null(item.Name);
            Assert.Equal(2, item.Count);
        }

        [SkippableFact]
        public async Task Index_CanBeUnique()
        {
            await Reset();

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendEnsureTable<UniqueItem>();
            builder.AppendInsert(new[] { new UniqueItem { Id = "a", Name = "same" } });
            await Run(builder.Build());

            builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { new UniqueItem { Id = "b", Name = "same" } });

            Assert.Equal(409, (await Service.Execute(builder.Build())).StatusCode);
        }

        [SkippableFact]
        public async Task Transaction_RollsBackWhenAStepFails()
        {
            await CreateItems();
            await InsertItems(NewItem("a"));

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { NewItem("b") });
            builder.AppendInsert(new[] { NewItem("a") }); // Duplicate key
            var response = await Service.Execute(builder.Build());

            Assert.False(response.Success);
            Assert.Equal(new[] { "a" }, (await ReadItems()).Select(o => o.Id));
        }

        [SkippableFact]
        public async Task Transaction_CanBeTurnedOff()
        {
            await CreateItems();
            await InsertItems(NewItem("a"));

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { NewItem("b") });
            builder.AppendInsert(new[] { NewItem("a") }); // Duplicate key
            var response = await Service.Execute(builder.Build(useTransaction: false));

            Assert.False(response.Success);
            Assert.Equal(new[] { "a", "b" }, (await ReadItems()).Select(o => o.Id));
        }

        [SkippableFact]
        public async Task Steps_ReturnAResultEach()
        {
            await CreateItems();

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { NewItem("a"), NewItem("b") });
            builder.AppendQuery($"select \"id\" from {Item_} order by \"id\";");
            var results = await Run(builder.Build());

            Assert.Equal(2, results.Length);
            Assert.Equal("(2 rows affected)", results[0].Message);
            Assert.Equal(new[] { "a", "b" }, results[1].Results[0].ReadMultipleString());
        }

        [SkippableFact]
        public async Task Ansi_SupportsTheDocumentedSubset()
        {
            await CreateItems();
            await InsertItems(NewItem("a", 1, "x"), NewItem("b", 2, "x"), new Item { Id = "c", Count = 3 });

            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("minimum", 1);
            parameters.Add("match", "x");

            var query = $@"with ""ranked"" as
(
    select
        ""id"",
        coalesce(""name"", 'none') as ""name"",
        ""item_count"",
        row_number() over (partition by coalesce(""name"", 'none') order by ""item_count"" desc) as ""item_rank""
    from {Item_}
    where ""item_count"" >= @minimum
)
select
    ""name"",
    count(*) as ""total"",
    sum(case when ""name"" = @match then ""item_count"" else 0 end) as ""matched"",
    max(case when ""item_rank"" = 1 then ""id"" end) as ""last_id"",
    concat(""name"", '-', count(*)) as ""label"",
    nullif(max(""item_count""), 0) as ""largest""
from ""ranked""
group by ""name""
order by ""name"";";

            var results = await Run(TrakHoundTableRequest.Ansi(query, parameters));
            var rows = results[0].Results[0].Rows;

            Assert.Equal(2, rows.Length);
            Assert.Equal("none", rows[0][0]);
            Assert.Equal(3L, Convert.ToInt64(rows[1][2]));
            Assert.Equal("b", rows[1][3]);
        }

        [SkippableFact]
        public async Task Ansi_LikeEscapesWildcards()
        {
            await CreateItems();
            await InsertItems(NewItem("a%b"), NewItem("a[b"), NewItem("axb"));

            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("percent", "a\\%%");
            parameters.Add("bracket", "a\\[%");

            var results = await Run(TrakHoundTableRequest.Ansi($"select \"id\" from {Item_} where \"id\" like @percent escape '\\' or \"id\" like @bracket escape '\\' order by \"id\";", parameters));

            Assert.Equal(new[] { "a%b", "a[b" }, results[0].Results[0].ReadMultipleString());
        }

        [SkippableFact]
        public async Task Values_WithBatchSeparatorsAreKept()
        {
            await CreateItems();
            await InsertItems(NewItem("a", 0, "line\r\nGO\r\nGo to settings"));

            Assert.Equal("line\r\nGO\r\nGo to settings", Assert.Single(await ReadItems()).Name);
        }

        [SkippableFact]
        public async Task Ansi_ColumnNamesAreLowercase()
        {
            await CreateItems();
            await InsertItems(NewItem("a"));

            var results = await Run(TrakHoundTableRequest.Ansi($"select \"id\", \"item_count\" as \"a_count\" from {Item_};"));

            Assert.Equal(new[] { "id", "a_count" }, results[0].Results[0].Columns.Select(o => o.Name));
        }

        [SkippableFact]
        public async Task Paging_SkipsAndTakes()
        {
            await CreateItems();
            await InsertItems(NewItem("a"), NewItem("b"), NewItem("c"), NewItem("d"));

            var results = await Run(TrakHoundTableRequest.Ansi($"select \"id\" from {Item_} order by \"id\";", skip: 1, take: 2));
            Assert.Equal(new[] { "b", "c" }, results[0].Results[0].ReadMultipleString());

            results = await Run(TrakHoundTableRequest.Ansi($"select \"id\" from {Item_} order by \"id\";", skip: 3));
            Assert.Equal(new[] { "d" }, results[0].Results[0].ReadMultipleString());

            results = await Run(TrakHoundTableRequest.Ansi($"select \"id\" from {Item_} order by \"id\";", take: 1));
            Assert.Equal(new[] { "a" }, results[0].Results[0].ReadMultipleString());
        }

        [SkippableFact]
        public async Task Variants_PreferTheServicesDialect()
        {
            var variants = new TrakHoundTableQueryVariants
            {
                [TrakHoundTableDialects.Ansi] = "select 'ansi';",
                [Dialect] = "select 'native';"
            };

            var results = await Run(TrakHoundTableRequest.Query(variants));

            Assert.Equal("native", results[0].Results[0].ReadSingleString());
        }

        [SkippableFact]
        public async Task Variants_NothingRunsWhenOneStepHasNoVariant()
        {
            await CreateItems();

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { NewItem("a") });
            builder.AppendQuery(new TrakHoundTableQueryVariants { ["other"] = "select 1;" });
            var response = await Service.Execute(builder.Build(useTransaction: false));

            Assert.Equal(405, response.StatusCode);
            Assert.Empty(await ReadItems());
        }

        [SkippableFact]
        public async Task DeleteBatched_DeletesEveryMatchingRow()
        {
            await CreateItems();
            await InsertItems(Enumerable.Range(0, 25).Select(i => NewItem($"id{i:00}", i)).ToArray());

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendDeleteBatched<Item>("\"target\".\"item_count\" < @limit", 10);
            builder.Parameters.Add("limit", 20);
            var results = await Run(builder.Build(useTransaction: false));

            Assert.Equal("(20 rows affected)", results[0].Message);
            Assert.Equal(5, (await ReadItems()).Count);
        }

        [SkippableFact]
        public async Task DeleteBatched_CanUseOtherTables()
        {
            await CreateItems();
            await InsertItems(NewItem("a", 1), NewItem("b", 2));

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendDeleteBatched<Item>($"exists (select 1 from {Item_} as \"other\" where \"other\".\"id\" = \"target\".\"id\" and \"other\".\"item_count\" = 1)", 10);
            await Run(builder.Build(useTransaction: false));

            Assert.Equal(new[] { "b" }, (await ReadItems()).Select(o => o.Id));
        }

        [SkippableFact]
        public async Task DeleteBatched_CantBeInATransaction()
        {
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendDeleteBatched<Item>("1 = 1");

            Assert.Equal(400, (await Service.Execute(builder.Build())).StatusCode);
        }

        [SkippableFact]
        public async Task Errors_QueryErrorsAreBadRequests()
        {
            var response = await Service.Execute(TrakHoundTableRequest.Ansi("selct nothing;"));
            Assert.Equal(400, response.StatusCode);

            response = await Service.Execute(TrakHoundTableRequest.Ansi("select * from \"no_such_table\";"));
            Assert.Equal(400, response.StatusCode);
        }

        [SkippableFact]
        public async Task List_IncludesTablesInASchema()
        {
            await CreateItems();

            var response = await Service.List();

            Assert.True(response.Success, response.Message);
            Assert.Contains(response.Result, o => o.Schema == Schema && o.Name == "item");
        }

        [SkippableFact]
        public async Task GetColumns_DescribesATable()
        {
            await CreateItems();

            var response = await Service.GetColumns($"{Schema}.item");

            Assert.True(response.Success, response.Message);
            var columns = response.Result.ToDictionary(o => o.Name);
            Assert.False(columns["id"].AllowNull);
            Assert.True(columns["name"].AllowNull);
            Assert.Equal(TrakHoundTableDataType.Float, columns["value"].DataType);
            Assert.Equal(TrakHoundTableDataType.Decimal, columns["amount"].DataType);

            Assert.Equal(404, (await Service.GetColumns($"{Schema}.no_such_table")).StatusCode);
        }

        [SkippableFact]
        public async Task Native_QueriesStillWork()
        {
            var response = await Service.ExecuteQuery("select 1;");

            Assert.True(response.Success, response.Message);
            Assert.Equal("1", response.Result[0].ReadSingleString());
        }

        [SkippableFact]
        public async Task Availability_IsAvailable()
        {
            var response = await Service.CheckAvailability();

            Assert.True(response.Result.IsAvailable, response.Result.Message);
        }
    }
}
