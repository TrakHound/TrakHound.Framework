// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// Routes Table Requests to the Service and Router Targets configured for the "Tables" Route of a Service Router
    /// </summary>
    public class TrakHoundTableRouter
    {
        private readonly TrakHoundServiceRouter _router;


        /// <summary>
        /// Creates a Table Router for the specified Service Router
        /// </summary>
        public TrakHoundTableRouter(TrakHoundServiceRouter router)
        {
            _router = router;
        }


        /// <summary>
        /// Checks the availability of the Table Services matched by the Router
        /// </summary>
        public Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run("CheckAvailability", requestId, cancellationToken,
                service => service.CheckAvailability(cancellationToken),
                (router, id) => router.Tables.CheckAvailability(id, cancellationToken)
                );
        }

        /// <summary>
        /// Lists the Tables and Views in the database
        /// </summary>
        public Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run("List", requestId, cancellationToken,
                service => service.List(cancellationToken),
                (router, id) => router.Tables.List(id, cancellationToken)
                );
        }

        /// <summary>
        /// Gets the Columns of the specified Table
        /// </summary>
        public Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run("GetColumns", requestId, cancellationToken,
                service => service.GetColumns(tableName, cancellationToken),
                (router, id) => router.Tables.GetColumns(tableName, id, cancellationToken)
                );
        }

        /// <summary>
        /// Gets the SQL Dialects of the first Table Service that answers (a Router with Targets on different databases has no single answer)
        /// </summary>
        public Task<TrakHoundServiceResponse<string[]>> GetDialects(string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run("GetDialects", requestId, cancellationToken,
                service => service.GetDialects(cancellationToken),
                (router, id) => router.Tables.GetDialects(id, cancellationToken)
                );
        }

        /// <summary>
        /// Runs the steps of a Request. A Request that only reads needs the same Permission as ExecuteQuery, and any other needs the same Permission as ExecuteNonQuery
        /// </summary>
        public Task<TrakHoundServiceResponse<TrakHoundTableStepResult[]>> Execute(TrakHoundTableRequest request, string requestId = null, CancellationToken cancellationToken = default)
        {
            var isRead = request != null && request.ReadOnly && request.Steps != null && request.Steps.All(o => o?.Type == TrakHoundTableStepType.Query);

            return Run(isRead ? "ExecuteQuery" : "ExecuteNonQuery", requestId, cancellationToken,
                service => service.Execute(request, cancellationToken),
                (router, id) => router.Tables.Execute(request, id, cancellationToken)
                );
        }

        /// <summary>
        /// Runs a Query that does not return rows. The timeout is in seconds (null uses the Service's default)
        /// </summary>
        public Task<TrakHoundServiceResponse> ExecuteNonQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string requestId = null, CancellationToken cancellationToken = default)
        {
            var request = new RouteRequest("ExecuteNonQuery", requestId) { CancellationToken = cancellationToken };

            return RouteTarget.Run<ITrakHoundTableService>(
                _router,
                request,
                _router.GetTargets(TrakHoundTableRoutes.Tables),
                serviceRequest => serviceRequest.Service.ExecuteNonQuery(query, timeout, parameters, cancellationToken),
                routerRequest => routerRequest.Router.Tables.ExecuteNonQuery(query, timeout, parameters, routerRequest.Request.Id, cancellationToken)
                );
        }

        /// <summary>
        /// Runs a Query and returns a Result for each result set. The timeout is in seconds (null uses the Service's default)
        /// </summary>
        public Task<TrakHoundServiceResponse<TrakHoundTableResult[]>> ExecuteQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run("ExecuteQuery", requestId, cancellationToken,
                service => service.ExecuteQuery(query, timeout, parameters, cancellationToken),
                (router, id) => router.Tables.ExecuteQuery(query, timeout, parameters, id, cancellationToken)
                );
        }


        // Runs the Request against the Targets of the Tables Route: serviceCommand for Service Targets,
        // routerCommand (with the Request ID) for Router Targets
        private Task<TrakHoundServiceResponse<TResult>> Run<TResult>(
            string name,
            string requestId,
            CancellationToken cancellationToken,
            Func<ITrakHoundTableService, Task<TrakHoundServiceResponse<TResult>>> serviceCommand,
            Func<TrakHoundServiceRouter, string, Task<TrakHoundServiceResponse<TResult>>> routerCommand
            )
        {
            var request = new RouteRequest(name, requestId) { CancellationToken = cancellationToken };

            return RouteTarget.Run<ITrakHoundTableService, TResult>(
                _router,
                request,
                _router.GetTargets(TrakHoundTableRoutes.Tables),
                serviceRequest => serviceCommand(serviceRequest.Service),
                routerRequest => routerCommand(routerRequest.Router, routerRequest.Request.Id)
                );
        }
    }
}
