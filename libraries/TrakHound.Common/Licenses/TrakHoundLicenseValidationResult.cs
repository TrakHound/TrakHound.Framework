// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Licenses
{
    // A licensed Module compiles this file into itself (with TRAKHOUND_LICENSE_SOURCES) so a changed TrakHound.Common.dll can't skip its License check.
    // It is internal there, so it doesn't clash with TrakHound.Common's public type
    /// <summary>
    /// The result of validating a License or License Assignment
    /// </summary>
#if TRAKHOUND_LICENSE_SOURCES
    internal struct TrakHoundLicenseValidationResult
#else
    public struct TrakHoundLicenseValidationResult
#endif
    {
        /// <summary>
        /// Gets or sets whether the License is valid
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        /// Gets or sets a Message describing the result
        /// </summary>
        public string Message { get; set; }


        /// <summary>
        /// Gets or sets the ID of the Product the License is for
        /// </summary>
        public string ProductId { get; set; }

        /// <summary>
        /// Gets or sets the ID of the Device the License is for
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// Gets or sets the date when the License expires
        /// </summary>
        public DateTime ExpirationDate { get; set; }

        /// <summary>
        /// Gets or sets whether the License is permanent (does not expire)
        /// </summary>
        public bool IsPermanent { get; set; }

        /// <summary>
        /// Gets or sets whether the License is for any Device
        /// </summary>
        public bool IsAnyDevice { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of Assignments allowed for the License
        /// </summary>
        public int AssignmentLimit { get; set; }
    }
}
