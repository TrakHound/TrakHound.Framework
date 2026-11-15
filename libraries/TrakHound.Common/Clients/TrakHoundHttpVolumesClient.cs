// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Http;

namespace TrakHound.Clients
{
    /// <summary>
    /// HTTP implementation of ITrakHoundVolumesClient that uses the Volumes Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpVolumesClient : ITrakHoundVolumesClient
    {
		private const string _apiRoute = TrakHoundHttpConstants.VolumesPrefix;

		private readonly ITrakHoundClient _client;


		/// <summary>
		/// Creates a new Volumes Client that sends Requests through the Api Client of the specified Client
		/// </summary>
		public TrakHoundHttpVolumesClient(ITrakHoundClient client)
		{
			_client = client;
		}


        /// <inheritdoc/>
        public async Task<IEnumerable<string>> ListDirectories(string volumeId, string path = null, bool includeSubdirectories = false)
        {
            var route = Url.Combine(_apiRoute, "list/directories");

            var parameters = new Dictionary<string, string>();
            parameters.Add("volumeId", volumeId);
            parameters.Add("path", path);
            parameters.Add("includeSubdirectories", includeSubdirectories.ToString());

            return await _client.Api.GetJson<IEnumerable<string>>(route, parameters);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<string>> ListFiles(string volumeId, string path = null, bool includeSubdirectories = false)
        {
			var route = Url.Combine(_apiRoute, "list/files");

            var parameters = new Dictionary<string, string>();
            parameters.Add("volumeId", volumeId);
            parameters.Add("path", path);
            parameters.Add("includeSubdirectories", includeSubdirectories.ToString());

            return await _client.Api.GetJson<IEnumerable<string>>(route, parameters);
        }

        /// <inheritdoc/>
        public async Task<bool> CreateFile(string volumeId, string path)
        {
            var route = Url.Combine(_apiRoute, "file/create");

            var parameters = new Dictionary<string, string>();
            parameters.Add("volumeId", volumeId);
            parameters.Add("path", path);

            var response = await _client.Api.Put(route, queryParameters: parameters);
            return response.Success;
        }

        /// <inheritdoc/>
        public async Task<byte[]> DownloadFile(string volumeId, string path)
		{
            var route = Url.Combine(_apiRoute, "file/download");

            var parameters = new Dictionary<string, string>();
			parameters.Add("volumeId", volumeId);
			parameters.Add("path", path);

			return await _client.Api.GetBytes(route, parameters);
		}

        /// <inheritdoc/>
        public async Task<bool> UploadFile(string volumeId, string path, byte[] content)
        {
            var route = Url.Combine(_apiRoute, "file/upload");

            var parameters = new Dictionary<string, string>();
            parameters.Add("volumeId", volumeId);
            parameters.Add("path", path);

            var response = await _client.Api.Post(route, content, queryParameters: parameters);
            return response.Success;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteDirectory(string volumeId, string path)
        {
            var route = Url.Combine(_apiRoute, "directory");

            var parameters = new Dictionary<string, string>();
            parameters.Add("volumeId", volumeId);
            parameters.Add("path", path);

            var response = await _client.Api.Delete(route, queryParameters: parameters);
            return response.Success;
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteFile(string volumeId, string path)
        {
            var route = Url.Combine(_apiRoute, "file");

            var parameters = new Dictionary<string, string>();
            parameters.Add("volumeId", volumeId);
            parameters.Add("path", path);

            var response = await _client.Api.Delete(route, queryParameters: parameters);
            return response.Success;
        }
    }
}
