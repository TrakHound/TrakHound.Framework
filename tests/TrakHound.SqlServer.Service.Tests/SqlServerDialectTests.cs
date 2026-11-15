// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services.Tables;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.SqlServer.Service.Tests
{
    /// <summary>
    /// The T-SQL generated for the steps of a Table Request
    /// </summary>
    public class SqlServerDialectTests
    {
        [Table("item", "test")]
        [IgnoreDuplicateKey]
        [TableIndex("ix_item_count", "item_count desc", Include = new[] { "name" })]
        private class Item
        {
            [PrimaryKey]
            public string Id { get; set; }

            [Column(Length = TrakHoundTableColumnDefinition.MaxLength, Unicode = true)]
            [Update]
            public string Name { get; set; }

            [Column("item_count")]
            public int Count { get; set; }
        }

        [Table("plain")]
        private class NoKeyItem
        {
            public string Name { get; set; }
        }


        private class TypesItem
        {
            public ulong Big { get; set; }
            public decimal Amount { get; set; }
            public DateTime Timestamp { get; set; }
            public bool Enabled { get; set; }
            public char Letter { get; set; }
            [Column(Length = 255, Unicode = true)]
            public string Title { get; set; }
            public Guid Uuid { get; set; }
        }


        private readonly SqlServerDialect _dialect = new SqlServerDialect();

        private static TrakHoundTableDefinition Definition => TrakHoundTables.GetDefinition<Item>();

        private static object[][] Rows(int count) => Enumerable.Range(0, count).Select(i => new object[] { $"id{i}", "name", i }).ToArray();


        [Fact]
        public void Dialects_PreferSqlServer()
        {
            Assert.Equal(new[] { "sqlserver", "ansi" }, _dialect.SupportedDialects);
        }

        [Fact]
        public void EnsureTable_ChecksTheCatalog()
        {
            var query = Assert.Single(_dialect.GenerateEnsureTable(Definition)).Text;

            Assert.Contains("sys.schemas", query);
            Assert.Contains("create table [test].[item]", query);
            Assert.Contains("[id] varchar(255) NOT NULL", query);
            Assert.Contains("[name] nvarchar(max)", query);
            Assert.Contains("primary key clustered ([id]) with (IGNORE_DUP_KEY = ON)", query);
            Assert.Contains("create nonclustered index [ix_item_count] on [test].[item] ([item_count] desc) include ([name])", query);
        }

        [Fact]
        public void EnsureTable_KeepsTheTypesOfExistingTables()
        {
            var definition = TrakHoundTables.GetDefinition<TypesItem>();
            var query = Assert.Single(_dialect.GenerateEnsureTable(definition)).Text;

            Assert.Contains("[big] decimal(20, 0)", query);
            Assert.Contains("[amount] decimal(38, 10)", query);
            Assert.Contains("[timestamp] bigint", query);
            Assert.Contains("[enabled] bit", query);
            Assert.Contains("[letter] nchar(1)", query);
            Assert.Contains("[title] nvarchar(255)", query);
            Assert.Contains("[uuid] uniqueidentifier", query);
        }

        [Fact]
        public void Identifiers_AreEscaped()
        {
            Assert.Equal("[odd]]name]", _dialect.QuoteIdentifier("odd]name"));
        }

        [Fact]
        public void Insert_SplitsRowsToStayWithinTheLimits()
        {
            // 3 Columns allow 666 rows per command under the 2000 Parameter limit
            var statements = _dialect.GenerateInsert(Definition, Rows(700), false).ToList();

            Assert.Equal(2, statements.Count);
            Assert.Equal(1998, statements[0].Parameters.Count);
            Assert.Equal(102, statements[1].Parameters.Count);
            Assert.Contains("@p0", statements[1].Text);
        }

        [Fact]
        public void Insert_OverwriteDeletesFirst()
        {
            var statements = _dialect.GenerateInsert(Definition, Rows(1), true).ToList();

            Assert.Equal(2, statements.Count);
            Assert.StartsWith("delete [target]", statements[0].Text);
            Assert.StartsWith("insert into", statements[1].Text);
        }

        [Fact]
        public void Merge_OnlyUpdatesWhenAnUpdateColumnChanged()
        {
            var query = Assert.Single(_dialect.GenerateMerge(Definition, Rows(1))).Text;

            Assert.Contains("merge [test].[item] as [target]", query);
            Assert.Contains("exists (select [input].[name] except select [target].[name])", query);
        }

        [Fact]
        public void Merge_NeedsAPrimaryKey()
        {
            Assert.Throws<InvalidOperationException>(() => _dialect.GenerateMerge(TrakHoundTables.GetDefinition<NoKeyItem>(), new[] { new object[] { "a" } }).ToList());
        }

        [Fact]
        public void DeleteBatch_UsesTop()
        {
            Assert.Equal("delete top (500) [target] from [test].[item] as [target] where \"id\" = @id;", _dialect.GenerateDeleteBatch("item", "test", "\"id\" = @id", 500));
        }

        [Fact]
        public void Paging_UsesOffsetFetch()
        {
            Assert.Equal("select 1 order by 1 offset 10 rows fetch next 5 rows only;", _dialect.AddPaging("select 1 order by 1;", 10, 5));
            Assert.Equal("select 1;", _dialect.AddPaging("select 1;", null, null));
        }

        [Fact]
        public void Scripts_SplitOnGo()
        {
            var statements = _dialect.SplitScript("select 1;\r\nGO\r\nselect 'GO\r\nGO\r\nx';\r\ngo 5\r\nselect 3;").ToList();

            Assert.Equal(3, statements.Count);
            Assert.Contains("GO\r\nGO", statements[1]);
        }
    }
}
