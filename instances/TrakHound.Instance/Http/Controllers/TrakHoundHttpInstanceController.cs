// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Instance;
using TrakHound.Instance.Boots;
using TrakHound.Instance.Http;
using TrakHound.Instance.Security;
using TrakHound.Packages;

namespace TrakHound.Http
{
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Instance)]
    public class TrakHoundHttpInstanceController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;
        private readonly EndpointDataSource _endpointDataSource;


        public TrakHoundHttpInstanceController(ITrakHoundInstance instance, EndpointDataSource endpointDataSource)
        {
            _instance = instance;
            _endpointDataSource = endpointDataSource;
        }


        [HttpGet("_instances/information/host")]
        [RouteDescription("Gets information about the Instance (ID, Version, and the machine it runs on).")]
        [RouteResponse(200, typeof(TrakHoundInstanceInformation))]
        public IActionResult GetHostInformation()
        {
            return Ok(_instance.Information);
        }

        [HttpGet("_instances/health")]
        [RouteDescription("Gets the overall Health of the Instance.")]
        [RouteResponse(200, typeof(TrakHoundInstanceHealth))]
        public IActionResult GetHealth()
        {
            return Ok(_instance.Health.GetHealth());
        }

        [HttpGet("_instances/cpu")]
        [RouteDescription("Gets the CPU used by the Instance process, by Module Type and by Module (Workers, Functions, APIs, Apps and the Admin Interface), with the CPU not measured for any Module.")]
        [RouteResponse(200, typeof(TrakHoundCpuUsage))]
        public IActionResult GetCpuUsage()
        {
            return Ok(TrakHoundModuleCpuMonitors.Default.GetUsage());
        }

        [HttpGet("_instances/boots")]
        [RouteDescription("Lists the recent Boots of the Instance process (newest first) : when each Started, how long each step took, the Warnings and Errors logged, and how it ended.")]
        [RouteParameterDescription("take", "The most Boots to return (up to 100)")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundInstanceBoot>))]
        public IActionResult GetBoots([FromQuery] int take = 20)
        {
            // Read from the Boot History directly (not a Client), so it is available while the Runtime is Stopped or in Error
            var boots = TrakHoundInstanceLifetimeMonitor.BootHistory?.GetBoots() ?? Enumerable.Empty<TrakHoundInstanceBoot>();
            return Ok(boots.Take(Math.Clamp(take, 1, TrakHoundBootHistory.DefaultMaxBoots)));
        }

        [HttpGet("_instances/boots/{bootId}")]
        [RouteDescription("Gets a Boot of the Instance process by its ID.")]
        [RouteParameterDescription("bootId", "The ID of the Boot (from the Boots list)")]
        [RouteResponse(200, typeof(TrakHoundInstanceBoot))]
        [RouteResponse(404)]
        public IActionResult GetBoot(string bootId)
        {
            var boot = TrakHoundInstanceLifetimeMonitor.BootHistory?.GetBoot(bootId);
            if (boot == null) return NotFound();

            return Ok(boot);
        }

        [HttpGet("_instances/packages/verification")]
        [RouteDescription("Gets the Package Verification Mode in use and the result of the last check of each Package on disk : whether its files match its Manifest, who signed it, the commit it was built from, and whether it was refused.")]
        [RouteResponse(200, typeof(TrakHoundPackageVerificationReport))]
        public IActionResult GetPackageVerification()
        {
            return Ok(_instance.PackageManager.GetVerificationReport());
        }

        [HttpGet("_instances/packages/verification/{packageId}/{packageVersion}")]
        [RouteDescription("Gets the result of the last check of a Package version. It reports the last check and doesn't read the files, so it can be older than a change on disk.")]
        [RouteParameterDescription("packageId", "The ID of the Package")]
        [RouteParameterDescription("packageVersion", "The Version of the Package")]
        [RouteResponse(200, typeof(TrakHoundPackageVerificationInformation))]
        [RouteResponse(404)]
        public IActionResult GetPackageVerification(string packageId, string packageVersion)
        {
            var information = _instance.PackageManager.GetVerificationReport().Packages
                .FirstOrDefault(o => string.Equals(o.Id, packageId, StringComparison.OrdinalIgnoreCase) && string.Equals(o.Version, packageVersion, StringComparison.OrdinalIgnoreCase));
            if (information == null) return NotFound();

            return Ok(information);
        }

        [HttpGet("_instances/openapi")]
        [RouteDescription("Gets the OpenAPI Document for the System API (every Area).")]
        [RouteResponse(200, typeof(object))]
        public IActionResult GetOpenApiDocument()
        {
            return CreateOpenApiResponse(null);
        }

        [HttpGet("_instances/openapi/{area}")]
        [RouteDescription("Gets the OpenAPI Document for one Area of the System API (ex. volumes, services, functions).")]
        [RouteParameterDescription("area", "The Area (the first route segment without the underscore, ex. volumes for /_volumes)")]
        [RouteResponse(200, typeof(object))]
        [RouteResponse(404)]
        public IActionResult GetAreaOpenApiDocument(string area)
        {
            if (string.IsNullOrEmpty(area)) return NotFound();
            return CreateOpenApiResponse(area);
        }

        [HttpGet("_instances/openapi/areas")]
        [RouteDescription("Lists the Areas of the System API that have their own OpenAPI Document.")]
        [RouteResponse(200, typeof(IEnumerable<string>))]
        public IActionResult GetOpenApiAreas()
        {
            return Ok(TrakHoundSystemOpenApi.GetAreas(_endpointDataSource));
        }


        private IActionResult CreateOpenApiResponse(string area)
        {
            var serverUrl = TrakHoundIdentityHttp.GetBaseUrl(HttpContext, _instance.Configuration.BasePath);

            var document = TrakHoundSystemOpenApi.CreateJson(_endpointDataSource, _instance.Version, serverUrl, area);
            if (document == null) return NotFound();

            return Content(document, "application/json");
        }
    }
}
