// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to access the files in the Volumes of a TrakHound Instance
    /// </summary>
    public interface ITrakHoundVolumesClient
    {
        /// <summary>
        /// Lists the directories at the path (relative to the Volume root) in the specified Volume
        /// </summary>
        Task<IEnumerable<string>> ListDirectories(string volumeId, string path = null, bool includeSubdirectories = false);

        /// <summary>
        /// Lists the files at the path (relative to the Volume root) in the specified Volume
        /// </summary>
        Task<IEnumerable<string>> ListFiles(string volumeId, string path = null, bool includeSubdirectories = false);

        /// <summary>
        /// Creates an empty file at the path in the specified Volume. Returns true if successful
        /// </summary>
        Task<bool> CreateFile(string volumeId, string path);

        /// <summary>
        /// Downloads the contents of the file at the path in the specified Volume (null if not found)
        /// </summary>
        Task<byte[]> DownloadFile(string volumeId, string path);

        /// <summary>
        /// Uploads the content to the file at the path in the specified Volume. Returns true if successful
        /// </summary>
        Task<bool> UploadFile(string volumeId, string path, byte[] content);

        /// <summary>
        /// Deletes the directory at the path in the specified Volume. Returns true if successful
        /// </summary>
        Task<bool> DeleteDirectory(string volumeId, string path);

        /// <summary>
        /// Deletes the file at the path in the specified Volume. Returns true if successful
        /// </summary>
        Task<bool> DeleteFile(string volumeId, string path);
    }
}
