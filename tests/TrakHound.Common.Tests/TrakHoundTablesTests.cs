// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services.Tables;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// The Definitions and rows that TrakHoundTables gets from classes
    /// </summary>
    public class TrakHoundTablesTests
    {
        public enum TestStatus
        {
            Running,
            Stopped
        }

        [Table("test_item", "test")]
        public class TestItem
        {
            [PrimaryKey]
            public string Id { get; set; }

            [Column("MyName")]
            public string Name { get; set; }

            public double Value { get; set; }

            public TimeSpan Duration { get; set; }

            public TestStatus? Status { get; set; }

            public Guid? Uuid { get; set; }

            public bool Enabled { get; set; }

            public int Count { get; set; }

            public ulong Big { get; set; }
        }

        [TableIndex("ix_indexed", "name desc", "status asc", Include = new[] { "count" })]
        public class IndexedItem
        {
            public string Name { get; set; }

            public string Status { get; set; }

            public int Count { get; set; }
        }

        [TableIndex("ix_missing", "missing")]
        public class BadIndexItem
        {
            public string Name { get; set; }
        }


        private static object Value(TestItem item, string column)
        {
            var i = TrakHoundTables.GetColumnNames<TestItem>().ToList().IndexOf(column);
            return TrakHoundTables.GetRows(new[] { item })[0][i];
        }


        [Fact]
        public void GetColumnNames_UsesColumnAttributeName()
        {
            var names = TrakHoundTables.GetColumnNames<TestItem>();

            Assert.Contains("MyName", names);
            Assert.Contains("id", names);
            Assert.DoesNotContain("name", names);
        }

        [Fact]
        public void Rows_LongDurationsAreWholeNanoseconds()
        {
            Assert.Equal(TimeSpan.FromDays(30).Ticks * 100, Value(new TestItem { Duration = TimeSpan.FromDays(30) }, "duration"));
        }

        [Fact]
        public void Rows_ConvertValuesToWhatIsStored()
        {
            var uuid = Guid.NewGuid();

            Assert.Equal("Stopped", Value(new TestItem { Status = TestStatus.Stopped }, "status"));
            Assert.Equal(uuid, Value(new TestItem { Uuid = uuid }, "uuid"));
            Assert.Equal(1, Value(new TestItem { Enabled = true }, "enabled"));
            Assert.Equal((decimal)ulong.MaxValue, Value(new TestItem { Big = ulong.MaxValue }, "big"));
        }

        [Fact]
        public void Rows_NullAndEmptyStringsAreNull()
        {
            Assert.Null(Value(new TestItem { Name = null }, "MyName"));
            Assert.Null(Value(new TestItem { Name = "" }, "MyName"));
            Assert.Null(Value(new TestItem { Status = null }, "status"));
        }

        [Fact]
        public void Rows_RejectNaN()
        {
            Assert.Throws<ArgumentException>(() => TrakHoundTables.GetRows(new[] { new TestItem { Value = double.NaN } }));
        }

        [Fact]
        public void Definition_ParsesIndexColumns()
        {
            var index = Assert.Single(TrakHoundTables.GetDefinition<IndexedItem>().Indexes);

            Assert.Equal("name", index.Columns[0].Name);
            Assert.Equal(TrakHoundSortOrder.Descending, index.Columns[0].Order);
            Assert.Equal(TrakHoundSortOrder.Ascending, index.Columns[1].Order);
            Assert.Equal(new[] { "count" }, index.IncludeColumns);
        }

        [Fact]
        public void Definition_IndexWithUnknownColumnThrows()
        {
            Assert.Throws<InvalidOperationException>(() => TrakHoundTables.GetDefinition<BadIndexItem>());
        }
    }
}
