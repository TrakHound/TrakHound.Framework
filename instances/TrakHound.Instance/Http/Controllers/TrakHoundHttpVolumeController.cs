// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Instance.Security;
using TrakHound.Zones;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.VolumesPrefix)]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Volumes)]
    public class TrakHoundHttpVolumesController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;


        public TrakHoundHttpVolumesController(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        // A Request made with a Module Token is checked against the Module's Zone (the same as a call through the Module's Client)
        private bool IsZoneAllowed(string volumeId, string path, string permission)
        {
            var source = HttpContext.GetModuleSource();
            if (source == null || _instance.ZoneManager == null) return true;

            return _instance.ZoneManager.Authorize(source, null, TrakHoundZoneResources.GetVolumeResourceId(volumeId, path), permission).IsAllowed;
        }


        [HttpGet("list/directories")]
        [RouteDescription("Lists the directories in a Volume directory.")]
        [RouteParameterDescription("volumeId", "The ID of the Volume")]
        [RouteParameterDescription("path", "The directory to list, relative to the Volume root (the root when empty)")]
        [RouteParameterDescription("includeSubdirectories", "Whether to include the directories in subdirectories")]
        [RouteResponse(200, typeof(IEnumerable<string>))]
        [RouteResponse(400)]
        [RouteResponse(403)]
        [RouteResponse(404)]
        public async Task<IActionResult> ListDirectories(
            [FromQuery] string volumeId,
            [FromQuery] string path = null,
            [FromQuery] bool includeSubdirectories = false
            )
        {
            if (!string.IsNullOrEmpty(volumeId))
            {
                if (!IsZoneAllowed(volumeId, path, TrakHoundZoneResources.ReadPermission)) return StatusCode(403, "Denied by Zone");

                var volume = _instance.VolumeProvider.GetVolume(volumeId);
                if (volume != null)
                {
                    var files = await volume.ListDirectories(path, includeSubdirectories);
                    if (!files.IsNullOrEmpty())
                    {
                        return Ok(files);
                    }
                    else
                    {
                        return NotFound("No Directories Found");
                    }
                }
                else
                {
                    return NotFound("Volume Not Found");
                }
            }
            else
            {
                return BadRequest("No volumeId specified");
            }
        }

        [HttpGet("list/files")]
        [RouteDescription("Lists the files in a Volume directory.")]
        [RouteParameterDescription("volumeId", "The ID of the Volume")]
        [RouteParameterDescription("path", "The directory to list, relative to the Volume root (the root when empty)")]
        [RouteParameterDescription("includeSubdirectories", "Whether to include the files in subdirectories")]
        [RouteResponse(200, typeof(IEnumerable<string>))]
        [RouteResponse(400)]
        [RouteResponse(403)]
        [RouteResponse(404)]
        public async Task<IActionResult> ListFiles(
            [FromQuery] string volumeId,
            [FromQuery] string path = null,
            [FromQuery] bool includeSubdirectories = false
            )
        {
            if (!string.IsNullOrEmpty(volumeId))
            {
                if (!IsZoneAllowed(volumeId, path, TrakHoundZoneResources.ReadPermission)) return StatusCode(403, "Denied by Zone");

                var volume = _instance.VolumeProvider.GetVolume(volumeId);
                if (volume != null)
                {
                    var files = await volume.ListFiles(path, includeSubdirectories);
                    if (!files.IsNullOrEmpty())
                    {
                        return Ok(files);
                    }
                    else
                    {
                        return NotFound("No Files Found");
                    }
                }
                else
                {
                    return NotFound("Volume Not Found");
                }
            }
            else
            {
                return BadRequest("No volumeId specified");
            }
        }

        [HttpGet("file")]
        [RouteDescription("Checks that a file path in a Volume can be read (file information is not returned yet).")]
        [RouteParameterDescription("volumeId", "The ID of the Volume")]
        [RouteParameterDescription("path", "The path of the file, relative to the Volume root")]
        [RouteResponse(200)]
        [RouteResponse(400)]
        [RouteResponse(403)]
        [RouteResponse(404)]
        public IActionResult GetFile([FromQuery] string volumeId, [FromQuery] string path)
        {
            if (!string.IsNullOrEmpty(volumeId))
            {
                if (!IsZoneAllowed(volumeId, path, TrakHoundZoneResources.ReadPermission)) return StatusCode(403, "Denied by Zone");

                var volume = _instance.VolumeProvider.GetVolume(volumeId);
                if (volume != null)
                {
                    // Read File Information (Size, etc.)

                    return Ok();
                }
                else
                {
                    return NotFound("Volume Not Found");
                }
            }
            else
            {
                return BadRequest("No volumeId specified");
            }
        }

        [HttpGet("file/download")]
        [RouteDescription("Downloads a file from a Volume.")]
        [RouteParameterDescription("volumeId", "The ID of the Volume")]
        [RouteParameterDescription("path", "The path of the file, relative to the Volume root")]
        [RouteResponse(200, typeof(byte[]))]
        [RouteResponse(400)]
        [RouteResponse(403)]
        [RouteResponse(404)]
        public async Task<IActionResult> DownloadFile([FromQuery] string volumeId, [FromQuery] string path)
        {
            if (!string.IsNullOrEmpty(volumeId) && !string.IsNullOrEmpty(path))
            {
                if (!IsZoneAllowed(volumeId, path, TrakHoundZoneResources.ReadPermission)) return StatusCode(403, "Denied by Zone");

                var volume = _instance.VolumeProvider.GetVolume(volumeId);
                if (volume != null)
                {
                    // Streamed from the file (instead of read into memory), and range requests are supported for large files
                    var stream = await volume.ReadStream(path);
                    if (stream != null)
                    {
                        var filename = System.IO.Path.GetFileName(path);
                        var contentType = MimeTypes.GetMimeType(filename);

                        return File(stream, contentType, filename, enableRangeProcessing: true);
                    }
                    else
                    {
                        return NotFound("File Not Found");
                    }
                }
                else
                {
                    return NotFound("Volume Not Found");
                }
            }
            else
            {
                return BadRequest("No volumeId specified");
            }
        }

        [HttpPut("file/create")]
        [TrakHoundSystemWrite]
        [RouteDescription("Creates an empty file in a Volume.")]
        [RouteParameterDescription("volumeId", "The ID of the Volume")]
        [RouteParameterDescription("path", "The path of the file, relative to the Volume root")]
        [RouteResponse(201)]
        [RouteResponse(400)]
        [RouteResponse(403)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        public async Task<IActionResult> CreateFile([FromQuery] string volumeId, [FromQuery] string path)
        {
            if (!string.IsNullOrEmpty(volumeId) && !string.IsNullOrEmpty(path))
            {
                if (!IsZoneAllowed(volumeId, path, TrakHoundZoneResources.WritePermission)) return StatusCode(403, "Denied by Zone");

                var volume = _instance.VolumeProvider.GetVolume(volumeId);
                if (volume != null)
                {
                    if (await volume.CreateFile(path))
                    {
                        return Created();
                    }
                    else
                    {
                        return StatusCode(500, "Error Creating File");
                    }
                }
                else
                {
                    return NotFound("Volume Not Found");
                }
            }
            else
            {
                return BadRequest("No volumeId specified");
            }
        }

        [HttpPost("file/upload")]
        [TrakHoundSystemWrite]
        [RouteDescription("Uploads a file to a Volume (the request body is the file contents).")]
        [RouteParameterDescription("volumeId", "The ID of the Volume")]
        [RouteParameterDescription("path", "The path of the file, relative to the Volume root")]
        [RouteRequestBody("application/octet-stream", Description = "The file contents")]
        [RouteResponse(201)]
        [RouteResponse(400)]
        [RouteResponse(403)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        public async Task<IActionResult> UploadFile([FromQuery] string volumeId, [FromQuery] string path)
        {
            if (!string.IsNullOrEmpty(volumeId) && !string.IsNullOrEmpty(path) && Request.Body != null)
            {
                if (!IsZoneAllowed(volumeId, path, TrakHoundZoneResources.WritePermission)) return StatusCode(403, "Denied by Zone");

                var volume = _instance.VolumeProvider.GetVolume(volumeId);
                if (volume != null)
                {
                    // Streamed to the file (instead of held in memory)
                    if (await volume.WriteStream(path, Request.Body))
                    {
                        return Created();
                    }
                    else
                    {
                        return StatusCode(500, "Error Uploading File");
                    }
                }
                else
                {
                    return NotFound("Volume Not Found");
                }
            }
            else
            {
                return BadRequest("No volumeId specified");
            }
        }

        [HttpDelete("directory")]
        [TrakHoundSystemWrite]
        [RouteDescription("Deletes a directory from a Volume.")]
        [RouteParameterDescription("volumeId", "The ID of the Volume")]
        [RouteParameterDescription("path", "The path of the directory, relative to the Volume root")]
        [RouteResponse(200)]
        [RouteResponse(400)]
        [RouteResponse(403)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        public async Task<IActionResult> DeleteDirectory([FromQuery] string volumeId, [FromQuery] string path)
        {
            if (!string.IsNullOrEmpty(volumeId) && !string.IsNullOrEmpty(path))
            {
                if (!IsZoneAllowed(volumeId, path, TrakHoundZoneResources.WritePermission)) return StatusCode(403, "Denied by Zone");

                var volume = _instance.VolumeProvider.GetVolume(volumeId);
                if (volume != null)
                {
                    if (await volume.DeleteDirectory(path))
                    {
                        return Ok();
                    }
                    else
                    {
                        return StatusCode(500, "Error Deleting Directory");
                    }
                }
                else
                {
                    return NotFound("Volume Not Found");
                }
            }
            else
            {
                return BadRequest("No volumeId specified");
            }
        }

        [HttpDelete("file")]
        [TrakHoundSystemWrite]
        [RouteDescription("Deletes a file from a Volume.")]
        [RouteParameterDescription("volumeId", "The ID of the Volume")]
        [RouteParameterDescription("path", "The path of the file, relative to the Volume root")]
        [RouteResponse(200)]
        [RouteResponse(400)]
        [RouteResponse(403)]
        [RouteResponse(404)]
        [RouteResponse(500)]
        public async Task<IActionResult> DeleteFile([FromQuery] string volumeId, [FromQuery] string path)
        {
            if (!string.IsNullOrEmpty(volumeId) && !string.IsNullOrEmpty(path))
            {
                if (!IsZoneAllowed(volumeId, path, TrakHoundZoneResources.WritePermission)) return StatusCode(403, "Denied by Zone");

                var volume = _instance.VolumeProvider.GetVolume(volumeId);
                if (volume != null)
                {
                    if (await volume.DeleteFile(path))
                    {
                        return Ok();
                    }
                    else
                    {
                        return StatusCode(500, "Error Deleting File");
                    }
                }
                else
                {
                    return NotFound("Volume Not Found");
                }
            }
            else
            {
                return BadRequest("No volumeId specified");
            }
        }
    }
}
