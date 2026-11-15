// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Linq;

namespace TrakHound.Licenses
{
    /// <summary>
    /// Information about a License
    /// </summary>
    public class TrakHoundLicenseInformation
    {
        /// <summary>
        /// Gets or sets the License Code
        /// </summary>
        public string LicenseCode { get; set; }

        /// <summary>
        /// Gets or sets the ID of the Product the License is for
        /// </summary>
        public string ProductId { get; set; }

        /// <summary>
        /// Gets or sets the ID of the Device the License is for
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// Gets or sets a Description of the License (ex. which order or site it is for)
        /// </summary>
        /// <remarks>
        /// A label only: it isn't part of the signed License Code, so anyone can change it. Never use it to decide what is licensed
        /// </remarks>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the number of Assignments currently using the License
        /// </summary>
        public int AssignmentCount { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of Assignments allowed for the License
        /// </summary>
        public int AssignmentLimit { get; set; }

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
        /// The longest Description that is kept
        /// </summary>
        public const int MaxDescriptionLength = 200;

        /// <summary>
        /// A Description as it is kept: one trimmed line of up to MaxDescriptionLength characters, or null if it is empty
        /// </summary>
        public static string NormalizeDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return null;

            // Line breaks (and other control characters) become spaces, so it shows on one line
            var chars = description.Trim().Select(c => char.IsControl(c) ? ' ' : c).ToArray();
            var normalized = new string(chars);
            if (normalized.Length > MaxDescriptionLength) normalized = normalized.Substring(0, MaxDescriptionLength).TrimEnd();

            return normalized;
        }
    }
}
