// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Npgsql;
using TrakHound.Services;

namespace TrakHound.Postgres.Service
{
    /// <summary>
    /// Creates the PostgreSQL connection string from a Service Configuration
    /// (server, port, database, user, password, connectionTimeout in milliseconds)
    /// </summary>
    public static class PostgresConnectionString
    {
        private const int _defaultConnectionTimeout = 10; // Seconds
        private const int _maxPoolSize = 200;


        /// <summary>
        /// Creates the connection string (null if configuration is null)
        /// </summary>
        public static string Create(ITrakHoundServiceConfiguration configuration, string applicationName = null)
        {
            if (configuration == null) return null;

            // The builder escapes values, so a password with ';' or '=' can't break the string or add settings
            var builder = new NpgsqlConnectionStringBuilder();
            builder.Host = configuration.GetParameter("server");

            var port = configuration.GetParameter<int>("port");
            if (port > 0) builder.Port = port;

            var database = configuration.GetParameter("database");
            if (!string.IsNullOrEmpty(database)) builder.Database = database;

            var user = configuration.GetParameter("user");
            if (!string.IsNullOrEmpty(user))
            {
                builder.Username = user;
                builder.Password = configuration.GetParameter("password") ?? "";
            }

            builder.Timeout = _defaultConnectionTimeout;
            if (configuration.ParameterExists("connectionTimeout"))
            {
                // Milliseconds, rounded up to whole seconds (0 would mean wait forever)
                var milliseconds = configuration.GetParameter<int>("connectionTimeout");
                builder.Timeout = Math.Max(1, (int)Math.Ceiling(milliseconds / 1000.0));
            }

            builder.Pooling = true;
            builder.MaxPoolSize = _maxPoolSize;
            if (!string.IsNullOrEmpty(applicationName)) builder.ApplicationName = applicationName;

            return builder.ConnectionString;
        }
    }
}
