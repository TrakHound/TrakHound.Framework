// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Licenses
{
    // A licensed Module compiles this file into itself (with TRAKHOUND_LICENSE_SOURCES) so a changed TrakHound.Common.dll can't skip its License check.
    // It is internal there, so it doesn't clash with TrakHound.Common's public type
    /// <summary>
    /// Checks License Codes offline: the signature, Product, Device and expiration
    /// </summary>
    /// <remarks>
    /// A licensed Module calls this itself, with its Publisher's public key embedded in its code (never read from configuration),
    /// so that neither a changed License Code nor a License Manager that always answers "valid" can license it.
    /// </remarks>
#if TRAKHOUND_LICENSE_SOURCES
    internal static class TrakHoundLicenseVerifier
#else
    public static class TrakHoundLicenseVerifier
#endif
    {
        /// <summary>
        /// Check a License Code for this Device
        /// </summary>
        /// <param name="publicKeyPem">The Publisher's public key (PEM)</param>
        /// <param name="productId">The Product the Module needs a License for</param>
        /// <param name="licenseCode">The License Code to check</param>
        public static TrakHoundLicenseValidationResult Verify(string publicKeyPem, string productId, string licenseCode)
        {
            return Verify(new[] { publicKeyPem }, productId, licenseCode, TrakHoundDeviceId.Get(), DateTime.UtcNow);
        }

        /// <summary>
        /// Check a License Code
        /// </summary>
        /// <param name="publicKeyPems">The Publisher's public keys (PEM). More than one while a replaced key's Licenses are still in use</param>
        /// <param name="productId">The Product the Module needs a License for</param>
        /// <param name="licenseCode">The License Code to check</param>
        /// <param name="deviceId">The Device the License must be for</param>
        /// <param name="now">The time to check the expiration against (UTC)</param>
        public static TrakHoundLicenseValidationResult Verify(IEnumerable<string> publicKeyPems, string productId, string licenseCode, string deviceId, DateTime now)
        {
            var result = new TrakHoundLicenseValidationResult();
            result.ProductId = productId;
            result.DeviceId = deviceId;

            var code = TrakHoundLicenseCode.Parse(licenseCode);
            if (code == null)
            {
                result.Message = "Not a License Code";
                return result;
            }

            result.ExpirationDate = code.ExpirationDate;
            result.IsPermanent = code.IsPermanent;
            result.IsAnyDevice = code.IsAnyDevice;
            result.AssignmentLimit = code.AssignmentLimit;

            if (!IsSigned(code, publicKeyPems))
            {
                result.Message = "The License Code wasn't issued by the Publisher";
                return result;
            }

            if (!code.IsForProduct(productId))
            {
                result.Message = $"The License isn't for Product '{productId}'";
                return result;
            }

            if (!code.IsForDevice(deviceId))
            {
                result.Message = "The License is for a different Device";
                return result;
            }

            if (!code.IsPermanent && now >= code.ExpirationDate)
            {
                result.Message = $"The License expired on {code.ExpirationDate:yyyy-MM-dd}";
                return result;
            }

            result.IsValid = true;
            return result;
        }

        /// <summary>
        /// Check the License assigned to an Assignment for this Device
        /// </summary>
        /// <param name="publicKeyPem">The Publisher's public key (PEM)</param>
        /// <param name="productId">The Product the Module needs a License for</param>
        /// <param name="licenseManager">The License Manager for the Publisher</param>
        /// <param name="assignmentId">The Assignment to check</param>
        public static TrakHoundLicenseValidationResult VerifyAssignment(string publicKeyPem, string productId, ITrakHoundLicenseManager licenseManager, string assignmentId)
        {
            var licenseCode = licenseManager?.GetAssignment(assignmentId)?.LicenseCode;
            if (string.IsNullOrEmpty(licenseCode))
            {
                return new TrakHoundLicenseValidationResult { ProductId = productId, Message = "No License is assigned" };
            }

            return Verify(publicKeyPem, productId, licenseCode);
        }


        private static bool IsSigned(TrakHoundLicenseCode code, IEnumerable<string> publicKeyPems)
        {
            if (publicKeyPems == null) return false;

            foreach (var publicKeyPem in publicKeyPems)
            {
                if (code.IsSignedBy(publicKeyPem)) return true;
            }

            return false;
        }
    }
}
