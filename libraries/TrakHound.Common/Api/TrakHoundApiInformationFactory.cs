// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TrakHound.Packages;
using TrakHound.Security;

namespace TrakHound.Api
{
    /// <summary>
    /// Creates the Information for a loaded Api (and its Controllers and Endpoints) by reading its Package and the Attributes on its Controller Types.
    /// Kept apart from TrakHoundApiInformation so the Information classes only describe the data that the System API returns
    /// </summary>
    public static class TrakHoundApiInformationFactory
    {
        /// <summary>
        /// Creates the Information for an Api from its Controllers, Configuration, and Package. Returns null if any of these are missing
        /// </summary>
        public static TrakHoundApiInformation Create(
            IEnumerable<ITrakHoundApiController> controllers,
            ITrakHoundApiConfiguration configuration,
            TrakHoundPackage package,
            string volumeId,
            string packageReadme = null
            )
        {
            if (!controllers.IsNullOrEmpty() && configuration != null && package != null)
            {
                var route = !string.IsNullOrEmpty(configuration.Route) ? configuration.Route : "/";
                if (!route.StartsWith('/')) route = '/' + route;

                var information = new TrakHoundApiInformation();
                information.Id = configuration.Id;
                information.Route = route;
                information.PackageId = configuration.PackageId;
                information.VolumeId = volumeId;
                information.GroupId = configuration.GroupId;
                information.HttpEnabled = configuration.HttpEnabled;
                information.McpTools = configuration.McpTools;
                information.McpReadOnly = configuration.McpReadOnly;

                if (package != null)
                {
                    information.PackageVersion = package.Version;
                    information.PackageBuildDate = package.BuildDate;
                    information.PackageHash = package.Hash;
                    information.PackageIcon = package.GetMetadata(TrakHoundPackage.ImageName);

                    information.TrakHoundVersion = package.GetMetadata(TrakHoundPackage.TrakHoundVersionName);

                    information.Repository = package.GetMetadata(TrakHoundPackage.RepositoryName);
                    information.RepositoryBranch = package.GetMetadata(TrakHoundPackage.RepositoryBranchName);
                    information.RepositoryDirectory = package.GetMetadata(TrakHoundPackage.RepositoryDirectoryName);
                    information.RepositoryCommit = package.GetMetadata(TrakHoundPackage.RepositoryCommitName);
                }

                information.PackageReadMe = packageReadme;

                var controllerInformations = new List<TrakHoundApiControllerInformation>();
                foreach (var controller in controllers)
                {
                    controllerInformations.Add(CreateControllerInformation(configuration, controller.GetType()));
                }
                information.Controllers = controllerInformations;

                return information;
            }

            return null;
        }

        /// <summary>
        /// Creates the Information for a Controller Type using its TrakHoundApiControllerAttribute and Endpoint Methods. Returns null if either argument is null
        /// </summary>
        public static TrakHoundApiControllerInformation CreateControllerInformation(ITrakHoundApiConfiguration configuration, Type controllerType)
        {
            if (configuration != null && controllerType != null)
            {
                string route = Url.PathSeparator.ToString();
                string name = controllerType.Name;
                string description = null;

                var controllerAttribute = controllerType.GetCustomAttribute<TrakHoundApiControllerAttribute>();
                if (controllerAttribute != null)
                {
                    if (!string.IsNullOrEmpty(controllerAttribute.Route)) route = controllerAttribute.Route;
                    if (!string.IsNullOrEmpty(controllerAttribute.Name)) name = controllerAttribute.Name;
                    if (!string.IsNullOrEmpty(controllerAttribute.Description)) description = controllerAttribute.Description;
                }

                route = !string.IsNullOrEmpty(route) ? route : "/";
                if (!route.StartsWith('/')) route = '/' + route;

                var information = new TrakHoundApiControllerInformation();
                information.Id = TrakHoundApiInformation.GetControllerId(configuration.Id, controllerType);
                information.Route = route;
                information.Name = name;
                information.Description = description;
                information.EndPoints = GetEndpointInformation(configuration, controllerType, configuration.Route);

                return information;
            }

            return null;
        }

