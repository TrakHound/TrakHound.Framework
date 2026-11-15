// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.ServiceRoutersPrefix)]
    [Produces("application/json")]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Services)]
    public class TrakHoundHttpServiceRoutersController : TrakHoundHttpServiceControllerBase
    {
        public TrakHoundHttpServiceRoutersController(ITrakHoundInstance instance) : base(instance) { }


        [HttpGet]
        [RouteDescription("Lists the Service Routers.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHound.Services.TrakHoundServiceRouterInformation>))]
        [RouteResponse(404)]
        public async Task<IActionResult> GetRouters()
        {
            using var client = GetClient();
            if (client != null)
            {
                var informations = await client.Services.Routers.GetRouters();
                if (informations != null)
                {
                    return Ok(informations);
                }
            }

            return NotFound();
        }

        [HttpGet("{routerId}")]
        [RouteDescription("Gets a Service Router.")]
        [RouteParameterDescription("routerId", "The ID of the Service Router")]
        [RouteResponse(200, typeof(TrakHound.Services.TrakHoundServiceRouterInformation))]
        [RouteResponse(404)]
        public async Task<IActionResult> GetRouter([FromRoute] string routerId)
        {
            using var client = GetClient();
            if (client != null)
            {
                var information = await client.Services.Routers.GetRouter(routerId);
                if (information != null)
                {
                    return Ok(information);
                }
            }

            return NotFound();
        }
    }
}
