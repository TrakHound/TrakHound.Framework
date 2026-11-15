// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Licenses
{
    /// <summary>
    /// Information about a License Assignment
    /// </summary>
    public class TrakHoundLicenseAssignmentInformation
    {
        /// <summary>
        /// Gets or sets the unique ID of the Assignment
        /// </summary>
        public string AssignmentId { get; set; }

        /// <summary>
        /// Gets or sets the ID of the Product the Assignment is for
        /// </summary>
        public string ProductId { get; set; }

        /// <summary>
        /// Gets or sets the ID of the Device the Assignment is for
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// Gets or sets the License Code currently assigned to the Assignment
        /// </summary>
        public string LicenseCode { get; set; }

        /// <summary>
        /// Gets or sets a Description of the Assignment
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when the Assignment was created
        /// </summary>
        public DateTime Created { get; set; }

        /// <summary>
        /// Gets or sets the timestamp when a License was assigned (null if no License is assigned)
        /// </summary>
        public DateTime? Assigned { get; set; }
    }
}
