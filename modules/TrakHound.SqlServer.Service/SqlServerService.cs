// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Services;
using TrakHound.Services.Tables;

namespace TrakHound.SqlServer.Service
{
    public abstract class SqlServerService : TrakHoundSqlTableService, IDisposable
    {
        public SqlServerService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }
    }
}
