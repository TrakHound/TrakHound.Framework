// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.Tables;

namespace TrakHound.Http.Service
{
    public class HttpTableService : HttpService, ITrakHoundTableService
    {
        protected override ITrakHoundServiceClient ServiceClient => Client?.Services.Tables;


        public HttpTableService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }


        public async Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<TrakHoundTable[]>(NotConfiguredMessage);

            return Forward(await Client.Services.Tables.List(cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<TrakHoundTableColumn[]>(NotConfiguredMessage);

            return Forward(await Client.Services.Tables.GetColumns(tableName, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<string[]>> GetDialects(CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<string[]>(NotConfiguredMessage);

            return Forward(await Client.Services.Tables.GetDialects(cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTableStepResult[]>> Execute(TrakHoundTableRequest request, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<TrakHoundTableStepResult[]>(NotConfiguredMessage);

            return Forward(await Client.Services.Tables.Execute(request, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse> ExecuteNonQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable(NotConfiguredMessage);

            return Forward(await Client.Services.Tables.ExecuteNonQuery(query, timeout, parameters, cancellationToken: cancellationToken), cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<TrakHoundTableResult[]>> ExecuteQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, CancellationToken cancellationToken = default)
        {
            if (Client == null) return NotAvailable<TrakHoundTableResult[]>(NotConfiguredMessage);

            return Forward(await Client.Services.Tables.ExecuteQuery(query, timeout, parameters, cancellationToken: cancellationToken), cancellationToken);
        }
    }
}
