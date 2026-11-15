// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using TrakHound.Api;
using TrakHound.Http;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Security;

namespace TrakHound.Instance.Mcp
{
    /// <summary>
    /// The MCP Server's Resources : the Instance's llms.txt, and the OpenAPI Document of each Api the caller can use (with only the
    /// Endpoints they can use). Both are the Api Catalog's content, so they require "read" on system:api-catalog like /_developer
    /// </summary>
    public static class TrakHoundMcpResources
    {
        public const string LlmsTextUri = "trakhound://llms.txt";
        private const string ApiUriPrefix = "trakhound://apis/";
        private const string OpenApiUriSuffix = "/openapi";


        public static ValueTask<ListResourcesResult> List(ITrakHoundInstance instance, RequestContext<ListResourcesRequestParams> context)
        {
            var resources = new List<Resource>();

            if (CanReadCatalog())
            {
                resources.Add(new Resource
                {
                    Uri = LlmsTextUri,
                    Name = "llms.txt",
                    Title = "Instance overview",
                    Description = "What this TrakHound Instance is, how to connect, and the Apis and Endpoints you can call (Markdown). Read it first.",
                    MimeType = "text/markdown"
                });

                foreach (var api in GetApis(instance))
                {
                    var name = !string.IsNullOrWhiteSpace(api.Name) ? api.Name.Trim() : api.Route;
                    resources.Add(new Resource
                    {
                        Uri = GetOpenApiUri(api.Id),
                        Name = $"{name} OpenAPI",
                        Title = $"{name} OpenAPI Document",
                        Description = $"The OpenAPI Document of the Api at {api.Route} (only the Endpoints you can call){(string.IsNullOrWhiteSpace(api.Description) ? "" : $": {api.Description.Trim()}")}",
                        MimeType = "application/json"
                    });
                }
            }

            return ValueTask.FromResult(new ListResourcesResult { Resources = resources });
        }

        public static ValueTask<ReadResourceResult> Read(ITrakHoundInstance instance, RequestContext<ReadResourceRequestParams> context)
        {
            var uri = context.Params?.Uri;
            if (string.IsNullOrEmpty(uri) || !CanReadCatalog()) throw new McpException($"Resource not found : {uri}");

            var httpContext = context.Services?.GetService<IHttpContextAccessor>()?.HttpContext;
            var baseUrl = httpContext != null ? TrakHoundIdentityHttp.GetBaseUrl(httpContext, instance.Configuration?.BasePath) : "";

            if (uri == LlmsTextUri)
            {
                var text = TrakHoundLlmsText.Create(instance, GetApis(instance), baseUrl, TrakHoundApiKeyIdentityProvider.ApiKeyHeader);
                return Result(uri, "text/markdown", text);
            }

            if (uri.StartsWith(ApiUriPrefix, StringComparison.Ordinal) && uri.EndsWith(OpenApiUriSuffix, StringComparison.Ordinal))
            {
                var apiId = Uri.UnescapeDataString(uri[ApiUriPrefix.Length..^OpenApiUriSuffix.Length]);
                var apiProvider = instance.ApiProvider as TrakHoundApiProvider;
                var api = apiProvider?.GetCatalogApi(apiId, instance.SecurityManager, TrakHoundSecurityContext.Current?.Identity); // Only this Api (not the whole Catalog)
                if (api != null)
                {
                    var serverUrl = baseUrl + (api.Route != "/" ? api.Route : "");
                    var document = apiProvider.GetCatalogOpenApiDocument(apiId, instance.SecurityManager, TrakHoundSecurityContext.Current?.Identity, serverUrl, TrakHoundApiKeyIdentityProvider.ApiKeyHeader);
                    if (document != null) return Result(uri, "application/json", document);
                }
            }

            throw new McpException($"Resource not found : {uri}");
        }


        private static string GetOpenApiUri(string apiId) => $"{ApiUriPrefix}{Uri.EscapeDataString(apiId)}{OpenApiUriSuffix}";

        // The Api Catalog's resource (the same as /_developer and /llms.txt)
        private static bool CanReadCatalog()
        {
            var securityContext = TrakHoundSecurityContext.Current;
            if (securityContext == null) return false;
            return securityContext.Authorize(TrakHoundSystemSecurity.ApiCatalog, TrakHoundSystemSecurity.ReadPermission).Status == TrakHoundAuthorizationStatus.Allowed;
        }

        // The Apis served over HTTP with the Endpoints the caller can use
        private static IEnumerable<TrakHoundApiCatalogApi> GetApis(ITrakHoundInstance instance)
        {
            if (instance.ApiProvider is not TrakHoundApiProvider apiProvider) return Enumerable.Empty<TrakHoundApiCatalogApi>();
            return apiProvider.GetCatalog(instance.SecurityManager, TrakHoundSecurityContext.Current?.Identity) ?? Enumerable.Empty<TrakHoundApiCatalogApi>();
        }

        private static ValueTask<ReadResourceResult> Result(string uri, string mimeType, string text)
        {
            return ValueTask.FromResult(new ReadResourceResult
            {
                Contents = new List<ResourceContents> { new TextResourceContents { Uri = uri, MimeType = mimeType, Text = text } }
            });
        }
    }
}
