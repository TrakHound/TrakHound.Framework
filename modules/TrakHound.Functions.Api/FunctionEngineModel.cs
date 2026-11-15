// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Functions
{
    [Table(TableName)]
    [IgnoreDuplicateKey]
    public class FunctionEngineModel
    {
        public const string TableName = "function_engine";
        public const string SchemaName = "trakhound";


        [JsonPropertyName("id")]
        [Column]
        [PrimaryKey]
        public string Id { get; set; }

        [JsonPropertyName("packageId")]
        [Column]
        public string PackageId { get; set; }

        [JsonPropertyName("packageVersion")]
        [Column]
        public string PackageVersion { get; set; }

        [JsonPropertyName("sender")]
        [Column]
        public string Sender { get; set; }
    }
}
