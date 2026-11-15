// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using Microsoft.Data.SqlClient;
using TrakHound.Services;

namespace TrakHound.SqlServer.Service
{
    /// <summary>
    /// Creates the SQL Server connection string from a Service Configuration
    /// (server, port, database, user, password, connectionTimeout in milliseconds, trustServerCertificate)
    /// </summary>
    public static class SqlServerConnectionString
    {
        private const int _defaultConnectionTimeout = 10; // Seconds
        private const int _maxPoolSize = 200;


        /// <summary>
        /// Creates the connection string (null if configuration is null). Integrated Security is used when no user is set
        /// </summary>
        public static string Create(ITrakHoundServiceConfiguration configuration, string applicationName = null)
        {
            if (configuration == null) return null;

            // The builder escapes values, so a password with ';' or '=' can't break the string or add settings
            var builder = new SqlConnectionStringBuilder();

            var server = configuration.GetParameter("server");
            var port = configuration.GetParameter<int>("port");
            builder.DataSource = port > 0 ? $"{server},{port}" : server ?? "";

            var database = configuration.GetParameter("database");
            if (!string.IsNullOrEmpty(database)) builder.InitialCatalog = database;

            var user = configuration.GetParameter("user");
            if (!string.IsNullOrEmpty(user))
            {
                builder.UserID = user;
                builder.Password = configuration.GetParameter("password") ?? "";
            }
            else
            {
                builder.IntegratedSecurity = true;
            }

            builder.ConnectTimeout = _defaultConnectionTimeout;
            if (configuration.ParameterExists("connectionTimeout"))
            {
                // Milliseconds, rounded up to whole seconds (0 would mean wait forever)
                var milliseconds = configuration.GetParameter<int>("connectionTimeout");
                builder.ConnectTimeout = Math.Max(1, (int)Math.Ceiling(milliseconds / 1000.0));
            }

            builder.TrustServerCertificate = configuration.ParameterExists("trustServerCertificate") ? configuration.GetParameter<bool>("trustServerCertificate") : true;
            builder.Pooling = true;
            builder.MaxPoolSize = _maxPoolSize;
            if (!string.IsNullOrEmpty(applicationName)) builder.ApplicationName = applicationName;

            return builder.ConnectionString;
        }
    }
}
