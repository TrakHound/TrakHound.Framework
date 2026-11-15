// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Deployments;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// Files that belong to this Instance only, kept in the Profile's '.local' folder so Deployments, Backups and Restores never copy or remove them :
    /// the keys that protect Admin sign-ins (Data Protection) and the revoked Admin sessions
    /// </summary>
    public static class TrakHoundInstanceLocalFiles
    {
        public const string DataProtectionDirectory = "data-protection";
        public const string RevokedSessionsFilename = "admin-sessions-revoked.json";

        // Where earlier versions kept them (the 'security' Configuration Category, which Deployments copy)
        private const string _previousDirectory = "security";


        public static string GetDirectory(string profilePath) => Path.Combine(profilePath, TrakHoundDeployment.LocalDirectory);

        public static string GetDataProtectionPath(string profilePath) => Path.Combine(GetDirectory(profilePath), DataProtectionDirectory);

        public static string GetRevokedSessionsPath(string profilePath) => Path.Combine(GetDirectory(profilePath), RevokedSessionsFilename);


        /// <summary>
        /// Moves the files from where earlier versions kept them (once, so sign-ins and revoked sessions are kept). Returns what was moved
        /// </summary>
        public static List<string> MoveFromPreviousLocation(string profilePath)
        {
            var moved = new List<string>();
            var previous = Path.Combine(profilePath, _previousDirectory);

            var previousKeys = Path.Combine(previous, DataProtectionDirectory);
            var keys = GetDataProtectionPath(profilePath);
            if (Directory.Exists(previousKeys) && !Directory.Exists(keys))
            {
                Directory.CreateDirectory(GetDirectory(profilePath));
                Directory.Move(previousKeys, keys);
                moved.Add(DataProtectionDirectory);
            }

            var previousRevoked = Path.Combine(previous, RevokedSessionsFilename);
            var revoked = GetRevokedSessionsPath(profilePath);
            if (File.Exists(previousRevoked) && !File.Exists(revoked))
            {
                Directory.CreateDirectory(GetDirectory(profilePath));
                File.Move(previousRevoked, revoked);
                moved.Add(RevokedSessionsFilename);
            }

            return moved;
        }
    }
}
