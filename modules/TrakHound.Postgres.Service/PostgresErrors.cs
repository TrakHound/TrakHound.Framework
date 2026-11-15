// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Postgres.Service
{
    /// <summary>
    /// Maps PostgreSQL error codes (SQLSTATE) to Service Status Codes. A Service Router only sends a Request on to the next Target
    /// for results it falls back on, so errors in the Query itself must not be Internal Errors, or the Query would be run again
    /// </summary>
    public static class PostgresErrors
    {
        /// <summary>
        /// Gets the Status Code for a SQLSTATE: 400 for errors in the Query, 409 for constraint violations,
        /// 503 when the server can't be reached (or the Query was rolled back as a deadlock victim or serialization failure),
        /// 504 for a timeout, and 500 for anything else
        /// </summary>
        public static int GetStatusCode(string sqlState)
        {
            if (string.IsNullOrEmpty(sqlState)) return 503; // The driver has no SQLSTATE when it can't reach the server

            switch (sqlState)
            {
                case "57014": return 504; // Query canceled (statement timeout)
                case "40001": // Serialization failure
                case "40P01": // Deadlock
                    return 503;
                case "23502": // Not null violation
                    return 400;
            }

            switch (sqlState.Substring(0, 2))
            {
                case "08": // Connection exceptions
                case "28": // Invalid authorization
                case "3D": // Invalid database
                case "53": // Insufficient resources
                case "57": // Operator intervention (ex. the server is shutting down)
                    return 503;

                case "23": // Integrity constraints
                    return 409;

                case "22": // Data exceptions (ex. conversion or truncation)
                case "42": // Syntax errors and unknown objects
                    return 400;

                default:
                    return 500;
            }
        }
    }
}
