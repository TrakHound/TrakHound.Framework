// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Data.SqlClient;
using System.Data;
using TrakHound.Services;
using TrakHound.Services.Tables;
using TrakHound.SqlServer.Service;

namespace TrakHound.SqlServer.Service.Tests
{
    /// <summary>
    /// The parts of the SQL Server Table Service that don't need a database: error Status Codes, the connection string,
    /// data types and parameters
    /// </summary>
    public class SqlServerTableServiceTests
    {
        private static TrakHoundServiceConfiguration Configuration(params (string Name, object Value)[] parameters)
        {
            var configuration = new TrakHoundServiceConfiguration { Id = "sql", Name = "sql" };
            foreach (var parameter in parameters) configuration.SetParameter(parameter.Name, parameter.Value);
            return configuration;
        }


        [Theory]
        [InlineData(102, 400)]   // Syntax error
        [InlineData(208, 400)]   // Invalid object name
        [InlineData(207, 400)]   // Invalid column name
        [InlineData(2627, 409)]  // Duplicate key
        [InlineData(547, 409)]   // Foreign key / check constraint
        [InlineData(-2, 504)]    // Timeout
        [InlineData(53, 503)]    // Server not found
        [InlineData(18456, 503)] // Login failed
        [InlineData(1205, 503)]  // Deadlock victim
        [InlineData(50000, 500)] // RAISERROR
        public void Errors_MapToStatusCodes(int number, int statusCode)
        {
            Assert.Equal(statusCode, SqlServerErrors.GetStatusCode(number));
        }

        [Theory]
        [InlineData(102)]
        [InlineData(2627)]
        public void QueryErrors_DoNotFallBack(int number)
        {
            var resultType = TrakHoundServiceFallback.GetResultType(SqlServerErrors.GetStatusCode(number));

            Assert.True(resultType == null || !TrakHoundServiceFallback.Default.Contains(resultType.Value));
        }

        [Fact]
        public void ConnectionString_EscapesValues()
        {
            var connectionString = SqlServerConnectionString.Create(Configuration(
                ("server", "db.local"),
                ("database", "trakhound"),
                ("user", "admin"),
                ("password", "p;Encrypt=False;x='\"")
                ));

            var builder = new SqlConnectionStringBuilder(connectionString);

            Assert.Equal("p;Encrypt=False;x='\"", builder.Password);
            Assert.Equal("db.local", builder.DataSource);
            Assert.Equal("trakhound", builder.InitialCatalog);
            Assert.Equal("admin", builder.UserID);
            Assert.False(builder.IntegratedSecurity);
        }

        [Fact]
        public void ConnectionString_UsesPort()
        {
            var builder = new SqlConnectionStringBuilder(SqlServerConnectionString.Create(Configuration(("server", "db.local"), ("port", 1500), ("user", "a"))));

            Assert.Equal("db.local,1500", builder.DataSource);
        }

        [Theory]
        [InlineData(5, 1)]
        [InlineData(15000, 15)]
        [InlineData(1500, 2)]
        public void ConnectionString_TimeoutIsMillisecondsRoundedUp(int milliseconds, int seconds)
        {
            var builder = new SqlConnectionStringBuilder(SqlServerConnectionString.Create(Configuration(("server", "s"), ("user", "a"), ("connectionTimeout", milliseconds))));

            Assert.Equal(seconds, builder.ConnectTimeout);
        }

        [Fact]
        public void ConnectionString_Defaults()
        {
            var builder = new SqlConnectionStringBuilder(SqlServerConnectionString.Create(Configuration(("server", "s")), "TrakHound-Instance:test"));

            Assert.Equal(10, builder.ConnectTimeout);
            Assert.True(builder.TrustServerCertificate);
            Assert.True(builder.IntegratedSecurity); // No user
            Assert.Equal("TrakHound-Instance:test", builder.ApplicationName);
        }

        [Fact]
        public void ConnectionString_TrustServerCertificateCanBeTurnedOff()
        {
            var builder = new SqlConnectionStringBuilder(SqlServerConnectionString.Create(Configuration(("server", "s"), ("trustServerCertificate", false))));

            Assert.False(builder.TrustServerCertificate);
        }

        [Theory]
        [InlineData("bit", TrakHoundTableDataType.Boolean)]
        [InlineData("decimal", TrakHoundTableDataType.Decimal)]
        [InlineData("money", TrakHoundTableDataType.Decimal)]
        [InlineData("uniqueidentifier", TrakHoundTableDataType.Guid)]
        [InlineData("varbinary", TrakHoundTableDataType.Binary)]
        [InlineData("datetimeoffset", TrakHoundTableDataType.Timestamp)]
        [InlineData("time", TrakHoundTableDataType.Duration)]
        [InlineData("bigint", TrakHoundTableDataType.Int64)]
        [InlineData("nvarchar", TrakHoundTableDataType.String)]
        public void DataTypes_FromTypeNames(string typeName, TrakHoundTableDataType dataType)
        {
            Assert.Equal(dataType, SqlServerDataTypes.GetDataType(typeName));
        }

        [Fact]
        public void DataTypes_FromClrTypes()
        {
            Assert.Equal(TrakHoundTableDataType.Boolean, SqlServerDataTypes.GetDataType(typeof(bool)));
            Assert.Equal(TrakHoundTableDataType.Decimal, SqlServerDataTypes.GetDataType(typeof(decimal)));
            Assert.Equal(TrakHoundTableDataType.Guid, SqlServerDataTypes.GetDataType(typeof(Guid)));
            Assert.Equal(TrakHoundTableDataType.Binary, SqlServerDataTypes.GetDataType(typeof(byte[])));
            Assert.Equal(TrakHoundTableDataType.Timestamp, SqlServerDataTypes.GetDataType(typeof(DateTimeOffset)));
        }

        [Fact]
        public void DataLength_IsInCharacters()
        {
            Assert.Equal(50, SqlServerDataTypes.GetDataLength("nvarchar", 100));
            Assert.Equal(255, SqlServerDataTypes.GetDataLength("varchar", 255));
            Assert.Null(SqlServerDataTypes.GetDataLength("nvarchar", -1)); // (max)
            Assert.Null(SqlServerDataTypes.GetDataLength("int", 4));
            Assert.True(SqlServerDataTypes.IsFixedLength("nchar"));
            Assert.False(SqlServerDataTypes.IsFixedLength("nvarchar"));
        }

        [Fact]
        public void Parameters_UnicodeStringsAreNVarChar()
        {
            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("unicode", "Größe 測定");
            parameters.Add("ascii", "plain");

            var sqlParameters = parameters.Select(SqlServerTableService.CreateParameter).ToList();

            Assert.Equal("@unicode", sqlParameters[0].ParameterName);
            Assert.Equal(SqlDbType.NVarChar, sqlParameters[0].SqlDbType);
            Assert.Equal(-1, sqlParameters[0].Size);
            Assert.Equal(SqlDbType.VarChar, sqlParameters[1].SqlDbType);
        }

        [Fact]
        public void Parameters_NullIsDBNull()
        {
            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add<string>("value", null);

            Assert.Equal(DBNull.Value, SqlServerTableService.CreateParameter(parameters.First()).Value);
        }
    }
}
