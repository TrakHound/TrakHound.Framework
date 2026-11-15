// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Sqlite.Service
{
    /// <summary>
    /// Maps SQLite result codes to Service Status Codes. A Service Router only sends a Request on to the next Target
    /// for results it falls back on, so errors in the Query itself must not be Internal Errors, or the Query would be run again
    /// </summary>
    public static class SqliteErrors
    {
        /// <summary>
        /// Gets the Status Code for a SQLite primary result code: 400 for errors in the Query, 409 for constraint violations,
        /// 503 when the database is busy, locked or can't be opened, and 500 for anything else
        /// </summary>
        public static int GetStatusCode(int resultCode)
        {
            switch (resultCode)
            {
                case 5: // SQLITE_BUSY
                case 6: // SQLITE_LOCKED
                case 14: // SQLITE_CANTOPEN
                    return 503;

                case 19: // SQLITE_CONSTRAINT
                    return 409;

                case 1: // SQLITE_ERROR (syntax errors and unknown objects)
                case 18: // SQLITE_TOOBIG
                case 20: // SQLITE_MISMATCH
                case 25: // SQLITE_RANGE
                    return 400;

                default:
                    return 500;
            }
        }
    }
}
