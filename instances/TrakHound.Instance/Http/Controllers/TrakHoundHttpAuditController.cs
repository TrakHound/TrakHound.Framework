// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Controllers.Http;
using TrakHound.Instance.Audit;
using TrakHound.Instance.Security;
using TrakHound.Instances;

namespace TrakHound.Http
{
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Audit)]
    public class TrakHoundHttpAuditController : ControllerBase
    {
        private const int _maxTake = 10000;

        private readonly ITrakHoundInstance _instance;


        public TrakHoundHttpAuditController(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        [HttpGet("_instances/audit")]
        [RouteDescription("Lists the recorded changes to Packages and Configurations (newest first) : when, what changed, who made the change and the file hashes before and after.")]
        [RouteParameterDescription("take", "The most Records to return (up to 10,000)")]
        [RouteParameterDescription("search", "Only Records with a Target or User containing this text")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundAuditRecord>))]
        [RouteResponse(404)]
        public IActionResult GetRecords([FromQuery] int take = 100, [FromQuery] string search = null)
        {
            var log = _instance.AuditMonitor?.Log;
            if (log == null) return NotFound();

            return Ok(log.GetRecords(Math.Clamp(take, 1, _maxTake), search));
        }

        [HttpGet("_instances/audit/verify")]
        [RouteDescription("Checks the Hash Chain of the Audit Log and reports the first line that fails. It can't detect Records removed from the end or the whole file being replaced.")]
        [RouteResponse(200, typeof(TrakHoundAuditVerification))]
        [RouteResponse(404)]
        public IActionResult Verify()
        {
            var log = _instance.AuditMonitor?.Log;
            if (log == null) return NotFound();

            return Ok(log.Verify());
        }
    }
}
