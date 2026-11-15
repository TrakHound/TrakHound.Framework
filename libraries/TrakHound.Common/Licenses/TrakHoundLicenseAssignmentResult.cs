// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Licenses
{
    /// <summary>
    /// The result of assigning a License to a License Assignment
    /// </summary>
    public struct TrakHoundLicenseAssignmentResult
    {
        /// <summary>
        /// Gets or sets whether the License was assigned successfully
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets a Message describing the result
        /// </summary>
        public string Message { get; set; }


        /// <summary>
        /// Gets or sets the ID of the Product the Assignment is for
        /// </summary>
        public string ProductId { get; set; }

        /// <summary>
        /// Gets or sets the ID of the Device the Assignment is for
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// Gets or sets the ID of the Assignment the License was assigned to
        /// </summary>
        public string AssignmentId { get; set; }
    }
}
