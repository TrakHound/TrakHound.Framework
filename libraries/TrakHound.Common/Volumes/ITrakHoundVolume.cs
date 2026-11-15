// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading.Tasks;

namespace TrakHound.Volumes
{
    /// <summary>
    /// Interface for a Volume used to store and access Files and Directories
    /// </summary>
    public interface ITrakHoundVolume : IDisposable
    {
        /// <summary>
        /// Gets the ID of the Volume
        /// </summary>
        string Id { get; }


        /// <summary>
        /// Lists the Directories at the specified Path (relative to the Volume root). Returns paths relative to the Volume root
        /// </summary>
        Task<string[]> ListDirectories(string path = null, bool includeSubdirectories = false);

        /// <summary>
        /// Lists the Files at the specified Path (relative to the Volume root). Returns paths relative to the Volume root
        /// </summary>
        Task<string[]> ListFiles(string path = null, bool includeSubdirectories = false);


        /// <summary>
        /// Creates an empty File at the specified Path, creating any parent Directories as needed
        /// </summary>
        Task<bool> CreateFile(string path);


        /// <summary>
        /// Reads the contents of the File at the specified Path as a string
        /// </summary>
        Task<string> ReadString(string path);

        /// <summary>
        /// Reads the File at the specified Path and deserializes its JSON contents to the specified type
        /// </summary>
        Task<T> ReadJson<T>(string path);

        /// <summary>
        /// Reads the contents of the File at the specified Path as a byte array
        /// </summary>
        Task<byte[]> ReadBytes(string path);

        /// <summary>
        /// Opens a read-only Stream to the File at the specified Path. The caller is responsible for disposing the Stream
        /// </summary>
        Task<Stream> ReadStream(string path);


        /// <summary>
        /// Writes the specified string content to the File at the specified Path, creating any parent Directories as needed
        /// </summary>
        Task<bool> WriteString(string path, string content);

        /// <summary>
        /// Serializes the specified content to JSON and writes it to the File at the specified Path
        /// </summary>
        Task<bool> WriteJson(string path, object content);

        /// <summary>
        /// Writes the specified byte array to the File at the specified Path, creating any parent Directories as needed
        /// </summary>
        Task<bool> WriteBytes(string path, byte[] content);

        /// <summary>
        /// Writes the contents of the specified Stream to the File at the specified Path, creating any parent Directories as needed
        /// </summary>
        Task<bool> WriteStream(string path, Stream content);


        /// <summary>
        /// Deletes the Directory at the specified Path
        /// </summary>
        Task<bool> DeleteDirectory(string path);

        /// <summary>
        /// Deletes the File at the specified Path
        /// </summary>
        Task<bool> DeleteFile(string path);


        /// <summary>
        /// Creates a Listener that raises notifications when Files within the Volume are created, modified, or deleted
        /// </summary>
        ITrakHoundVolumeListener CreateListener(string path, string filter = null);
    }
}
