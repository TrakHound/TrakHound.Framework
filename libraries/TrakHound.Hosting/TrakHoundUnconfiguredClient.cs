// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;
using TrakHound.Clients;

namespace TrakHound.Hosting
{
    /// <summary>
    /// The Client of a host without an Instance. Every call fails with a message that says why, instead of timing out on a connection
    /// </summary>
    internal class TrakHoundUnconfiguredClient : ITrakHoundClient
    {
        public const string Message = $"No Instance is configured, so the Client can't be used (set \"{TrakHoundHostOptions.SectionName}:InstanceUrl\")";


        public string RouterId => null;

        public ITrakHoundApiClient Api { get; } = Create<ITrakHoundApiClient>();

        public ITrakHoundAppsClient Apps { get; } = Create<ITrakHoundAppsClient>();

        public ITrakHoundFunctionsClient Functions { get; } = Create<ITrakHoundFunctionsClient>();

        public ITrakHoundWorkersClient Workers { get; } = Create<ITrakHoundWorkersClient>();

        public ITrakHoundBuffersClient Buffers { get; } = Create<ITrakHoundBuffersClient>();

        public ITrakHoundModulesClient Modules { get; } = Create<ITrakHoundModulesClient>();

        public ITrakHoundStreamsClient Streams { get; } = Create<ITrakHoundStreamsClient>();

        public ITrakHoundMcpClient Mcp { get; } = Create<ITrakHoundMcpClient>();

        public ITrakHoundHttpRequestsClient HttpRequests { get; } = Create<ITrakHoundHttpRequestsClient>();

        public ITrakHoundServicesClient Services { get; } = Create<ITrakHoundServicesClient>();

        public ITrakHoundVolumesClient Volumes { get; } = Create<ITrakHoundVolumesClient>();

        public ITrakHoundLogsClient Logs { get; } = Create<ITrakHoundLogsClient>();

        public ITrakHoundInstancesClient Instances { get; } = Create<ITrakHoundInstancesClient>();


        public string GetLink(string route) => null;


        private static T Create<T>() => (T)UnconfiguredProxy.Create(typeof(T));


        internal class UnconfiguredProxy : DispatchProxy
        {
            private static readonly MethodInfo _fromException = typeof(Task).GetMethods().First(o => o.Name == nameof(Task.FromException) && o.IsGenericMethodDefinition);


            public static object Create(Type interfaceType) => Create(interfaceType, typeof(UnconfiguredProxy));


            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                // Sub-clients (ex. Client.Api.Endpoints) are proxies too, so the error comes from the call that would reach the Instance
                if (targetMethod.IsSpecialName && targetMethod.Name.StartsWith("get_") && targetMethod.ReturnType.IsInterface) return Create(targetMethod.ReturnType);

                // Fail a Task the way a failed request would, so awaiting it (or not) behaves as it does with a real Client
                var exception = new InvalidOperationException(Message);
                var returnType = targetMethod.ReturnType;
                if (returnType == typeof(Task)) return Task.FromException(exception);
                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>)) return _fromException.MakeGenericMethod(returnType.GetGenericArguments()[0]).Invoke(null, new object[] { exception });

                throw exception;
            }
        }
    }
}
