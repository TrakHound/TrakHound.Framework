// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// Reads the rows of a Table Request step with each value as a JsonElement, so a Table Service converts it using its Column's
    /// Data Type (reading a number first as a double would lose the precision of a decimal)
    /// </summary>
    public class TrakHoundTableRowsJsonConverter : JsonConverter<object[][]>
    {
        /// <inheritdoc/>
        public override object[][] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var rows = JsonSerializer.Deserialize<JsonElement[][]>(ref reader);
            return rows?.Select(row => row?.Select(value => (object)value).ToArray()).ToArray();
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, object[][] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var row in value)
            {
                if (row == null)
                {
                    writer.WriteNullValue();
                    continue;
                }

                writer.WriteStartArray();
                foreach (var cell in row)
                {
                    JsonSerializer.Serialize(writer, cell, cell?.GetType() ?? typeof(object));
                }
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
    }
}
