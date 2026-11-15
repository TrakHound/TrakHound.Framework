// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Services.TimeSeries
{
    /// <summary>
    /// Routes Time Series Requests to the Time Series Services and Routers configured as Targets for the Router
    /// </summary>
    public class TrakHoundTimeSeriesRouter
    {
        private readonly TrakHoundServiceRouter _router;


        /// <summary>
        /// Creates a Time Series Router that uses the Targets of the specified Service Router
        /// </summary>
        public TrakHoundTimeSeriesRouter(TrakHoundServiceRouter router)
        {
            _router = router;
        }


        /// <summary>
        /// Checks the availability of the Time Series Targets
        /// </summary>
        public Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run<TrakHoundServiceAvailabilityResponse>(
                "CheckAvailability",
                requestId,
                service => service.CheckAvailability(cancellationToken),
                (router, id) => router.TimeSeries.CheckAvailability(id, cancellationToken),
                cancellationToken
                );
        }

        /// <summary>
        /// Lists the Measurements available through the Targets
        /// </summary>
        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListMeasurements(string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run<IEnumerable<string>>(
                "ListMeasurements",
                requestId,
                service => service.ListMeasurements(cancellationToken),
                (router, id) => router.TimeSeries.ListMeasurements(id, cancellationToken),
                cancellationToken
                );
        }

        /// <summary>
        /// Lists the Series of a Measurement that have all of the specified Tags, paged using skip and take
        /// </summary>
        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>> ListSeries(string measurement, IReadOnlyDictionary<string, string> tags = null, int skip = 0, int take = TrakHoundTimeSeriesQuery.DefaultTake, string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run<IEnumerable<TrakHoundTimeSeries>>(
                "ListSeries",
                requestId,
                service => service.ListSeries(measurement, tags, skip, take, cancellationToken),
                (router, id) => router.TimeSeries.ListSeries(measurement, tags, skip, take, id, cancellationToken),
                cancellationToken
                );
        }

        /// <summary>
        /// Lists the Fields of a Measurement and their types
        /// </summary>
        public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>> ListFields(string measurement, string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run<IEnumerable<TrakHoundTimeSeriesField>>(
                "ListFields",
                requestId,
                service => service.ListFields(measurement, cancellationToken),
                (router, id) => router.TimeSeries.ListFields(measurement, id, cancellationToken),
                cancellationToken
                );
        }

        /// <summary>
        /// Writes Points
        /// </summary>
        public Task<TrakHoundServiceResponse> Write(IEnumerable<TrakHoundTimeSeriesPoint> points, string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run(
                "Write",
                requestId,
                service => service.Write(points, cancellationToken),
                (router, id) => router.TimeSeries.Write(points, id, cancellationToken),
                cancellationToken
                );
        }

        /// <summary>
        /// Reads the Points between Start and Stop
        /// </summary>
        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Read(TrakHoundTimeSeriesQuery query, string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run<TrakHoundTimeSeriesResult[]>(
                "Read",
                requestId,
                service => service.Read(query, cancellationToken),
                (router, id) => router.TimeSeries.Read(query, id, cancellationToken),
                cancellationToken
                );
        }

        /// <summary>
        /// Reads the newest Value of each Field of each Series
        /// </summary>
        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>> ReadLatest(string measurement, IReadOnlyDictionary<string, string[]> tags = null, IEnumerable<string> fields = null, string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run<TrakHoundTimeSeriesLatestResult[]>(
                "ReadLatest",
                requestId,
                service => service.ReadLatest(measurement, tags, fields, cancellationToken),
                (router, id) => router.TimeSeries.ReadLatest(measurement, tags, fields, id, cancellationToken),
                cancellationToken
                );
        }

        /// <summary>
        /// Calculates the Functions for each Interval between Start and Stop
        /// </summary>
        public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Aggregate(TrakHoundTimeSeriesAggregateQuery query, string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run<TrakHoundTimeSeriesResult[]>(
                "Aggregate",
                requestId,
                service => service.Aggregate(query, cancellationToken),
                (router, id) => router.TimeSeries.Aggregate(query, id, cancellationToken),
                cancellationToken
                );
        }

        /// <summary>
        /// Deletes the Points between start and stop
        /// </summary>
        public Task<TrakHoundServiceResponse> Delete(string measurement, IReadOnlyDictionary<string, string> tags, DateTimeOffset start, DateTimeOffset stop, string requestId = null, CancellationToken cancellationToken = default)
        {
            return Run(
                "Delete",
                requestId,
                service => service.Delete(measurement, tags, start, stop, cancellationToken),
                (router, id) => router.TimeSeries.Delete(measurement, tags, start, stop, id, cancellationToken),
                cancellationToken
                );
        }


        private async Task<TrakHoundServiceResponse<TResult>> Run<TResult>(
            string operation,
            string requestId,
            Func<ITrakHoundTimeSeriesService, Task<TrakHoundServiceResponse<TResult>>> service,
            Func<TrakHoundServiceRouter, string, Task<TrakHoundServiceResponse<TResult>>> router,
            CancellationToken cancellationToken
            )
        {
            Func<RouteTargetDriverRequest<ITrakHoundTimeSeriesService, TResult>, Task<TrakHoundServiceResponse<TResult>>> serviceCommand = (serviceRequest) => service(serviceRequest.Service);
            Func<RouteTargetRouterRequest<TResult>, Task<TrakHoundServiceResponse<TResult>>> routerCommand = (routerRequest) => router(routerRequest.Router, routerRequest.Request.Id);

            var request = new RouteRequest(operation, requestId) { CancellationToken = cancellationToken };

            return await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundTimeSeriesRoutes.TimeSeries),
                serviceCommand,
                routerCommand
                );
        }

        private async Task<TrakHoundServiceResponse> Run(
            string operation,
            string requestId,
            Func<ITrakHoundTimeSeriesService, Task<TrakHoundServiceResponse>> service,
            Func<TrakHoundServiceRouter, string, Task<TrakHoundServiceResponse>> router,
            CancellationToken cancellationToken
            )
        {
            Func<RouteTargetServiceRequest<ITrakHoundTimeSeriesService>, Task<TrakHoundServiceResponse>> serviceCommand = (serviceRequest) => service(serviceRequest.Service);
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerCommand = (routerRequest) => router(routerRequest.Router, routerRequest.Request.Id);

            var request = new RouteRequest(operation, requestId) { CancellationToken = cancellationToken };

            return await RouteTarget.Run(
                _router,
                request,
                _router.GetTargets(TrakHoundTimeSeriesRoutes.TimeSeries),
                serviceCommand,
                routerCommand
                );
        }
    }
}