        /// <summary>
        /// Gets the Information for each Endpoint (Method marked with an Endpoint Attribute) in the Controller Type. The route argument is currently not used
        /// </summary>
        public static IEnumerable<TrakHoundApiEndpointInformation> GetEndpointInformation(ITrakHoundApiConfiguration configuration, Type controllerType, string route = null)
        {
            var endpoints = new List<TrakHoundApiEndpointInformation>();

            try
            {
                var methods = controllerType.GetMethods();
                if (!methods.IsNullOrEmpty())
                {
                    foreach (var method in methods)
                    {
                        // Only Methods the Instance routes Requests to
                        if (!method.IsConstructor && TrakHoundApi.IsEndpointMethod(method))
                        {
                            // Process Route Attribute
                            var routeAttributes = GetRouteAttributes(method);
                            if (!routeAttributes.IsNullOrEmpty())
                            {
                                foreach (var routeAttribute in routeAttributes)
                                {
                                    var endPointRoute = !string.IsNullOrEmpty(routeAttribute.Route) ? routeAttribute.Route : "/";
                                    if (!endPointRoute.StartsWith('/')) endPointRoute = '/' + endPointRoute;

                                    var endpoint = new TrakHoundApiEndpointInformation();
                                    endpoint.Id = GetEndpointId(routeAttribute);
                                    endpoint.Type = routeAttribute.Type;
                                    endpoint.Method = routeAttribute.Method;
                                    endpoint.Route = endPointRoute;
                                    //endpoint.Route = !string.IsNullOrEmpty(routeAttribute.Route) ? routeAttribute.Route : Url.PathSeparator.ToString();
                                    endpoint.Description = routeAttribute.Description;

                                    // Set Group
                                    var groupAttribute = method.GetCustomAttribute<TrakHoundApiGroupAttribute>();
                                    if (groupAttribute != null && !string.IsNullOrEmpty(groupAttribute.Name))
                                    {
                                        endpoint.Group = groupAttribute.Name;
                                    }

                                    // Set Parameters
                                    var methodParameters = method.GetParameters();
                                    if (!methodParameters.IsNullOrEmpty())
                                    {
                                        var endpointParameters = new List<TrakHoundApiParameterInformation>();

                                        foreach (var methodParameter in methodParameters)
                                        {
                                            var endpointParameterType = GetParameterType(methodParameter);

                                            // Query Parameters are bound by the FromQuery Name (when set)
                                            var parameterName = methodParameter.Name;
                                            var queryAttribute = methodParameter.GetCustomAttribute<FromQueryAttribute>();
                                            if (queryAttribute != null && !string.IsNullOrEmpty(queryAttribute.ParameterName)) parameterName = queryAttribute.ParameterName;

                                            // Reserved Client Parameters are set by the Instance and can't be supplied by the caller
                                            if (queryAttribute != null && TrakHoundApi.IsReservedParameter(parameterName)) continue;

                                            // Header Parameters are bound by the FromHeader Name (when set)
                                            var headerAttribute = methodParameter.GetCustomAttribute<FromHeaderAttribute>();
                                            if (headerAttribute != null) parameterName = FromHeaderAttribute.GetHeaderName(headerAttribute, parameterName);

                                            if (endpointParameterType >= 0)
                                            {
                                                var endpointParameter = new TrakHoundApiParameterInformation();
                                                endpointParameter.Name = parameterName;
                                                endpointParameter.Type = ((TrakHoundApiParameterType)endpointParameterType).ToString();
                                                endpointParameter.DataType = GetParameterDataType(methodParameter);
                                                // Parameters with a Default Value (including a default of null) are optional
                                                endpointParameter.Required = !methodParameter.HasDefaultValue;
                                                endpointParameter.DefaultValue = methodParameter.HasDefaultValue ? methodParameter.DefaultValue?.ToString() : null;

                                                if (endpointParameterType == (int)TrakHoundApiParameterType.Body)
                                                {
                                                    var bodyAttribute = methodParameter.GetCustomAttribute<FromBodyAttribute>();
                                                    endpointParameter.ContentType = !string.IsNullOrEmpty(bodyAttribute.ContentType) ? bodyAttribute.ContentType : FromBodyAttribute.GetDefaultContentType(methodParameter.ParameterType);
                                                }

                                                endpointParameter.Description = TrakHoundApi.GetParameterDescription(methodParameter);

                                                endpointParameters.Add(endpointParameter);
                                            }
                                        }

                                        endpoint.Parameters = endpointParameters;
                                    }


                                    // Add Responses
                                    var responseAttributes = method.GetCustomAttributes<TrakHoundApiResponseAttribute>();
                                    if (!responseAttributes.IsNullOrEmpty())
                                    {
                                        var responses = new List<TrakHoundApiResponseInformation>();

                                        foreach (var responseAttribute in responseAttributes)
                                        {
                                            var response = TrakHoundApiResponseInformation.Create(responseAttribute.StatusCode, responseAttribute.ReturnType);
                                            if (response != null) responses.Add(response);
                                        }

                                        endpoint.Responses = responses;
                                    }

                                    // Add Permissions
                                    var permissionAttributes = method.GetCustomAttributes<TrakHoundPermissionAttribute>();
                                    if (!permissionAttributes.IsNullOrEmpty())
                                    {
                                        var endpointPermissions = new List<string>();
                                        foreach (var permissionAttribute in permissionAttributes)
                                        {
                                            if (!string.IsNullOrEmpty(permissionAttribute.Permission))
                                            {
                                                endpointPermissions.Add(permissionAttribute.Permission);
                                            }
                                        }
                                        endpoint.Permissions = endpointPermissions;
                                    }

                                    endpoints.Add(endpoint);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return endpoints;
        }


        /// <summary>
        /// The Identifier of an Endpoint (an MD5 hash of the Method and Route of its Endpoint Attribute)
        /// </summary>
        public static string GetEndpointId(TrakHoundApiEndpointRouteAttribute routeAttribute)
        {
            return routeAttribute != null ? TrakHoundApiInformation.GetEndpointId(routeAttribute.Method, routeAttribute.Route) : null;
        }

        private static TrakHoundApiEndpointRouteAttribute GetRouteAttribute(MethodInfo method)
        {
            if (method != null)
            {
                var endpointAttribute = method.GetCustomAttribute<TrakHoundApiEndpointRouteAttribute>();
                if (endpointAttribute != null) return endpointAttribute;
            }

            return null;
        }

        private static IEnumerable<TrakHoundApiEndpointRouteAttribute> GetRouteAttributes(MethodInfo method)
        {
            if (method != null)
            {
                var endpointAttributes = method.GetCustomAttributes<TrakHoundApiEndpointRouteAttribute>();
                if (!endpointAttributes.IsNullOrEmpty()) return endpointAttributes;
            }

            return null;
        }

        private static int GetParameterType(ParameterInfo methodParameter)
        {
            if (methodParameter.GetCustomAttribute<FromRouteAttribute>() != null)
            {
                return (int)TrakHoundApiParameterType.Route;
            }
            else if (methodParameter.GetCustomAttribute<FromQueryAttribute>() != null)
            {
                return (int)TrakHoundApiParameterType.Query;
            }
            else if (methodParameter.GetCustomAttribute<FromHeaderAttribute>() != null)
            {
                return (int)TrakHoundApiParameterType.Header;
            }
            else if (methodParameter.GetCustomAttribute<FromBodyAttribute>() != null)
            {
                return (int)TrakHoundApiParameterType.Body;
            }

            return -1;
        }

        private static string GetParameterDataType(ParameterInfo methodParameter)
        {
            var type = methodParameter.ParameterType;

            // Handle Nullable<T> Type
            if (type.IsValueType && Nullable.GetUnderlyingType(type) != null)
            {
                type = Nullable.GetUnderlyingType(type);
            }

            // Handle Dictionary Type (a JSON object, ex. Dictionary<string, string> => "Dictionary<String,String>"). Checked before
            // collections, which would otherwise describe it by its Key Type ("String[]")
            var dictionaryType = GetDictionaryType(type);
            if (dictionaryType != null)
            {
                var arguments = dictionaryType.GetGenericArguments();
                return $"Dictionary<{arguments[0].Name},{arguments[1].Name}>";
            }

            // Handle Array Type (ex. byte[] => "Byte[]", List<string> => "String[]")
            if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
            {
                type = type.IsArray ? type.GetElementType() : type.GetGenericArguments()?.FirstOrDefault();

                return type != null ? $"{type.Name}[]" : null;
            }

            return type?.Name;
        }

        private static Type GetDictionaryType(Type type)
        {
            if (type == null) return null;

            var types = type.IsInterface ? type.GetInterfaces().Prepend(type) : type.GetInterfaces();
            return types.FirstOrDefault(o => o.IsGenericType &&
                (o.GetGenericTypeDefinition() == typeof(IDictionary<,>) || o.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
        }
    }
}
