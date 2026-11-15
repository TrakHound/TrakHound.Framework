// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using TrakHound.Instance.Security;
using TrakHound.Instances;

namespace TrakHound.Instance.Mcp
{
    [McpServerToolType]
    public class TrakHoundMcpApiTools : TrakHoundMcpTools
    {
        public TrakHoundMcpApiTools(ITrakHoundInstance instance) : base(instance) { }


        [McpServerTool(Name = "apis_list", Title = "List Apis", ReadOnly = true)]
        [Description("Lists the Apis on the Instance (ID, route, package, and how many endpoints each has).")]
        [TrakHoundMcpTool(Resource = TrakHoundSystemSecurity.ApiInformation)]
        public async Task<string> List()
        {
            var apis = await GetClient().Api.GetRouteInformation();
            return ToJson(apis?.OrderBy(o => o.Route).Select(o => new
            {
                apiId = o.Id,
                route = o.Route,
                packageId = o.PackageId,
                packageVersion = o.PackageVersion,
                httpEnabled = o.HttpEnabled,
                endpointCount = o.Controllers?.Sum(c => c.EndPoints?.Count() ?? 0) ?? 0
            }));
        }

        [McpServerTool(Name = "apis_get", Title = "Get Api", ReadOnly = true)]
        [Description("Describes an Api's endpoints (method, route, description and parameters).")]
        [TrakHoundMcpTool(Resource = TrakHoundSystemSecurity.ApiInformation)]
        public async Task<string> Get([Description("The Api ID (from apis_list)")] string apiId)
        {
            var api = await GetClient().Api.GetInformation(apiId);
            if (api == null) throw new McpException($"Api '{apiId}' was not found");

            return ToJson(new
            {
                apiId = api.Id,
                route = api.Route,
                packageId = api.PackageId,
                packageVersion = api.PackageVersion,
                endpoints = api.Controllers?.SelectMany(c => c.EndPoints ?? Enumerable.Empty<TrakHound.Api.TrakHoundApiEndpointInformation>()).Select(e => new
                {
                    type = e.Type.ToString(),
                    method = e.Method?.ToString(),
                    route = "/" + Url.Combine(api.Route, e.Route).TrimStart('/'),
                    description = e.Description,
                    parameters = e.Parameters?.Select(p => new { name = p.Name, @in = p.Type, dataType = p.DataType, required = p.Required, defaultValue = p.DefaultValue, description = p.Description })
                })
            });
        }
    }
}
