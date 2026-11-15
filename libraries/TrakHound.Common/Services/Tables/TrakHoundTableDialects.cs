// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// The names of the SQL Dialects used by first-party Table Services. Dialect names are strings so a Service can add its own
    /// </summary>
    public static class TrakHoundTableDialects
    {
        /// <summary>
        /// The portable subset of SQL documented in the Developer Guide. A Service that supports it has passed the shared conformance tests
        /// </summary>
        public const string Ansi = "ansi";

        /// <summary>
        /// Microsoft SQL Server (T-SQL)
        /// </summary>
        public const string SqlServer = "sqlserver";

        /// <summary>
        /// PostgreSQL
        /// </summary>
        public const string Postgres = "postgres";

        /// <summary>
        /// SQLite
        /// </summary>
        public const string Sqlite = "sqlite";
    }
}
