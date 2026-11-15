// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Security.Cryptography;

namespace TrakHound.Licenses
{
    /// <summary>
    /// Issues License Codes signed with a Publisher's private key. The key is the only secret: whoever has it can issue any License for the Publisher's Products
    /// </summary>
    public sealed class TrakHoundLicenseSigner : IDisposable
    {
        private const string _p256Oid = "1.2.840.10045.3.1.7";

        private readonly ECDsa _key;


        /// <summary>
        /// ID of the signing key, written into each License Code (the first byte of the SHA-256 of its public key)
        /// </summary>
        public byte KeyId { get; }


        private TrakHoundLicenseSigner(ECDsa key)
        {
            _key = key;
            KeyId = TrakHoundLicenseCode.GetKeyId(key.ExportSubjectPublicKeyInfo());
        }

        public void Dispose() => _key.Dispose();


        /// <summary>
        /// Create a signer with a new key
        /// </summary>
        public static TrakHoundLicenseSigner Generate() => new TrakHoundLicenseSigner(ECDsa.Create(ECCurve.NamedCurves.nistP256));

        /// <summary>
        /// Create a signer from a private key in PEM format. Throws if the value isn't a P-256 private key
        /// </summary>
        public static TrakHoundLicenseSigner FromPem(string privateKeyPem)
        {
            if (string.IsNullOrWhiteSpace(privateKeyPem)) throw new ArgumentException("No signing key was specified");

            // Importing a public key also succeeds, but it can't sign
            if (privateKeyPem.Contains("PUBLIC KEY")) throw new ArgumentException("This is a public key. Sign with the private key file instead");

            var key = ECDsa.Create();
            try
            {
                key.ImportFromPem(privateKeyPem);
                if (key.ExportParameters(false).Curve.Oid?.Value != _p256Oid) throw new ArgumentException("The signing key must be an ECDSA P-256 key");

                return new TrakHoundLicenseSigner(key);
            }
            catch
            {
                key.Dispose();
                throw;
            }
        }

        public string ExportPrivateKeyPem() => _key.ExportPkcs8PrivateKeyPem();

        /// <summary>
        /// The public key to embed in the Modules that check these Licenses
        /// </summary>
        public string ExportPublicKeyPem() => _key.ExportSubjectPublicKeyInfoPem();


        /// <summary>
        /// Issue a License for one Device
        /// </summary>
        /// <param name="productId">The Product the License is for</param>
        /// <param name="deviceId">The Device ID shown on the Instance's Licenses page</param>
        /// <param name="assignmentLimit">How many Assignments can use the License (1 to 65,535)</param>
        /// <param name="expiration">When the License expires (UTC, to the second), or null for a License that doesn't expire</param>
        public string Sign(string productId, string deviceId, int assignmentLimit, DateTime? expiration)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("No Device ID was specified. Use SignForAnyDevice to issue a License that works on any Device");

            var deviceHash = TrakHoundLicenseCode.GetDeviceHash(deviceId);
            if (deviceHash == null) throw new ArgumentException($"'{deviceId}' isn't a Device ID");

            // Reserved for "any Device" (a real Device ID gets this hash with a chance of 1 in 2^64)
            if (Array.TrueForAll(deviceHash, b => b == 0)) throw new ArgumentException($"'{deviceId}' can't be licensed");

            return Create(productId, deviceHash, assignmentLimit, expiration);
        }

        /// <summary>
        /// Issue a License that works on any Device. The Assignment Limit applies to each installation, so only issue these when that is intended
        /// </summary>
        public string SignForAnyDevice(string productId, int assignmentLimit, DateTime? expiration)
        {
            return Create(productId, new byte[8], assignmentLimit, expiration);
        }

        private string Create(string productId, byte[] deviceHash, int assignmentLimit, DateTime? expiration)
        {
            if (string.IsNullOrWhiteSpace(productId)) throw new ArgumentException("No Product ID was specified");
            if (assignmentLimit < 1 || assignmentLimit > ushort.MaxValue) throw new ArgumentException($"The Assignment Limit must be from 1 to {ushort.MaxValue}");

            DateTime expirationDate;
            if (expiration.HasValue)
            {
                // A date without a Kind (ex. parsed from "2027-01-01") is taken as UTC, not local time
                expirationDate = expiration.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(expiration.Value, DateTimeKind.Utc) : expiration.Value.ToUniversalTime();
                if (expirationDate >= TrakHoundLicenseCode.PermanentDate) throw new ArgumentException($"The Expiration must be before {TrakHoundLicenseCode.PermanentDate:yyyy-MM-dd}. Leave it out for a License that doesn't expire");
                if (expirationDate.Year < 1970) throw new ArgumentException("The Expiration must be after 1970");
            }
            else
            {
                expirationDate = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
            }

            return TrakHoundLicenseCode.Create(_key, KeyId, productId, deviceHash, assignmentLimit, expirationDate);
        }
    }
}
