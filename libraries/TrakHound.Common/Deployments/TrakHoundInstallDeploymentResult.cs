// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Deployments
{
    /// <summary>
    /// The result of Installing a Deployment
    /// </summary>
    public struct TrakHoundInstallDeploymentResult
    {
        /// <summary>
        /// True if the Deployment was installed successfully
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Informational messages written while installing (may be null)
        /// </summary>
        public string[] Messages { get; }

        /// <summary>
        /// Errors that occurred while installing (may be null)
        /// </summary>
        public string[] Errors { get; }

        /// <summary>
        /// The Backup created before installing (null if no Backup was created)
        /// </summary>
        public string BackupPath { get; }


        /// <summary>
        /// Creates a new Install Deployment Result
        /// </summary>
        public TrakHoundInstallDeploymentResult(bool success, string[] messages = null, string[] errors = null, string backupPath = null)
        {
            Success = success;
            Messages = messages;
            Errors = errors;
            BackupPath = backupPath;
        }
    }
}
