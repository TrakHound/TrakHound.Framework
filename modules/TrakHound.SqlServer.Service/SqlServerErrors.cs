// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

namespace TrakHound.SqlServer.Service
{
    /// <summary>
    /// Maps SQL Server error numbers to Service Status Codes. A Service Router only sends a Request on to the next Target
    /// for results it falls back on (by default Not Found, Not Available, Timeout and Internal Error), so errors in the Query
    /// itself must not be Internal Errors, or the Query would be run again on the next Target
    /// </summary>
    public static class SqlServerErrors
    {
        /// <summary>
        /// Gets the Status Code for a SqlException Number: 400 for errors in the Query, 409 for constraint violations,
        /// 503 when the server can't be reached (or the Query was chosen as a deadlock victim and rolled back),
        /// 504 for a timeout, and 500 for anything else
        /// </summary>
        public static int GetStatusCode(int number)
        {
            switch (number)
            {
                // Timeout
                case -2:
                    return 504;

                // Connection, login and availability errors (and deadlock victims, which are rolled back)
                case -1:
                case 2:
                case 53:
                case 64:
                case 121:
                case 233:
                case 1205:
                case 4060:
                case 18456:
                case 10053:
                case 10054:
                case 10060:
                case 10061:
                case 11001:
                case 40197:
                case 40501:
                case 40613:
                    return 503;

                // Constraint violations (duplicate key, foreign key or check)
                case 547:
                case 2601:
                case 2627:
                    return 409;

                // Errors in the Query (syntax, unknown objects or columns, conversions, nulls and truncation)
                case 102:
                case 105:
                case 137:
                case 156:
                case 170:
                case 207:
                case 208:
                case 245:
                case 515:
                case 2628:
                case 2812:
                case 4104:
                case 8114:
                case 8134:
                case 8152:
                    return 400;

                default:
                    return 500;
            }
        }
    }
}
