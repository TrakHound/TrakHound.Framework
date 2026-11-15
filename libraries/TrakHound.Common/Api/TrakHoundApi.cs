// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace TrakHound.Api
{
    /// <summary>
    /// Helper methods and constants used to Route and Bind Api Requests
    /// </summary>
    public static class TrakHoundApi
    {
        /// <summary>
        /// Reserved Query Parameter set by the Instance to the IP Address of the client that sent the HTTP Request.
        /// Read using [FromQuery(TrakHoundApi.ClientAddressParameter)]
        /// </summary>
        public const string ClientAddressParameter = "_client_address";

        /// <summary>
        /// Reserved Query Parameter set by the Instance to the User-Agent header of the HTTP Request.
        /// Read using [FromQuery(TrakHoundApi.ClientAgentParameter)]
        /// </summary>
        public const string ClientAgentParameter = "_client_agent";


        /// <summary>
        /// Endpoint Methods return a TrakHoundApiResponse (or a Stream of them), optionally as a Task
        /// </summary>
        public static bool IsEndpointMethod(MethodInfo methodInfo)
        {
            if (methodInfo != null)
            {
                return methodInfo.ReturnType == typeof(TrakHoundApiResponse) ||
                    methodInfo.ReturnType == typeof(Task<TrakHoundApiResponse>) ||
                    methodInfo.ReturnType == typeof(ITrakHoundStream<TrakHoundApiResponse>) ||
                    methodInfo.ReturnType == typeof(Task<ITrakHoundStream<TrakHoundApiResponse>>);
            }

            return false;
        }

        /// <summary>
        /// Returns true if the specified Query Parameter name is a Reserved Client Parameter (case-insensitive)
        /// </summary>
        /// <summary>
        /// The Description of an Endpoint Method Parameter, from its FromQuery, FromRoute, FromHeader, or FromBody attribute (null when it has none)
        /// </summary>
        public static string GetParameterDescription(ParameterInfo parameter)
        {
            if (parameter == null) return null;

            var description = parameter.GetCustomAttribute<FromQueryAttribute>()?.Description
                ?? parameter.GetCustomAttribute<FromRouteAttribute>()?.Description
                ?? parameter.GetCustomAttribute<FromHeaderAttribute>()?.Description
                ?? parameter.GetCustomAttribute<FromBodyAttribute>()?.Description;

            return !string.IsNullOrWhiteSpace(description) ? description : null;
        }

        public static bool IsReservedParameter(string name)
        {
            return string.Equals(name, ClientAddressParameter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, ClientAgentParameter, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Sets the Reserved Client Parameters, replacing any values supplied by the client so they can't be spoofed
        /// </summary>
        public static void SetClientParameters(Dictionary<string, string> parameters, string clientAddress, string clientAgent)
        {
            if (parameters != null)
            {
                foreach (var key in parameters.Keys.Where(IsReservedParameter).ToList())
                {
                    parameters.Remove(key);
                }

                if (!string.IsNullOrEmpty(clientAddress)) parameters[ClientAddressParameter] = clientAddress;
                if (!string.IsNullOrEmpty(clientAgent)) parameters[ClientAgentParameter] = clientAgent;
            }
        }


        /// <summary>
        /// Converts a TrakHoundApiMethod to an HttpMethod
        /// </summary>
        public static HttpMethod GetHttpMethod(TrakHoundApiMethod apiMethod)
        {
            switch (apiMethod)
            {
                case TrakHoundApiMethod.CONNECT: return HttpMethod.Connect;
                case TrakHoundApiMethod.DELETE: return HttpMethod.Delete;
                case TrakHoundApiMethod.GET: return HttpMethod.Get;
                case TrakHoundApiMethod.HEAD: return HttpMethod.Head;
                case TrakHoundApiMethod.OPTIONS: return HttpMethod.Options;
                case TrakHoundApiMethod.PATCH: return HttpMethod.Patch;
                case TrakHoundApiMethod.POST: return HttpMethod.Post;
                case TrakHoundApiMethod.PUT: return HttpMethod.Put;
                case TrakHoundApiMethod.TRACE: return HttpMethod.Trace;
            }

            return null;
        }

        /// <summary>
        /// Converts an HTTP Method name (case-insensitive) to a TrakHoundApiMethod. Returns GET if not recognized
        /// </summary>
        public static TrakHoundApiMethod GetApiMethod(string httpMethod)
        {
            if (!string.IsNullOrEmpty(httpMethod))
            {
                switch (httpMethod.ToUpper())
                {
                    case "CONNECT": return TrakHoundApiMethod.CONNECT;
                    case "DELETE": return TrakHoundApiMethod.DELETE;
                    case "GET": return TrakHoundApiMethod.GET;
                    case "HEAD": return TrakHoundApiMethod.HEAD;
                    case "OPTIONS": return TrakHoundApiMethod.OPTIONS;
                    case "PATCH": return TrakHoundApiMethod.PATCH;
                    case "POST": return TrakHoundApiMethod.POST;
                    case "PUT": return TrakHoundApiMethod.PUT;
                    case "TRACE": return TrakHoundApiMethod.TRACE;
                }
            }

            return TrakHoundApiMethod.GET;
        }
    }
}
