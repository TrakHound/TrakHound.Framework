// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.IO.Enumeration;
using TrakHound;
using TrakHound.Services;
using TrakHound.Services.Blobs;

namespace Trakhound.FileSystem.Service
{
    public class FileSystemBlobService : 
        TrakHoundService,
        ITrakHoundBlobService
    {
        private const string DefaultDirectory = "files/blobs";
        private const string MetadataExtension = ".metadata.json";
        private const string TempExtension = ".tmp";

        private readonly string _directory;


        public FileSystemBlobService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            if (configuration != null)
            {
                var path = configuration.GetParameter("directory");
                if (string.IsNullOrEmpty(path)) path = DefaultDirectory;
                if (!Path.IsPathRooted(path)) path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);

                _directory = path;

                if (!Directory.Exists(_directory))
                {
                    try
                    {
                        Directory.CreateDirectory(_directory);
                    }
                    catch { }
                }
            }
        }


        /// <summary>
        /// Resolves the directory for a Collection, ensuring it stays within the root Blob directory.
        /// Returns null if the Collection name is invalid or resolves outside of the root directory.
        /// </summary>
        private string GetCollectionDirectory(string collection)
        {
            if (string.IsNullOrEmpty(collection) || string.IsNullOrEmpty(_directory)) return null;

            try
            {
                var root = Path.GetFullPath(_directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var path = Path.GetFullPath(Path.Combine(root, collection));

                // Must be a direct child of the root directory (no nested paths or traversal)
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
                if (path.Length <= root.Length) return null;
                if (path.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, root.Length) >= 0) return null;

                return path;
            }
            catch
            {
                return null;
            }
        }

        private static string GetMetadataPath(string blobPath) => blobPath + MetadataExtension;

        private static bool IsBlobFile(string path) =>
            !path.EndsWith(MetadataExtension, StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<string> EnumerateBlobFiles(string collectionDirectory) =>
            Directory.EnumerateFiles(collectionDirectory).Where(IsBlobFile);

        // The same options as Directory.EnumerateFiles (include hidden and system files, and throw on inaccessible directories)
        private static readonly EnumerationOptions _countOptions = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };

        // Counts the Blob Files without building a path for each File (ListCollections counts every Collection)
        private static int CountBlobFiles(string collectionDirectory)
        {
            var files = new FileSystemEnumerable<bool>(collectionDirectory, (ref FileSystemEntry entry) => true, _countOptions)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                    !entry.IsDirectory &&
                    !entry.FileName.EndsWith(MetadataExtension, StringComparison.OrdinalIgnoreCase) &&
                    !entry.FileName.EndsWith(TempExtension, StringComparison.OrdinalIgnoreCase)
            };

            var count = 0;
            foreach (var _ in files) count++;
            return count;
        }


        public override async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var response = new TrakHoundServiceAvailabilityResponse();
                response.IsAvailable = Directory.Exists(_directory);
                response.Message = !response.IsAvailable ? "Directory Not Found" : null;

                return Ok(response);
            }
            catch (Exception ex)
            {
                return InternalError<TrakHoundServiceAvailabilityResponse>(ex.Message);
            }
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlobCollection>>> ListCollections(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (Directory.Exists(_directory))
                {
                    var collectionDirectories = Directory.GetDirectories(_directory);
                    if (!collectionDirectories.IsNullOrEmpty())
                    {
                        var collections = new List<TrakHoundBlobCollection>();
                        foreach (var collectionDirectory in collectionDirectories)
                        {
                            var count = CountBlobFiles(collectionDirectory);
                            if (count > 0)
                            {
                                var collection = new TrakHoundBlobCollection();
                                collection.Name = Path.GetFileName(collectionDirectory);
                                collection.Count = count;
                                collections.Add(collection);
                            }
                        }

                        if (!collections.IsNullOrEmpty())
                        {
                            return Ok<IEnumerable<TrakHoundBlobCollection>>(collections);
                        }
                        else
                        {
                            return NotFound<IEnumerable<TrakHoundBlobCollection>>("No Collections Found");
                        }
                    }
                    else
                    {
                        return NotFound<IEnumerable<TrakHoundBlobCollection>>("No Collections Found");
                    }
                }
                else
                {
                    return NotFound<IEnumerable<TrakHoundBlobCollection>>("Directory Not Found");
                }
            }
            catch (Exception ex)
            {
                return InternalError<IEnumerable<TrakHoundBlobCollection>>(ex.Message);
            }
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, int skip, int take, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(collection))
            {
                var collectionDirectory = GetCollectionDirectory(collection);
                if (collectionDirectory == null) return BadRequest<IEnumerable<string>>("Invalid Collection");

                try
                {
                    if (Directory.Exists(collectionDirectory))
                    {
                        var files = EnumerateBlobFiles(collectionDirectory).Skip(skip).Take(take).ToList();
                        if (!files.IsNullOrEmpty())
                        {
                            var keys = new List<string>();
                            foreach (var file in files)
                            {
                                var informationPath = GetMetadataPath(file);
                                var information = await Json.ReadAsync<BlobInformation>(informationPath);
                                if (information != null && !string.IsNullOrEmpty(information.Key))
                                {
                                    keys.Add(information.Key);
                                }
                            }

                            return Ok<IEnumerable<string>>(keys);
                        }
                        else
                        {
                            return NotFound<IEnumerable<string>>("No Blobs Found");
                        }
                    }
                    else
                    {
                        return NotFound<IEnumerable<string>>("Collection Directory Not Found");
                    }
                }
                catch (Exception ex)
                {
                    return InternalError<IEnumerable<string>>(ex.Message);
                }
            }
            else
            {
                return NotFound<IEnumerable<string>>("Collection Not Specified");
            }        
        }

        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundBlob>>> List(string collection, int skip, int take, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(collection))
            {
                var collectionDirectory = GetCollectionDirectory(collection);
                if (collectionDirectory == null) return BadRequest<IEnumerable<TrakHoundBlob>>("Invalid Collection");

                try
                {
                    if (Directory.Exists(collectionDirectory))
                    {
                        var files = EnumerateBlobFiles(collectionDirectory).Skip(skip).Take(take).ToList();
                        if (!files.IsNullOrEmpty())
                        {
                            var blobs = new List<TrakHoundBlob>();
                            foreach (var file in files)
                            {
                                var fileInfo = new FileInfo(file);

                                var blobResponse = new TrakHoundBlob();
                                blobResponse.Collection = collection;
                                blobResponse.Size = fileInfo.Length;
                                blobResponse.Created = fileInfo.CreationTime.ToUnixTime();

                                var informationPath = GetMetadataPath(file);
                                var information = await Json.ReadAsync<BlobInformation>(informationPath);
                                if (information != null && !string.IsNullOrEmpty(information.Key))
                                {
                                    blobResponse.Key = information.Key;
                                    blobResponse.ContentType = information.ContentType;
                                    blobResponse.Metadata = information.Metadata;

                                    blobs.Add(blobResponse);
                                }
                            }

                            return Ok<IEnumerable<TrakHoundBlob>>(blobs);
                        }
                        else
                        {
                            return NotFound<IEnumerable<TrakHoundBlob>>("No Blobs Found");
                        }
                    }
                    else
                    {
                        return NotFound<IEnumerable<TrakHoundBlob>>("Collection Directory Not Found");
                    }
                }
                catch (Exception ex)
                {
                    return InternalError<IEnumerable<TrakHoundBlob>>(ex.Message);
                }
            }
            else
            {
                return NotFound<IEnumerable<TrakHoundBlob>>("Collection Not Specified");
            }
        }

        public async Task<TrakHoundServiceResponse<TrakHoundBlob>> Read(string collection, string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(key))
            {
                var collectionDirectory = GetCollectionDirectory(collection);
                if (collectionDirectory == null) return BadRequest<TrakHoundBlob>("Invalid Collection");

                try
                {
                    var filePath = Path.Combine(collectionDirectory, key.ToSHA256Hash());
                    if (File.Exists(filePath))
                    {
                        var fileInfo = new FileInfo(filePath);

                        var blobResponse = new TrakHoundBlob();
                        blobResponse.Collection = collection;
                        blobResponse.Size = fileInfo.Length;
                        blobResponse.Created = fileInfo.CreationTime.ToUnixTime();

                        var blobInformation = await Json.ReadAsync<BlobInformation>(GetMetadataPath(filePath));
                        if (blobInformation != null)
                        {
                            blobResponse.Key = blobInformation.Key;
                            blobResponse.ContentType = blobInformation.ContentType;
                            blobResponse.Metadata = blobInformation.Metadata;

                            return Ok(blobResponse);
                        }
                        else
                        {
                            return NotFound<TrakHoundBlob>();
                        }
                    }
                    else
                    {
                        return NotFound<TrakHoundBlob>();
                    }
                }
                catch (Exception ex)
                {
                    return InternalError<TrakHoundBlob>(ex.Message);
                }
            }
            else
            {
                return BadRequest<TrakHoundBlob>();
            }
        }

        public async Task<TrakHoundServiceResponse<Stream>> Download(string collection, string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(key))
            {
                var collectionDirectory = GetCollectionDirectory(collection);
                if (collectionDirectory == null) return BadRequest<Stream>("Invalid Collection");

                try
                {
                    var filePath = Path.Combine(collectionDirectory, key.ToSHA256Hash());
                    if (File.Exists(filePath))
                    {
                        // Allow Delete sharing so that an Upload can replace the Blob while it is being downloaded
                        var contentStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

                        return Ok<Stream>(contentStream);
                    }
                    else
                    {
                        return NotFound<Stream>();
                    }
                }
                catch (Exception ex)
                {
                    return InternalError<Stream>(ex.Message);
                }
            }
            else
            {
                return BadRequest<Stream>();
            }
        }

        public async Task<TrakHoundServiceResponse> Upload(string collection, string key, Stream content, string contentType, IReadOnlyDictionary<string, string> metadata = null, bool overwrite = true, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(contentType) && content != null)
            {
                var collectionDirectory = GetCollectionDirectory(collection);
                if (collectionDirectory == null) return BadRequest("Invalid Collection");

                var filePath = Path.Combine(collectionDirectory, key.ToSHA256Hash());
                var metadataPath = GetMetadataPath(filePath);

                // Fail early (before copying the content) when the Blob already exists
                if (!overwrite && File.Exists(filePath)) return Conflict($"Blob Already Exists : {collection} / {key}");

                // Write to unique temp files first, then move into place, so that a failed or
                // concurrent Upload never leaves a partial Blob or removes the existing one
                var tempId = Guid.NewGuid().ToString("N");
                var tempFilePath = $"{filePath}.{tempId}{TempExtension}";
                var tempMetadataPath = $"{metadataPath}.{tempId}{TempExtension}";

                try
                {
                    if (!Directory.Exists(collectionDirectory)) Directory.CreateDirectory(collectionDirectory);

                    using (var fileStream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write))
                    {
                        if (content.CanSeek) content.Seek(0, SeekOrigin.Begin);
                        await content.CopyToAsync(fileStream, cancellationToken);
                        await fileStream.FlushAsync(cancellationToken);
                    }

                    var blobInformation = new BlobInformation();
                    blobInformation.Key = key;
                    blobInformation.ContentType = contentType;
                    blobInformation.Metadata = metadata?.ToDictionary(o => o.Key, o => o.Value);

                    var blobInformationJson = Json.Convert(blobInformation, indented: true);
                    if (string.IsNullOrEmpty(blobInformationJson)) return InternalError("Error Serializing Blob Metadata");

                    await File.WriteAllTextAsync(tempMetadataPath, blobInformationJson, cancellationToken);

                    if (overwrite)
                    {
                        ReplaceFile(tempFilePath, filePath);
                    }
                    else
                    {
                        // Move without overwriting so that a concurrent Upload that created the Blob after the check above is not replaced
                        try
                        {
                            File.Move(tempFilePath, filePath);
                        }
                        catch (IOException) when (File.Exists(filePath))
                        {
                            return Conflict($"Blob Already Exists : {collection} / {key}");
                        }
                    }

                    ReplaceFile(tempMetadataPath, metadataPath);

                    return Ok();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return InternalError(ex.Message);
                }
                finally
                {
                    TryDeleteFile(tempFilePath);
                    TryDeleteFile(tempMetadataPath);
                }
            }
            else
            {
                return BadRequest();
            }
        }

        public async Task<TrakHoundServiceResponse> Delete(string collection, string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrEmpty(collection) && !string.IsNullOrEmpty(key))
            {
                var collectionDirectory = GetCollectionDirectory(collection);
                if (collectionDirectory == null) return BadRequest("Invalid Collection");

                try
                {
                    var filePath = Path.Combine(collectionDirectory, key.ToSHA256Hash());
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);

                        var metadataPath = GetMetadataPath(filePath);
                        if (File.Exists(metadataPath)) File.Delete(metadataPath);

                        return Ok();
                    }
                    else
                    {
                        return NotFound();
                    }
                }
                catch (Exception ex)
                {
                    return InternalError(ex.Message);
                }
            }
            else
            {
                return BadRequest();
            }
        }

        private static void ReplaceFile(string sourcePath, string destinationPath)
        {
            try
            {
                File.Move(sourcePath, destinationPath, overwrite: true);
            }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && File.Exists(destinationPath))
            {
                // The destination may be open for reading (ex. an active Download). Files are opened with FileShare.Delete,
                // so deleting releases the name immediately and the move can then complete.
                File.Delete(destinationPath);
                File.Move(sourcePath, destinationPath);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }
    }
}
