// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace TrakHound.Volumes
{
    /// <summary>
    /// A Volume that stores Files and Directories on the local File System under a base Directory
    /// </summary>
    public class TrakHoundVolume : ITrakHoundVolume
    {
        private readonly string _id;
        private readonly string _basePath;
        private readonly List<ITrakHoundVolumeListener> _listeners = new List<ITrakHoundVolumeListener>();
        private readonly object _lock = new object();


        /// <summary>
        /// Gets the ID of the Volume
        /// </summary>
        public string Id => _id;


        /// <summary>
        /// Initializes a new Volume with the specified ID, rooted at the specified base Path
        /// </summary>
        public TrakHoundVolume(string id, string basePath)
        {
            _id = id;
            _basePath = basePath;
        }

        /// <summary>
        /// Disposes all Listeners created by the Volume
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var listener in _listeners) listener.Dispose();
                _listeners.Clear();
            }
        }


        /// <summary>
        /// Lists the Directories at the specified Path (relative to the Volume root). Returns paths relative to the Volume root
        /// </summary>
        public async Task<string[]> ListDirectories(string path = null, bool includeSubdirectories = false)
        {
            try
            {
                var fullPath = !string.IsNullOrEmpty(path) && path != "/" && path != "\\" ? Path.Combine(_basePath, path) : _basePath;
                if (Directory.Exists(fullPath))
                {
                    var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                    var filePaths = Directory.GetDirectories(fullPath, "*", searchOption);
                    if (!filePaths.IsNullOrEmpty())
                    {
                        var relativePaths = new List<string>();

                        foreach (var filePath in filePaths)
                        {
                            relativePaths.Add(Path.GetRelativePath(_basePath, filePath));
                        }

                        return relativePaths.ToArray();
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Lists the Files at the specified Path (relative to the Volume root). Returns paths relative to the Volume root
        /// </summary>
        public async Task<string[]> ListFiles(string path = null, bool includeSubdirectories = false)
        {
            try
            {
                var fullPath = !string.IsNullOrEmpty(path) && path != "/" && path != "\\" ? Path.Combine(_basePath, path) : _basePath;
                if (Directory.Exists(fullPath))
                {
                    var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                    var filePaths = Directory.GetFiles(fullPath, "*.*", searchOption);
                    if (!filePaths.IsNullOrEmpty())
                    {
                        var relativePaths = new List<string>();

                        foreach (var filePath in filePaths)
                        {
                            relativePaths.Add(Path.GetRelativePath(_basePath, filePath));
                        }

                        return relativePaths.ToArray();
                    }
                }
            }
            catch { }

            return null;
        }


        /// <summary>
        /// Creates an empty File at the specified Path, creating any parent Directories as needed
        /// </summary>
        public async Task<bool> CreateFile(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var fullPath = Path.Combine(_basePath, path);

                    var directory = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                    using (File.Create(fullPath)) { }
                    return true;
                }
                catch { }
            }

            return false;
        }


        /// <summary>
        /// Reads the contents of the File at the specified Path as a string
        /// </summary>
        public async Task<string> ReadString(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var fullPath = Path.Combine(_basePath, path);
                    if (File.Exists(fullPath))
                    {
                        return await File.ReadAllTextAsync(fullPath);
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Reads the File at the specified Path and deserializes its JSON contents to the specified type
        /// </summary>
        public async Task<T> ReadJson<T>(string path)
        {
            var json = await ReadString(path);
            return Json.Convert<T>(json);
        }

        /// <summary>
        /// Reads the contents of the File at the specified Path as a byte array
        /// </summary>
        public async Task<byte[]> ReadBytes(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var fullPath = Path.Combine(_basePath, path);
                    if (File.Exists(fullPath))
                    {
                        return await File.ReadAllBytesAsync(fullPath);
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Opens a read-only Stream to the File at the specified Path. The caller is responsible for disposing the Stream
        /// </summary>
        public async Task<Stream> ReadStream(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var fullPath = Path.Combine(_basePath, path);
                    if (File.Exists(fullPath))
                    {
                        return new FileStream(fullPath, FileMode.Open, FileAccess.Read);
                    }
                }
                catch { }
            }

            return null;
        }


        /// <summary>
        /// Writes the specified string content to the File at the specified Path, creating any parent Directories as needed
        /// </summary>
        public async Task<bool> WriteString(string path, string content)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var fullPath = Path.Combine(_basePath, path);

                    var directory = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                    await File.WriteAllTextAsync(fullPath, content);
                    return true;
                }
                catch { }
            }

            return false;
        }

        /// <summary>
        /// Serializes the specified content to JSON and writes it to the File at the specified Path
        /// </summary>
        public async Task<bool> WriteJson(string path, object content)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var json = Json.Convert(content, indented: true);
                return await WriteString(path, json);
            }

            return false;
        }

        /// <summary>
        /// Writes the specified byte array to the File at the specified Path, creating any parent Directories as needed
        /// </summary>
        public async Task<bool> WriteBytes(string path, byte[] content)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var fullPath = Path.Combine(_basePath, path);

                    var directory = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                    await File.WriteAllBytesAsync(fullPath, content);
                    return true;
                }
                catch { }
            }

            return false;
        }

        /// <summary>
        /// Writes the contents of the specified Stream to the File at the specified Path, creating any parent Directories as needed
        /// </summary>
        public async Task<bool> WriteStream(string path, Stream content)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var fullPath = Path.Combine(_basePath, path);

                    var directory = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                    if (content != null)
                    {
                        // Written to a temporary file first and then moved into place, so a Stream that fails part way (ex. an upload
                        // that is cancelled) doesn't leave a partial file, and a shorter file fully replaces a longer one
                        var tempPath = fullPath + $".{System.Guid.NewGuid():N}.tmp";
                        try
                        {
                            using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                            {
                                await content.CopyToAsync(fileStream);
                            }

                            File.Move(tempPath, fullPath, true);
                        }
                        finally
                        {
                            if (File.Exists(tempPath)) File.Delete(tempPath);
                        }
                    }
                    else
                    {
                        File.Create(fullPath).Dispose();
                    }

                    return true;
                }
                catch { }
            }

            return false;
        }


        /// <summary>
        /// Deletes the Directory at the specified Path
        /// </summary>
        public async Task<bool> DeleteDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var fullPath = Path.Combine(_basePath, path);
                    if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath);
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        /// <summary>
        /// Deletes the File at the specified Path
        /// </summary>
        public async Task<bool> DeleteFile(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    var fullPath = Path.Combine(_basePath, path);
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }


        /// <summary>
        /// Creates a Listener that raises notifications when Files within the Volume are created, modified, or deleted
        /// </summary>
        public ITrakHoundVolumeListener CreateListener(string path, string filter = null)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var listener = new TrakHoundVolumeListener(_basePath, path, filter);
                lock (_lock) _listeners.Add(listener);
                return listener;
            }

            return null;
        }
    }
}
