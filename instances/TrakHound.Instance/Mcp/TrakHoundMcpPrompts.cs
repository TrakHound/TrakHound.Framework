// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol.Server;
using System.ComponentModel;

namespace TrakHound.Instance.Mcp
{
    /// <summary>
    /// The MCP Server's Prompts : starting points for common tasks that tell the AI client which Tools and Resources to use.
    /// Tools the caller can't use are left out of their Tool list, so the Prompts say to skip what isn't available
    /// </summary>
    [McpServerPromptType]
    public class TrakHoundMcpPrompts
    {
        [McpServerPrompt(Name = "instance_overview", Title = "Instance overview")]
        [Description("Summarizes what this TrakHound Instance runs and whether anything needs attention.")]
        public static string InstanceOverview()
        {
            return """
                Give me an overview of this TrakHound Instance.

                1. Read the trakhound://llms.txt resource for what the Instance is and the Apis it serves.
                2. List the Functions (functions_list) with their run counts and average durations.
                3. Look for problems in the last hour: logs_query with minimumLevel Warning and newestFirst, and group the results by logger.
                4. List the Key Value collections (keyvalues_collections), Tables (tables_list) and Time Series Measurements (timeseries_measurements) if those tools are available.

                Skip any step whose tool isn't available. Finish with a short summary: what the Instance does, its Apis and Functions, and anything that looks wrong (errors, failing Functions), with the evidence.
                """;
        }

        [McpServerPrompt(Name = "diagnose_function", Title = "Diagnose a Function")]
        [Description("Finds out why a Function is failing or slow, from its details, runs and logs.")]
        public static string DiagnoseFunction(
            [Description("The Function ID (from functions_list)")] string functionId,
            [Description("A Run ID to look at (optional)")] string runId = null)
        {
            var run = !string.IsNullOrWhiteSpace(runId) ? $" Focus on the Run {runId}." : "";
            return $"""
                Find out why the TrakHound Function "{functionId}" is failing or not behaving as expected.{run}

                1. Read what the Function does and its input parameters with functions_get (its ReadMe).
                2. Find its loggers with logs_loggers (pattern *{functionId}*), then read their recent logs with logs_query (minimumLevel Debug, newestFirst, the last day). Look for errors, warnings and the last successful Run.
                3. If the Functions Api tools are available (their names start with the Functions Api's route, ex. api_functions_), use them for the Function's Runs and Run logs.
                4. Don't run the Function (functions_run) unless I ask: it can change data or control equipment.

                Explain the most likely cause with the log lines that show it, and what to change (parameters, a Job, or the configuration).
                """;
        }

        [McpServerPrompt(Name = "explore_api", Title = "Explore an Api")]
        [Description("Explains what an Api on this Instance does and how to call it.")]
        public static string ExploreApi(
            [Description("The Api ID or route (from apis_list); empty to choose from all Apis")] string api = null)
        {
            var target = !string.IsNullOrWhiteSpace(api) ? $"the Api \"{api}\"" : "the Apis";
            return $"""
                Explain {target} on this TrakHound Instance and how to call it.

                1. Read the trakhound://llms.txt resource for the Apis and their Endpoints.
                2. Use apis_list and apis_get for the details, and read the Api's OpenAPI Document resource (trakhound://apis/<apiId>/openapi) for the parameters and request bodies.
                3. If the Api offers MCP tools (names starting with its route), say which tasks they cover.

                Describe what the Api is for, its main Endpoints, and give example requests (curl, with the X-API-Key header when the Instance requires a key). Don't call Endpoints that change data.
                """;
        }

        [McpServerPrompt(Name = "query_tables", Title = "Answer a question from the Tables")]
        [Description("Answers a question by finding the right Tables and reading them with SELECT queries.")]
        public static string QueryTables(
            [Description("The question to answer, ex. how many orders shipped last week")] string question)
        {
            return $"""
                Answer this question from the TrakHound Instance's Tables: {question}

                1. Find the relevant tables with tables_list, and their columns with tables_columns.
                2. Use tables_dialects to find which SQL to write, then tables_query with SELECT statements only, with a maxRows that fits the question. Never run statements that change data (INSERT, UPDATE, DELETE, DROP, ...).
                3. If tables_query isn't available (it requires write access to the MCP Server), say so and stop.

                Show the query you ran and answer the question from its results.
                """;
        }
    }
}
