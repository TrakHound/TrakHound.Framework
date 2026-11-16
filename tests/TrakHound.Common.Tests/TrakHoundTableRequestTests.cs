// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Services.Tables;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// How Table Requests are built and sent
    /// </summary>
    public class TrakHoundTableRequestTests
    {
        [Table("item", "test")]
        private class Item
        {
            [PrimaryKey]
            public string Id { get; set; }

            public decimal Amount { get; set; }

            public DateTime Timestamp { get; set; }

            public bool Enabled { get; set; }

            public ulong Big { get; set; }

            public double Value { get; set; }
        }


        [Fact]
        public void Variants_UseTheFirstSupportedDialect()
        {
            var variants = new TrakHoundTableQueryVariants { ["ansi"] = "a", ["SqlServer"] = "s" };

            Assert.Equal("s", variants.Select(new[] { "sqlserver", "ansi" }));
            Assert.Equal("a", variants.Select(new[] { "postgres", "ansi" }));
            Assert.Null(variants.Select(new[] { "kusto" }));
        }

        [Fact]
        public void Variants_FromDirectory_NamesEachFileByDialect()
        {
            var path = Directory.CreateTempSubdirectory().FullName;
            try
            {
                Assert.Throws<FileNotFoundException>(() => TrakHoundTableQueryVariants.FromDirectory(path));

                File.WriteAllText(Path.Combine(path, "postgres.sql"), "create view v;");

                Assert.Equal("create view v;", TrakHoundTableQueryVariants.FromDirectory(path)["postgres"]);
            }
            finally
            {
                Directory.Delete(path, true);
            }

            Assert.Throws<DirectoryNotFoundException>(() => TrakHoundTableQueryVariants.FromDirectory(path));
        }

        [Fact]
        public void Builder_RecordsStepsInOrder()
        {
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendEnsureTable<Item>();
            builder.AppendQuery("delete from \"item\" where \"id\" = @id;");
            builder.Parameters.Add("id", "a");
            builder.AppendInsert(new[] { new Item { Id = "a" } }, true);
            builder.AppendMerge(new[] { new Item { Id = "b" } });

            var request = builder.Build();

            Assert.Equal(new[] { TrakHoundTableStepType.EnsureTable, TrakHoundTableStepType.Query, TrakHoundTableStepType.Insert, TrakHoundTableStepType.Merge }, request.Steps.Select(o => o.Type));
            Assert.True(request.Transaction);
            Assert.False(request.ReadOnly);
            Assert.True(request.Steps[2].Overwrite);
            Assert.Equal("id", request.Parameters[0].Name);
        }

        [Fact]
        public void Builder_SkipsEmptyItems()
        {
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(Array.Empty<Item>());
            builder.AppendMerge<Item>(null);

            Assert.Null(builder.Build());
        }

        [Fact]
        public void Rows_RejectValuesAColumnCantStore()
        {
            Assert.Throws<ArgumentException>(() => TrakHoundTables.GetRows(new[] { new Item { Id = "a", Value = double.NaN } }));
        }

        [Fact]
        public void Request_RoundTripsAsJson()
        {
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { new Item { Id = "a", Amount = 1234567890.1234567891m, Enabled = true, Big = ulong.MaxValue, Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) } });
            builder.AppendQuery(new TrakHoundTableQueryVariants { ["ansi"] = "select 1;" }, 5, 10);
            builder.Parameters.Add("id", "a");

            var json = JsonSerializer.Serialize(builder.Build());
            var request = JsonSerializer.Deserialize<TrakHoundTableRequest>(json);

            Assert.Equal(TrakHoundTableStepType.Insert, request.Steps[0].Type);
            Assert.Equal("select 1;", request.Steps[1].Variants["ANSI"]); // Dialects ignore case
            Assert.Equal(5, request.Steps[1].Skip);
            Assert.Equal("a", request.GetParameters().First().Value);

            var columns = request.Steps[0].Definition.Columns;
            var row = request.Steps[0].Rows[0];
            object Value(string name)
            {
                var i = Array.FindIndex(columns, o => o.Name == name);
                return TrakHoundTableRequest.ConvertValue(columns[i].DataType, row[i]);
            }

            Assert.Equal("a", Value("id"));
            Assert.Equal(1234567890.1234567891m, Value("amount")); // A double would lose this precision
            Assert.Equal((decimal)ulong.MaxValue, Value("big"));
            Assert.IsType<long>(Value("timestamp"));
            Assert.Equal(1, Value("enabled"));
        }

        [Fact]
        public void Transaction_False_IsSent()
        {
            // The client leaves out default values, and Transaction defaults to true, so a Batched Delete over HTTP was refused
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendDeleteBatched("test", "item", "\"target\".\"id\" = 'a'");

            var request = Json.Convert<TrakHoundTableRequest>(Json.Convert(builder.Build(useTransaction: false)));

            Assert.False(request.Transaction);
        }
    }
}
