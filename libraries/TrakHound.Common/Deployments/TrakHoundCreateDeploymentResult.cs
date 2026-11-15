// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Deployments
{
    /// <summary>
    /// The result of Creating a Deployment
    /// </summary>
    public struct TrakHoundCreateDeploymentResult
    {
        /// <summary>
        /// True if the Deployment was created successfully
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// The Manifest of the Configurations and Packages included in the Deployment
        /// </summary>
        public TrakHoundDeploymentManifest Manifest { get; }

        /// <summary>
        /// The contents of the Deployment file (.thd archive)
        /// </summary>
        public byte[] Content { get; }

        /// <summary>
        /// Informational messages written while creating (may be null)
        /// </summary>
        public string[] Messages { get; }

        /// <summary>
        /// Errors that occurred while creating (may be null)
        /// </summary>
        public string[] Errors { get; }


        /// <summary>
        /// Creates a new Create Deployment Result
        /// </summary>
        public TrakHoundCreateDeploymentResult(bool success, TrakHoundDeploymentManifest manifest, byte[] content, string[] messages = null, string[] errors = null)
        {
            Success = success;
            Manifest = manifest;
            Content = content;
            Messages = messages;
            Errors = errors;
        }
    }
}
