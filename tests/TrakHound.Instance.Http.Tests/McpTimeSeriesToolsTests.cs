// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol.Server;
using System.Reflection;
using System.Text.Json;
using TrakHound.Instance.Mcp;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Instance.Http.Tests
{
    public class McpTimeSeriesToolsTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static IEnumerable<(string Name, McpServerToolAttribute Tool, TrakHoundMcpToolAttribute Access)> GetTools()
        {
            return typeof(TrakHoundMcpTimeSeriesTools).GetMethods()
                .Where(o => o.GetCustomAttribute<McpServerToolAttribute>() != null)
                .Select(o => (o.GetCustomAttribute<McpServerToolAttribute>().Name, o.GetCustomAttribute<McpServerToolAttribute>(), o.GetCustomAttribute<TrakHoundMcpToolAttribute>()));
        }


        [Fact]
        public void Only_Writing_Needs_The_Write_Permission()
        {
            var tools = GetTools().ToList();

            Assert.Equal(
                new[] { "timeseries_aggregate", "timeseries_fields", "timeseries_latest", "timeseries_measurements", "timeseries_read", "timeseries_series", "timeseries_write" },
                tools.Select(o => o.Name).OrderBy(o => o));

            Assert.All(tools, tool =>
            {
                Assert.NotNull(tool.Access);
                Assert.Equal(tool.Name == "timeseries_write", tool.Access.Write);
                Assert.Equal(tool.Name != "timeseries_write", tool.Tool.ReadOnly);
                if (tool.Name == "timeseries_write") Assert.True(tool.Tool.Destructive);
            });
        }

        [Fact]
        public void There_Is_No_Tool_That_Deletes()
        {
            Assert.DoesNotContain(GetTools(), o => o.Name.Contains("delete"));
        }

        [Fact]
        public void A_Range_With_Too_Many_Intervals_Says_What_Would_Work()
        {
            var message = TrakHoundMcpTimeSeriesTools.GetRangeError(T0, T0.AddDays(30), TimeSpan.FromSeconds(1));

            Assert.Contains("2,592,000", message);
            Assert.Contains("2592", message);
            Assert.Null(TrakHoundMcpTimeSeriesTools.GetRangeError(T0, T0.AddDays(30), TimeSpan.FromSeconds(2592)));
        }

        [Fact]
        public void A_Range_At_The_Limit_Is_Allowed_And_A_Backward_One_Is_Not()
        {
            Assert.Null(TrakHoundMcpTimeSeriesTools.GetRangeError(T0, T0.AddSeconds(TrakHoundMcpTimeSeriesTools.MaxRows), TimeSpan.FromSeconds(1)));
            Assert.NotNull(TrakHoundMcpTimeSeriesTools.GetRangeError(T0, T0.AddSeconds(TrakHoundMcpTimeSeriesTools.MaxRows + 1), TimeSpan.FromSeconds(1)));
            Assert.NotNull(TrakHoundMcpTimeSeriesTools.GetRangeError(T0, T0, TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void Output_Says_When_Rows_And_Series_Were_Left_Out()
        {
            var results = Enumerable.Range(0, TrakHoundMcpTimeSeriesTools.MaxSeries + 1).Select(i => new TrakHoundTimeSeriesResult
            {
                Tags = new() { ["machine"] = $"M{i}" },
                Columns = new[] { "timestamp", "speed" },
                Rows = Enumerable.Range(0, TrakHoundMcpTimeSeriesTools.MaxRows + 1).Select(_ => new object[] { T0, 1.0 }).ToArray()
            }).ToArray();

            using var document = JsonDocument.Parse(JsonSerializer.Serialize(TrakHoundMcpTimeSeriesTools.GetOutput(results)));
            var root = document.RootElement;

            Assert.True(root.GetProperty("seriesTruncated").GetBoolean());
            Assert.Equal(TrakHoundMcpTimeSeriesTools.MaxSeries + 1, root.GetProperty("seriesCount").GetInt32());
            Assert.Equal(TrakHoundMcpTimeSeriesTools.MaxSeries, root.GetProperty("results").GetArrayLength());

            var first = root.GetProperty("results")[0];
            Assert.True(first.GetProperty("truncated").GetBoolean());
            Assert.Equal(TrakHoundMcpTimeSeriesTools.MaxRows + 1, first.GetProperty("rowCount").GetInt32());
            Assert.Equal(TrakHoundMcpTimeSeriesTools.MaxRows, first.GetProperty("rows").GetArrayLength());
        }

        [Fact]
        public void Field_Values_From_A_Client_Become_The_Right_Types()
        {
            using var document = JsonDocument.Parse("{\"whole\":8000,\"decimal\":8000.0,\"on\":true,\"text\":\"RUN\",\"object\":{\"a\":1}}");
            var fields = document.RootElement;

            Assert.Equal(8000L, TrakHoundTimeSeriesValidation.GetFieldValue(fields.GetProperty("whole"), out var wholeType));
            Assert.Equal(TrakHoundTimeSeriesFieldType.Long, wholeType);
            Assert.Equal(8000.0, TrakHoundTimeSeriesValidation.GetFieldValue(fields.GetProperty("decimal"), out var decimalType));
            Assert.Equal(TrakHoundTimeSeriesFieldType.Double, decimalType);
            Assert.Equal(true, TrakHoundTimeSeriesValidation.GetFieldValue(fields.GetProperty("on"), out _));
            Assert.Equal("RUN", TrakHoundTimeSeriesValidation.GetFieldValue(fields.GetProperty("text"), out _));
            Assert.Null(TrakHoundTimeSeriesValidation.GetFieldValue(fields.GetProperty("object"), out _));
        }
    }
}
