// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Licenses
{
    /// <summary>
    /// Interface for a License Manager used to manage Licenses and License Assignments for a specific Publisher
    /// </summary>
    public interface ITrakHoundLicenseManager
    {
        /// <summary>
        /// Gets the ID of the Publisher that this License Manager handles Licenses for
        /// </summary>
        string PublisherId { get; }


        /// <summary>
        /// Reloads the Licenses and Assignments from the underlying License source
        /// </summary>
        void Refresh();


        /// <summary>
        /// Gets the ID of the current Device
        /// </summary>
        string GetDeviceId();

        /// <summary>
        /// Gets all of the Licenses
        /// </summary>
        IEnumerable<TrakHoundLicenseInformation> GetLicenses();

        /// <summary>
        /// Gets the License with the specified License Code
        /// </summary>
        TrakHoundLicenseInformation GetLicenseByLicenseCode(string licenseCode);

        /// <summary>
        /// Gets the Licenses for the specified Product
        /// </summary>
        IEnumerable<TrakHoundLicenseInformation> GetLicensesByProduct(string productId);

        /// <summary>
        /// Gets the Licenses for the specified Device
        /// </summary>
        IEnumerable<TrakHoundLicenseInformation> GetLicensesByDevice(string deviceId);


        /// <summary>
        /// Gets the Assignment ID for the specified Product and Assignment Key
        /// </summary>
        string GetAssignmentId(string productId, string assignmentKey);

        /// <summary>
        /// Gets all of the License Assignments
        /// </summary>
        IEnumerable<TrakHoundLicenseAssignmentInformation> GetAssignments();

        /// <summary>
        /// Gets the License Assignment with the specified Assignment ID
        /// </summary>
        TrakHoundLicenseAssignmentInformation GetAssignment(string assignmentId);

        /// <summary>
        /// Gets the License Assignments for the specified Product
        /// </summary>
        IEnumerable<TrakHoundLicenseAssignmentInformation> GetAssignmentsByProduct(string productId);

        /// <summary>
        /// Gets the License Assignments for the specified Device
        /// </summary>
        IEnumerable<TrakHoundLicenseAssignmentInformation> GetAssignmentsByDevice(string deviceId);

        /// <summary>
        /// Gets the License Assignments that are assigned to the specified License Code
        /// </summary>
        IEnumerable<TrakHoundLicenseAssignmentInformation> GetAssignmentsByLicense(string licenseCode);


        /// <summary>
        /// Adds a License for the specified Product and Device using the specified License Code.
        /// Adding a License that is already here keeps its Description, unless it has none
        /// </summary>
        TrakHoundLicenseInformation AddLicense(string productId, string deviceId, string licenseCode, string description = null);

        /// <summary>
        /// Sets (or clears, with null) the Description of the License with the specified License Code. Returns the License, or null if it wasn't found
        /// </summary>
        TrakHoundLicenseInformation SetLicenseDescription(string licenseCode, string description);

        /// <summary>
        /// Renews an existing License by replacing the Original License Code with the New License Code, keeping its Assignments and Description
        /// </summary>
        TrakHoundLicenseInformation RenewLicense(string productId, string deviceId, string originalLicenseCode, string newLicenseCode);

        /// <summary>
        /// Removes the License with the specified License Code
        /// </summary>
        void RemoveLicense(string licenseCode);

        /// <summary>
        /// Removes all Licenses
        /// </summary>
        void ClearLicenses();


        /// <summary>
        /// Adds a License Assignment for the specified Product, Device, and Assignment Key
        /// </summary>
        TrakHoundLicenseAssignmentInformation AddAssignment(string productId, string deviceId, string assignmentKey, string description = null);

        /// <summary>
        /// Removes the License Assignment with the specified Assignment ID
        /// </summary>
        void RemoveAssignment(string assignmentId);


        /// <summary>
        /// Assigns the specified License Code to the specified License Assignment
        /// </summary>
        TrakHoundLicenseAssignmentResult AssignLicense(string licenseCode, string assignmentId);

        /// <summary>
        /// Removes the License from the specified License Assignment
        /// </summary>
        void UnassignLicense(string assignmentId);

        /// <summary>
        /// Removes the specified License Code from all License Assignments that it is assigned to
        /// </summary>
        void UnassignAll(string licenseCode);

        /// <summary>
        /// Removes the Licenses from all License Assignments
        /// </summary>
        void UnassignAll();


        /// <summary>
        /// Checks the Product, Device and expiration of the License assigned to the specified License Assignment.
        /// The signature isn't checked: a licensed Module checks its own License with TrakHoundLicenseVerifier
        /// </summary>
        TrakHoundLicenseValidationResult CheckAssignment(string assignmentId);

        /// <summary>
        /// Checks the Product, Device and expiration of the specified License Code.
        /// The signature isn't checked: a licensed Module checks its own License with TrakHoundLicenseVerifier
        /// </summary>
        TrakHoundLicenseValidationResult CheckLicenseCode(string licenseCode);
    }
}
