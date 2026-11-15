// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Clients;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    /// <summary>
    /// System API for reading the Modules loaded in this Instance (and the Modules that failed to load)
    /// </summary>
    [ApiController]
    [Route(TrakHoundHttpConstants.ModulesPrefix)]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Modules)]
    public class TrakHoundHttpModulesController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;


        public TrakHoundHttpModulesController(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        [HttpGet]
        [RouteDescription("Lists the Modules loaded in the Instance and the Modules that failed to load.")]
        [RouteResponse(200, typeof(IEnumerable<TrakHound.Modules.TrakHoundModuleInformation>))]
        public IActionResult GetModules()
        {
            return Ok(TrakHoundInstanceModulesClient.GetModules(_instance.ModuleProvider));
        }
    }
}
