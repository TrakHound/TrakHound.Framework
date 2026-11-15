// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.IO;

namespace TrakHound.Instances
{
    /// <summary>
    /// The Instance's ID, kept in its own file in the Configuration Profile ("instance.id"). It isn't one of the Instance Settings, so a deployment
    /// that carries the settings to many devices doesn't give them all the same ID (Deployments and Backups leave the file out)
    /// </summary>
    public static class TrakHoundInstanceIdentity
    {
        public const string Filename = "instance.id";


        public static string GetPath(string profileDirectory) => Path.Combine(profileDirectory, Filename);

        /// <summary>
        /// Sets the Instance ID from the file in the Profile directory. When there is no file yet, the ID from a settings file saved before
        /// it moved (or a new ID) is used, and written to the file. Returns the ID
        /// </summary>
        public static string Load(string profileDirectory, TrakHoundInstanceConfiguration configuration)
        {
            var path = GetPath(profileDirectory);

            var id = File.Exists(path) ? File.ReadAllText(path).Trim() : null;
            if (string.IsNullOrEmpty(id))
            {
                id = !string.IsNullOrEmpty(configuration.FileInstanceId) ? configuration.FileInstanceId : configuration.InstanceId;
                if (string.IsNullOrEmpty(id)) id = new TrakHoundInstanceConfiguration().InstanceId;

                Directory.CreateDirectory(profileDirectory);
                File.WriteAllText(path, id);
            }

            configuration.InstanceId = id;
            return id;
        }
    }
}
