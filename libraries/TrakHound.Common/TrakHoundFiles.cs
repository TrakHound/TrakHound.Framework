// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace TrakHound
{
    /// <summary>
    /// Helpers for copying, moving, zipping, and clearing files and directories
    /// </summary>
    public static class TrakHoundFiles
    {
        /// <summary>
        /// Copies all files and subdirectories from the source directory to the destination directory, overwriting existing files and skipping paths that match any of the ignore patterns (Regex)
        /// </summary>
        public static void Copy(string sourceDirectory, string destinationDirectory, IEnumerable<string> ignorePatterns = null)
        {
            if (!string.IsNullOrEmpty(sourceDirectory) && !string.IsNullOrEmpty(destinationDirectory))
            {
                try
                {
                    if (Directory.Exists(sourceDirectory))
                    {
                        // Create Directories
                        var directories = Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories);
                        foreach (var directory in directories)
                        {
                            var valid = IsValid(directory, ignorePatterns);
                            if (valid)
                            {
                                var relativeDirectoryPath = Path.GetRelativePath(sourceDirectory, directory);
                                var destinationPath = Path.Combine(destinationDirectory, relativeDirectoryPath);

                                if (!Directory.Exists(destinationPath)) Directory.CreateDirectory(destinationPath);
                            }
                        }

                        // Copy Files
                        var files = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            var valid = IsValid(file, ignorePatterns);
                            if (valid)
                            {
                                var relativePath = Path.GetRelativePath(sourceDirectory, file);
                                var destinationPath = Path.Combine(destinationDirectory, relativePath);

                                File.Copy(file, destinationPath, true);
                            }
                        }
                    }
                    else
                    {
                        // DEBUG
                        Console.WriteLine($"Files.Copy() : {sourceDirectory} : Directory Does Not Exist");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Files.Copy() : {sourceDirectory} : {destinationDirectory} : {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Moves all files from the source directory to the destination directory (recreating subdirectories), overwriting existing files and skipping paths that match any of the ignore patterns (Regex)
        /// </summary>
        public static void Move(string sourceDirectory, string destinationDirectory, IEnumerable<string> ignorePatterns = null)
        {
            if (!string.IsNullOrEmpty(sourceDirectory) && !string.IsNullOrEmpty(destinationDirectory))
            {
                try
                {
                    // Create Directories
                    var directories = Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories);
                    foreach (var directory in directories)
                    {
                        var valid = IsValid(directory, ignorePatterns);
                        if (valid)
                        {
                            var relativeDirectoryPath = Path.GetRelativePath(sourceDirectory, directory);
                            var destinationPath = Path.Combine(destinationDirectory, relativeDirectoryPath);

                            if (!Directory.Exists(destinationPath)) Directory.CreateDirectory(destinationPath);
                        }
                    }

                    // Move Files
                    var files = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var valid = IsValid(file, ignorePatterns);
                        if (valid)
                        {
                            var relativePath = Path.GetRelativePath(sourceDirectory, file);
                            var destinationPath = Path.Combine(destinationDirectory, relativePath);

                            File.Move(file, destinationPath, true);
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Creates a Zip archive of all files in the specified directory and returns its bytes (null on error)
        /// </summary>
        public static byte[] Zip(string sourcePath)
        {
            if (!string.IsNullOrEmpty(sourcePath))
            {
                try
                {
                    using (var outputStream = new MemoryStream())
                    {
                        using (var archive = new ZipArchive(outputStream, ZipArchiveMode.Create))
                        {                      
                            // Add Source files from Directory
                            var files = Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories);
                            foreach (var file in files)
                            {
                                // Get path Relative to Archive root
                                var entry = Path.GetRelativePath(sourcePath, file);

                                // Add Entry to Archive
                                archive.CreateEntryFromFile(file, entry);
                            }
                        }

                        return outputStream.ToArray();
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Creates a Zip archive of all files in the source directory and writes it to the destination file path
        /// </summary>
        public static void Zip(string sourcePath, string destinationPath)
        {
            var bytes = Zip(sourcePath);
            if (bytes != null)
            {
                try
                {
                    File.WriteAllBytes(destinationPath, bytes);
                }
                catch { }
            }
        }

		/// <summary>
		/// Extracts the Zip archive bytes to the destination directory, overwriting existing files
		/// </summary>
		public static void Unzip(byte[] archivedBytes, string destinationPath)
		{
			if (archivedBytes != null && !string.IsNullOrEmpty(destinationPath))
			{
				try
				{
					if (!Directory.Exists(destinationPath)) Directory.CreateDirectory(destinationPath);

					using (var inputStream = new MemoryStream(archivedBytes))
					{
						using (var archive = new ZipArchive(inputStream, ZipArchiveMode.Read))
						{
							archive.ExtractToDirectory(destinationPath, true);
						}
					}
				}
				catch { }
			}
		}


		/// <summary>
		/// Deletes all files in the specified directory (subdirectories are not removed)
		/// </summary>
		public static void Clear(string directory)
        {
            if (!string.IsNullOrEmpty(directory))
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        var files = Directory.GetFiles(directory);
                        if (!files.IsNullOrEmpty())
                        {
                            foreach (var file in files)
                            {
                                try
                                {
                                    File.Delete(file);
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }
        }



        private static bool IsValid(string path, IEnumerable<string> ignorePatterns = null)
        {
            var valid = true;
            if (!ignorePatterns.IsNullOrEmpty())
            {
                foreach (var pattern in ignorePatterns)
                {
                    try
                    {
                        var regex = new Regex(pattern);
                        if (regex.Match(path).Success) return false;
                    }
                    catch { }
                }
            }

            return valid;
        }
    }
}
