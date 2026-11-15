// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Volumes;
using TrakHound.Zones;

namespace TrakHound.Clients
{
    public class TrakHoundInstanceVolumesClient : ITrakHoundVolumesClient
    {
        private readonly TrakHoundInstanceClient _baseClient;
        private readonly ITrakHoundVolumeProvider _volumeProvider;


        public TrakHoundInstanceVolumesClient(TrakHoundInstanceClient baseClient, ITrakHoundVolumeProvider volumeProvider)
        {
            _baseClient = baseClient;
            _volumeProvider = volumeProvider;
        }


        public async Task<IEnumerable<string>> ListDirectories(string volumeId, string path = null, bool includeSubdirectories = false)
        {
            var volume = GetVolume(volumeId, path, TrakHoundZoneResources.ReadPermission);
            if (volume != null)
            {
                return await volume.ListDirectories(path, includeSubdirectories);
            }

            return null;
        }

        public async Task<IEnumerable<string>> ListFiles(string volumeId, string path = null, bool includeSubdirectories = false)
        {
            var volume = GetVolume(volumeId, path, TrakHoundZoneResources.ReadPermission);
            if (volume != null)
            {
                return await volume.ListFiles(path, includeSubdirectories);
            }

            return null;
        }

        public async Task<bool> CreateFile(string volumeId, string path)
        {
            var volume = GetVolume(volumeId, path, TrakHoundZoneResources.WritePermission);
            if (volume != null)
            {
                return await volume.CreateFile(path);
            }

            return false;
        }

        public async Task<byte[]> DownloadFile(string volumeId, string path)
        {
            var volume = GetVolume(volumeId, path, TrakHoundZoneResources.ReadPermission);
            if (volume != null)
            {
                return await volume.ReadBytes(path);
            }

            return null;
        }

        public async Task<bool> UploadFile(string volumeId, string path, byte[] content)
        {
            var volume = GetVolume(volumeId, path, TrakHoundZoneResources.WritePermission);
            if (volume != null)
            {
                return await volume.WriteBytes(path, content);
            }

            return false;
        }

        public async Task<bool> DeleteDirectory(string volumeId, string path)
        {
            var volume = GetVolume(volumeId, path, TrakHoundZoneResources.WritePermission);
            if (volume != null)
            {
                return await volume.DeleteDirectory(path);
            }

            return false;
        }

        public async Task<bool> DeleteFile(string volumeId, string path)
        {
            var volume = GetVolume(volumeId, path, TrakHoundZoneResources.WritePermission);
            if (volume != null)
            {
                return await volume.DeleteFile(path);
            }

            return false;
        }


        // Gets the Volume if the Zone of the caller allows the access (null if not)
        private ITrakHoundVolume GetVolume(string volumeId, string path, string permission)
        {
            var zoneDecision = _baseClient.AuthorizeZone(null, TrakHoundZoneResources.GetVolumeResourceId(volumeId, path), permission);
            if (!zoneDecision.IsAllowed) return null;

            return _volumeProvider.GetVolume(volumeId);
        }
    }
}
