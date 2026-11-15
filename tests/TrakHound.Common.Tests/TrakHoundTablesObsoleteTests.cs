// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Globalization;
using TrakHound.Services.Tables;
using TrakHound.Services.Tables.Attributes;

#pragma warning disable CS0618 // Tests the obsolete SQL generation

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// The SQL that the obsolete TrakHoundTables methods generate from classes, which packages built against them still rely on
    /// </summary>
    public class TrakHoundTablesObsoleteTests
    {
        public enum TestStatus
        {
            Running,
            Stopped
        }

        [Table("test_item", "test")]
        [IgnoreDuplicateKey]
        public class TestItem
        {
            [PrimaryKey]
            public string Id { get; set; }

            [Column("MyName", "nvarchar(max)")]
            [Update]
            public string Name { get; set; }

            public double Value { get; set; }

            public TimeSpan Duration { get; set; }

            public TestStatus? Status { get; set; }

            public Guid? Uuid { get; set; }

            public int Count { get; set; }
        }

        public class NoKeyItem
        {
            public string Name { get; set; }
        }

        [Table("plain")]
        public class PlainKeyItem
        {
            [PrimaryKey(TrakHoundSortOrder.Descending)]
            public long Id { get; set; }

            public string Name { get; set; }
        }

        [Table("odd]name")]
        public class OddNameItem
        {
            [Column("col]a")]
            public string A { get; set; }
        }

        [Table("indexed", "test")]
        [TableIndex("ix_indexed_instance_start", "instance_id", "start desc", Include = new[] { "status" })]
        [TableIndex("ix_indexed_status", "status")]
        public class IndexedItem
        {
            [PrimaryKey]
            public string InstanceId { get; set; }

            [PrimaryKey]
            public string Id { get; set; }

            public long Start { get; set; }

            public string Status { get; set; }
        }

        [TableIndex("ix_missing", "missing")]
        public class BadIndexItem
        {
            public string Name { get; set; }
        }


        [Fact]
        public void Create_UsesColumnAttributeNameAsWritten()
        {
            var query = TrakHoundTables.GenerateCreate<TestItem>();

            Assert.Contains("[MyName] nvarchar(max)", query);
            Assert.DoesNotContain("my_name", query);
            Assert.Contains("[id] varchar(255) NOT NULL", query);
            Assert.Contains("create table [test].[test_item]", query);
        }

        [Fact]
        public void Create_NullableColumnsAllowNull()
        {
            var query = TrakHoundTables.GenerateCreate<TestItem>();

            Assert.Contains("[status] varchar(255),", query);
            Assert.Contains("[uuid] uniqueidentifier,", query);
            Assert.Contains("[count] int NOT NULL", query);
        }

        [Fact]
        public void Create_IgnoreDuplicateKeyOnlyWhenSet()
        {
            Assert.Contains("IGNORE_DUP_KEY = ON", TrakHoundTables.GenerateCreate<TestItem>());

            var plain = TrakHoundTables.GenerateCreate<PlainKeyItem>();
            Assert.DoesNotContain("IGNORE_DUP_KEY", plain);
            Assert.Contains("primary key clustered ([id] desc)", plain);
        }

        [Fact]
        public void Create_EscapesIdentifiers()
        {
            var query = TrakHoundTables.GenerateCreate<OddNameItem>();

            Assert.Contains("create table [odd]]name] ([col]]a] varchar(255))", query);
            Assert.Contains("[name] = N'odd]name'", query);
        }

        [Fact]
        public void Insert_UsesUnicodeStrings()
        {
            var query = TrakHoundTables.GenerateInsert(new[] { new TestItem { Id = "a", Name = "Größe 測定 'x'" } });

            Assert.Contains("N'Größe 測定 ''x'''", query);
        }

        [Fact]
        public void Insert_NumbersDoNotDependOnCulture()
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                var query = TrakHoundTables.GenerateInsert(new[] { new TestItem { Id = "a", Value = 1.5, Count = 1000 } });

                Assert.Contains("1.5", query);
                Assert.DoesNotContain("1,5", query);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }

        [Fact]
        public void Insert_LongDurationsAreWholeNanoseconds()
        {
            var query = TrakHoundTables.GenerateInsert(new[] { new TestItem { Id = "a", Duration = TimeSpan.FromDays(30) } });

            Assert.Contains("2592000000000000", query);
            Assert.DoesNotContain("E+", query);
        }

        [Fact]
        public void Insert_QuotesNullableEnumsAndGuids()
        {
            var uuid = Guid.NewGuid();
            var query = TrakHoundTables.GenerateInsert(new[] { new TestItem { Id = "a", Status = TestStatus.Stopped, Uuid = uuid } });

            Assert.Contains("N'Stopped'", query);
            Assert.Contains($"N'{uuid}'", query);
        }

        [Fact]
        public void Insert_RejectsNaN()
        {
            Assert.Throws<ArgumentException>(() => TrakHoundTables.GenerateInsert(new[] { new TestItem { Id = "a", Value = double.NaN } }));
        }

        [Fact]
        public void Insert_SplitsIntoStatementsOf1000Rows()
        {
            var items = Enumerable.Range(0, 2500).Select(i => new PlainKeyItem { Id = i, Name = "x" });
            var query = TrakHoundTables.GenerateInsert(items);

            Assert.Equal(3, CountOccurrences(query, "insert into [plain]"));
        }

        [Fact]
        public void Insert_OverwriteDeletesByColumnName()
        {
            var query = TrakHoundTables.GenerateInsert(new[] { new TestItem { Id = "a" } }, overwrite: true);

            Assert.Contains("delete [target] from [test].[test_item] as [target]", query);
            Assert.Contains("on [input].[id] = [target].[id]", query);
            Assert.True(query.IndexOf("delete") < query.IndexOf("insert into"));
        }

        [Fact]
        public void Insert_OverwriteOnTableVariable()
        {
            var query = TrakHoundTables.GenerateInsert(new[] { new TestItem { Id = "a" } }, tableName: "@items", overwrite: true);

            Assert.Contains("delete [target] from @items as [target]", query);
            Assert.Contains("insert into @items", query);
            Assert.DoesNotContain("[@items]", query);
        }

        [Fact]
        public void Merge_TableVariableUsesColumnDefinitions()
        {
            var query = TrakHoundTables.GenerateMerge(new[] { new TestItem { Id = "a", Name = new string('x', 500) } });

            Assert.Contains("[MyName] nvarchar(max)", query.Split("\r\n")[0]);
        }

        [Fact]
        public void Merge_ComparesUpdateColumnsIncludingNulls()
        {
            var query = TrakHoundTables.GenerateMerge(new[] { new TestItem { Id = "a" } });

            Assert.Contains("merge [test].[test_item] as [target] using", query);
            Assert.Contains("on [input].[id] = [target].[id]", query);
            Assert.Contains("when matched and exists (select [input].[MyName] except select [target].[MyName])", query);
            Assert.Contains("[target].[MyName] = [input].[MyName]", query);
            Assert.Contains("when not matched by target", query);
        }

        [Fact]
        public void Merge_WithoutPrimaryKeyThrows()
        {
            Assert.Throws<InvalidOperationException>(() => TrakHoundTables.GenerateMerge(new[] { new NoKeyItem { Name = "a" } }));
        }

        [Fact]
        public void Generate_ReturnsNullForNoItems()
        {
            Assert.Null(TrakHoundTables.GenerateInsert(Array.Empty<TestItem>()));
            Assert.Null(TrakHoundTables.GenerateMerge<TestItem>(null));
        }

        [Fact]
        public void GetColumnNames_UsesColumnAttributeName()
        {
            var names = TrakHoundTables.GetColumnNames<TestItem>();

            Assert.Equal(new[] { "id", "MyName", "value", "duration", "status", "uuid", "count" }, names);
        }

        [Fact]
        public void CreateTableVariable_FormatsValues()
        {
            var query = TrakHoundTables.CreateTableVariable("ids", "nvarchar(255)", new[] { "a'b", null });

            Assert.Equal("declare @ids as table ([value] nvarchar(255));\r\ninsert into @ids values (N'a''b'), (null);", query);
        }

        [Fact]
        public void Split_OnGoLines()
        {
            var statements = TrakHoundTables.SplitQueryStatements("select 1;\r\nGO\r\nselect 2;\r\ngo 5\r\nselect 3;").ToList();

            Assert.Equal(3, statements.Count);
        }

        [Fact]
        public void Split_IgnoresGoInsideStringsAndComments()
        {
            var query = "select 'line one\r\nGo to settings\r\nGO\r\n';\r\n/* comment\r\nGO\r\n*/\r\nselect [a\r\nGO\r\n];\r\n-- it's a comment\r\nGO\r\nselect 2;";
            var statements = TrakHoundTables.SplitQueryStatements(query).ToList();

            Assert.Equal(2, statements.Count);
            Assert.EndsWith("select 2;\r\n", statements[1]);
        }

        [Fact]
        public void Builder_KeepsMultiLineValuesInOneStatement()
        {
            var builder = new TrakHoundTableQueryBuilder();
            builder.AppendInsert(new[] { new TestItem { Id = "a", Name = "first\r\nGO\r\nlast" } });

            var script = builder.Build();

            Assert.Contains("N'first\r\nGO\r\nlast'", script);
            Assert.Single(TrakHoundTables.SplitQueryStatements(script));
        }

        [Fact]
        public void Create_AddsIndexesForNewAndExistingTables()
        {
            var query = TrakHoundTables.GenerateCreate<IndexedItem>();
            var lines = query.Split("\r\n");

            Assert.Contains("if not exists (select * from sys.indexes where [object_id] = OBJECT_ID(N'[test].[indexed]') and [name] = N'ix_indexed_instance_start')", lines);
            Assert.Contains("   create nonclustered index [ix_indexed_instance_start] on [test].[indexed] ([instance_id], [start] desc) include ([status]);", lines);
            Assert.Contains("   create nonclustered index [ix_indexed_status] on [test].[indexed] ([status]);", lines);

            // The Index check follows the Table's block (not inside it), so Indexes are added to existing Tables
            var indexLine = Array.FindIndex(lines, o => o.Contains("sys.indexes"));
            Assert.Equal("end;", lines[indexLine - 1]);
            Assert.True(indexLine > Array.FindIndex(lines, o => o.Contains("create table")));
        }

        [Fact]
        public void Create_WithoutIndexesHasNoIndexChecks()
        {
            Assert.DoesNotContain("sys.indexes", TrakHoundTables.GenerateCreate<TestItem>());
        }

        [Fact]
        public void Create_IndexWithUnknownColumnThrows()
        {
            Assert.Throws<InvalidOperationException>(() => TrakHoundTables.GenerateCreate<BadIndexItem>());
        }

        [Fact]
        public void Insert_WithParametersReferencesParameters()
        {
            var parameters = new TrakHoundTableParameterCollection();
            var items = new[]
            {
                new TestItem { Id = "a", Name = "x'y", Duration = TimeSpan.FromSeconds(1), Status = TestStatus.Stopped },
                new TestItem { Id = "b" }
            };

            var query = TrakHoundTables.GenerateInsert(items, parameters);

            Assert.Contains("values\r\n(@p0, @p1, @p2, @p3, @p4, @p5, @p6),\r\n(@p7, @p8, @p9, @p10, @p11, @p12, @p13);", query);
            Assert.DoesNotContain("N'", query);

            var values = parameters.ToList();
            Assert.Equal(14, values.Count);
            Assert.Equal("p0", values[0].Name);
            Assert.Equal(TrakHoundTableDataType.String, values[0].DataType);
            Assert.Equal("a", values[0].Value);
            Assert.Equal(TrakHoundTableDataType.String, values[1].DataType); // The Column definition
            Assert.Equal("x'y", values[1].Value);
            Assert.Equal(1000000000L, values[3].Value); // Nanoseconds
            Assert.Equal("Stopped", values[4].Value);
            Assert.Null(values[5].Value); // Null Guid
            Assert.Null(values[8].Value); // Null Name
        }

        [Fact]
        public void Insert_WithParametersIsTheSameQueryForTheSameShape()
        {
            var first = TrakHoundTables.GenerateInsert(new[] { new TestItem { Id = "a", Count = 1 } }, new TrakHoundTableParameterCollection());
            var second = TrakHoundTables.GenerateInsert(new[] { new TestItem { Id = "b", Name = "other", Count = 2 } }, new TrakHoundTableParameterCollection());

            Assert.Equal(first, second);
        }

        [Fact]
        public void Insert_OverwriteWithParametersReusesKeyParameters()
        {
            var parameters = new TrakHoundTableParameterCollection();
            var query = TrakHoundTables.GenerateInsert(new[] { new TestItem { Id = "a" } }, parameters, overwrite: true);

            Assert.Contains("inner join (values\r\n(@p0)\r\n) as [input] ([id])", query);
            Assert.Equal(7, parameters.Count);
        }

        [Fact]
        public void Insert_WithParametersRejectsNaN()
        {
            Assert.Throws<ArgumentException>(() => TrakHoundTables.GenerateInsert(new[] { new TestItem { Id = "a", Value = double.NaN } }, new TrakHoundTableParameterCollection()));
        }

        [Fact]
        public void Merge_WithParametersIsTheSameQueryForTheSameShape()
        {
            var first = TrakHoundTables.GenerateMerge(new[] { new TestItem { Id = "a" } }, new TrakHoundTableParameterCollection());
            var second = TrakHoundTables.GenerateMerge(new[] { new TestItem { Id = "b", Name = "other" } }, new TrakHoundTableParameterCollection());

            Assert.Equal(first, second);
            Assert.StartsWith("declare @merge_input_0 as table", first);
            Assert.Contains("values\r\n(@p0, @p1, @p2, @p3, @p4, @p5, @p6);", first);
        }

        [Fact]
        public void Builder_WritesLiteralsByDefault()
        {
            var builder = new TrakHoundTableQueryBuilder();
            builder.AppendInsert(new[] { new TestItem { Id = "a" } });

            Assert.NotNull(builder.Parameters);
            Assert.Equal(0, builder.Parameters.Count);
            Assert.Contains("N'a'", builder.Build());
        }

        [Fact]
        public void Builder_UseParametersNumbersParametersAcrossStatements()
        {
            var builder = new TrakHoundTableQueryBuilder();
            builder.UseParameters = true;
            builder.AppendInsert(new[] { new TestItem { Id = "a" }, new TestItem { Id = "b" } });
            builder.AppendMerge(new[] { new TestItem { Id = "c" } });

            var script = builder.Build();

            Assert.Equal(21, builder.Parameters.Count);
            Assert.Contains("(@p7, @p8, @p9, @p10, @p11, @p12, @p13)", script);
            Assert.Contains("declare @merge_input_14 as table", script);
            Assert.Contains("@p20", script);
            Assert.DoesNotContain("N'a'", script);
        }

        [Fact]
        public void Builder_UseParametersWritesLiteralsOverTheParameterLimit()
        {
            var builder = new TrakHoundTableQueryBuilder();
            builder.UseParameters = true;

            // 1000 rows of 2 Columns fill the 2000 Parameters exactly
            builder.AppendInsert(Enumerable.Range(0, 1000).Select(i => new PlainKeyItem { Id = i, Name = "x" }));
            Assert.Equal(2000, builder.Parameters.Count);

            // Over the limit, so written with literals
            builder.AppendInsert(new[] { new PlainKeyItem { Id = 1000, Name = "last" } });
            Assert.Equal(2000, builder.Parameters.Count);
            Assert.Contains("(1000, N'last')", builder.Build());

            // A batch that doesn't fit on its own (300 rows of 7 Columns) is written with literals
            var large = new TrakHoundTableQueryBuilder();
            large.UseParameters = true;
            large.AppendInsert(Enumerable.Range(0, 300).Select(i => new TestItem { Id = $"id{i}" }));
            Assert.Equal(0, large.Parameters.Count);
            Assert.Contains("N'id299'", large.Build());
        }


        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }
    }
}
