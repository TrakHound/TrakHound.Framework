// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrakHound.Services.Tables;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Functions
{
    /// <summary>
    /// A named set of Parameters for a Function (Scheduled Tasks run a Function with a Job's Parameters)
    /// </summary>
    public class FunctionJob
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("parameters")]
        public Dictionary<string, string> Parameters { get; set; }

        [JsonPropertyName("updated")]
        public DateTime Updated { get; set; }


        internal static FunctionJob FromModel(FunctionJobModel model)
        {
            if (model == null) return null;

            var job = new FunctionJob();
            job.Id = model.Id;
            job.FunctionId = model.FunctionId;
            job.Name = model.Name;
            job.Description = model.Description;
            job.Updated = DateTime.SpecifyKind(model.Updated, DateTimeKind.Utc);

            if (!string.IsNullOrEmpty(model.Parameters))
            {
                try { job.Parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(model.Parameters); }
                catch { }
            }

            return job;
        }

        /// <summary>
        /// The Parameters with trimmed Names, without empty Names
        /// </summary>
        internal static Dictionary<string, string> CleanParameters(Dictionary<string, string> parameters)
        {
            if (parameters == null) return null;

            var cleaned = new Dictionary<string, string>();
            foreach (var parameter in parameters)
            {
                if (!string.IsNullOrWhiteSpace(parameter.Key)) cleaned[parameter.Key.Trim()] = parameter.Value;
            }
            return cleaned;
        }

        internal FunctionJobModel ToModel(string instanceId)
        {
            var model = new FunctionJobModel();
            model.InstanceId = instanceId;
            model.FunctionId = FunctionId;
            model.Id = Id;
            model.Name = Name;
            model.Description = Description;
            model.Parameters = Parameters != null && Parameters.Count > 0 ? JsonSerializer.Serialize(Parameters) : null;
            model.Updated = Updated;
            return model;
        }
    }

    [Table(TableName, SchemaName)]
    [IgnoreDuplicateKey]
    public class FunctionJobModel
    {
        public const string TableName = "function_job";
        public const string SchemaName = "trakhound";


        [Column]
        [PrimaryKey]
        [NotNull]
        public string InstanceId { get; set; }

        [Column]
        [PrimaryKey]
        [NotNull]
        public string FunctionId { get; set; }

        [Column]
        [PrimaryKey]
        [NotNull]
        public string Id { get; set; }

        [Column(Length = 255, Unicode = true)]
        [NotNull]
        public string Name { get; set; }

        [Column(Length = 1000, Unicode = true)]
        public string Description { get; set; }

        /// <summary>
        /// The Parameters as a JSON object
        /// </summary>
        [Column(Length = TrakHoundTableColumnDefinition.MaxLength, Unicode = true)]
        public string Parameters { get; set; }

        [Column]
        [NotNull]
        public DateTime Updated { get; set; }
    }
}
