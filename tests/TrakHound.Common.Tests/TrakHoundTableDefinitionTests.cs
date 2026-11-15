// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Services.Tables;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// How classes become neutral Table Definitions
    /// </summary>
    public class TrakHoundTableDefinitionTests
    {
        [Table("item", "test")]
        [IgnoreDuplicateKey]
        [TableIndex("ix_item_count", "item_count desc", IsUnique = true)]
        private class Item
        {
            [PrimaryKey]
            public string Id { get; set; }

            [Column(Length = TrakHoundTableColumnDefinition.MaxLength, Unicode = true)]
            [Update]
            public string Name { get; set; }

            [Column("item_count")]
            public int Count { get; set; }

            public DateTime Timestamp { get; set; }

            public bool Enabled { get; set; }

            public decimal Amount { get; set; }

            public ulong Big { get; set; }

            public Guid? Uuid { get; set; }
        }


        [Fact]
        public void Definition_DescribesTheClass()
        {
            var definition = TrakHoundTables.GetDefinition<Item>();

            Assert.Equal("item", definition.Name);
            Assert.Equal("test", definition.Schema);
            Assert.True(definition.IgnoreDuplicateKey);

            var columns = definition.Columns.ToDictionary(o => o.Name);

            Assert.True(columns["id"].IsPrimaryKey);
            Assert.False(columns["id"].AllowNull);
            Assert.Equal(255, columns["id"].Length);

            Assert.Equal(TrakHoundTableColumnDefinition.MaxLength, columns["name"].Length);
            Assert.True(columns["name"].Unicode);
            Assert.True(columns["name"].IsUpdate);

            Assert.Equal(TrakHoundTableDataType.Int32, columns["item_count"].DataType);
            Assert.Equal(TrakHoundTableDataType.Int64, columns["timestamp"].DataType);
            Assert.Equal(TrakHoundTableDataType.Boolean, columns["enabled"].DataType);
            Assert.Equal(38, columns["amount"].Precision);
            Assert.Equal(20, columns["big"].Precision);
            Assert.Equal(TrakHoundTableDataType.Guid, columns["uuid"].DataType);
            Assert.True(columns["uuid"].AllowNull);

            var index = Assert.Single(definition.Indexes);
            Assert.True(index.IsUnique);
            Assert.Equal(TrakHoundSortOrder.Descending, index.Columns[0].Order);
        }

        [Fact]
        public void Definition_RoundTripsAsJson()
        {
            var json = JsonSerializer.Serialize(TrakHoundTables.GetDefinition<Item>());
            var definition = JsonSerializer.Deserialize<TrakHoundTableDefinition>(json);

            Assert.Equal("item", definition.Name);
            Assert.Equal(TrakHoundTableDataType.Int32, definition.Columns.First(o => o.Name == "item_count").DataType);
            Assert.True(definition.Indexes[0].IsUnique);
        }

        [Fact]
        public void Parameter_RoundTripsAsJson()
        {
            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("id", Guid.Empty);
            parameters.Add("count", 5L);
            parameters.Add("inferred", (TrakHoundTableDataType?)null, "x");

            var json = JsonSerializer.Serialize(new TrakHoundTableQueryRequest("select 1;", parameters));
            var result = JsonSerializer.Deserialize<TrakHoundTableQueryRequest>(json).GetParameters().ToList();

            Assert.Equal(TrakHoundTableDataType.Guid, result[0].DataType);
            Assert.IsType<Guid>(result[0].Value);
            Assert.IsType<long>(result[1].Value);
            Assert.Null(result[2].DataType);
        }
    }
}
