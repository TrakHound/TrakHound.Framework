// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Api;
using TrakHound.Instance.Api;
using TrakHound.Instance.Http;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Security;

namespace TrakHound.Http
{
    /// <summary>
    /// Describes the Apis to developers building their own applications (served on the HTTP Port, unlike the System API). Only the Apis that are
    /// served over HTTP and the Endpoints that the caller's Identity is allowed to call are included
    /// </summary>
    [TrakHoundSystemResource(TrakHoundSystemSecurity.ApiCatalog)]
    public class TrakHoundHttpDeveloperController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;


        public TrakHoundHttpDeveloperController(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        [HttpGet("_developer/catalog")]
        public IActionResult GetCatalog()
        {
            var apiProvider = _instance.ApiProvider as TrakHoundApiProvider;
            if (apiProvider == null) return NotFound();

            var identity = TrakHoundSecurityContext.Current?.Identity;

            var catalog = new TrakHoundApiCatalog();
            catalog.ApiKeyHeader = TrakHoundApiKeyIdentityProvider.ApiKeyHeader;
            catalog.SignInProviders = GetSignInProviders();
            catalog.Apis = apiProvider.GetCatalog(_instance.SecurityManager, identity);

            if (identity != null)
            {
                catalog.Identity = new TrakHoundApiCatalogIdentity
                {
                    ProviderId = identity.ProviderId,
                    UserId = identity.UserId,
                    Name = identity.Name
                };
            }

            // Depends on the caller's Identity
            Response.Headers.CacheControl = "no-store";

            return Ok(catalog);
        }

        /// <summary>
        /// llms.txt : a Markdown index for AI agents of the Instance, how to connect, and the Apis and Endpoints the caller can use
        /// </summary>
        [HttpGet("llms.txt")]
        public IActionResult GetLlmsText()
        {
            var apiProvider = _instance.ApiProvider as TrakHoundApiProvider;
            if (apiProvider == null) return NotFound();

            var identity = TrakHoundSecurityContext.Current?.Identity;
            var apis = apiProvider.GetCatalog(_instance.SecurityManager, identity);
            var baseUrl = TrakHoundIdentityHttp.GetBaseUrl(HttpContext, _instance.Configuration.BasePath);

            // Depends on the caller's Identity
            Response.Headers.CacheControl = "no-store";
            return Content(TrakHoundLlmsText.Create(_instance, apis, baseUrl, TrakHoundApiKeyIdentityProvider.ApiKeyHeader), TrakHoundLlmsText.ContentType);
        }

        /// <summary>
        /// The OpenAPI Document for an Api, describing only the Endpoints the caller can use
        /// </summary>
        [HttpGet("_developer/openapi/{apiId}")]
        public IActionResult GetOpenApiDocument(string apiId)
        {
            var apiProvider = _instance.ApiProvider as TrakHoundApiProvider;
            if (apiProvider == null) return NotFound();

            var identity = TrakHoundSecurityContext.Current?.Identity;

            var apiRoute = apiProvider.GetCatalogApi(apiId, _instance.SecurityManager, identity)?.Route; // Only this Api (not the whole Catalog)
            if (apiRoute == null) return NotFound();

            var serverUrl = TrakHoundIdentityHttp.GetBaseUrl(HttpContext, _instance.Configuration.BasePath) + (apiRoute != "/" ? apiRoute : "");

            var document = apiProvider.GetCatalogOpenApiDocument(apiId, _instance.SecurityManager, identity, serverUrl, TrakHoundApiKeyIdentityProvider.ApiKeyHeader);
            if (document == null) return NotFound();

            Response.Headers.CacheControl = "no-store";

            return Content(document, "application/json");
        }


        private IEnumerable<TrakHoundApiCatalogSignInProvider> GetSignInProviders()
        {
            var providers = new List<TrakHoundApiCatalogSignInProvider>();

            foreach (var provider in _instance.SecurityManager.GetProviders())
            {
                var features = provider.Features;
                if (features.HasFlag(TrakHoundIdentityProviderFeatures.Redirect) || features.HasFlag(TrakHoundIdentityProviderFeatures.Credentials))
                {
                    providers.Add(new TrakHoundApiCatalogSignInProvider
                    {
                        Id = provider.Id,
                        Name = provider.Id,
                        SignInPath = $"{TrakHoundIdentityHttp.SignInPrefix}/{Uri.EscapeDataString(provider.Id)}"
                    });
                }
            }

            return providers;
        }
    }
}
