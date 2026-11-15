// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TrakHound.Licenses
{
    // A licensed Module compiles this file into itself (with TRAKHOUND_LICENSE_SOURCES) so a changed TrakHound.Common.dll can't skip its License check.
    // It is internal there, so it doesn't clash with TrakHound.Common's public type
    /// <summary>
    /// The contents of a License Code: what the License is for, signed by the Publisher's private key
    /// </summary>
    /// <remarks>
    /// A License Code is 88 bytes written as 141 characters of Crockford Base32 (grouped by dashes):
    /// <list type="bullet">
    /// <item>Format Version (1 byte)</item>
    /// <item>Key ID (1 byte): the first byte of the SHA-256 of the signing public key, so a Publisher can replace its key</item>
    /// <item>Product Hash (8 bytes): the first 8 bytes of the SHA-256 of the Product ID (lower case)</item>
    /// <item>Device Hash (8 bytes): the first 8 bytes of the SHA-256 of the Device ID (normalized), or all zero for any Device</item>
    /// <item>Assignment Limit (2 bytes)</item>
    /// <item>Expiration (4 bytes): seconds since 1970-01-01 UTC. 2100 or later means the License doesn't expire</item>
    /// <item>Signature (64 bytes): ECDSA P-256 / SHA-256 of a prefix and the 24 bytes above</item>
    /// </list>
    /// Only the signature makes a License Code valid: anyone can read (or change) the other fields.
    /// </remarks>
#if TRAKHOUND_LICENSE_SOURCES
    internal sealed class TrakHoundLicenseCode
#else
    public sealed class TrakHoundLicenseCode
#endif
    {
        public const byte FormatVersion = 1;

        internal const int PayloadLength = 24;
        internal const int SignatureLength = 64;
        internal const int Length = PayloadLength + SignatureLength;
        internal const int TextLength = (Length * 8 + 4) / 5;

        // Signed with the payload, so a signature made for something else (ex. a Package) is never a valid License
        private static readonly byte[] _signaturePrefix = Encoding.ASCII.GetBytes("trakhound-license\n");

        private static readonly DateTime _epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Expirations at or after this date mean the License doesn't expire
        /// </summary>
        public static readonly DateTime PermanentDate = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);


        private readonly byte[] _payload;
        private readonly byte[] _signature;


        public byte Version => _payload[0];

        public byte KeyId => _payload[1];

        public int AssignmentLimit => BinaryPrimitives.ReadUInt16BigEndian(_payload.AsSpan(18, 2));

        public DateTime ExpirationDate => _epoch.AddSeconds(BinaryPrimitives.ReadUInt32BigEndian(_payload.AsSpan(20, 4)));

        public bool IsPermanent => ExpirationDate >= PermanentDate;

        /// <summary>
        /// Whether the License is for any Device (the Device Hash is all zero)
        /// </summary>
        public bool IsAnyDevice => _payload.AsSpan(10, 8).IndexOfAnyExcept((byte)0) < 0;


        private TrakHoundLicenseCode(byte[] payload, byte[] signature)
        {
            _payload = payload;
            _signature = signature;
        }


        /// <summary>
        /// Read a License Code. Returns null if it isn't one (the signature isn't checked)
        /// </summary>
        public static TrakHoundLicenseCode Parse(string licenseCode)
        {
            var normalized = TrakHoundBase32.Normalize(licenseCode);
            if (normalized == null || normalized.Length != TextLength) return null;

            var bytes = TrakHoundBase32.Decode(normalized);
            if (bytes == null || bytes.Length != Length || bytes[0] != FormatVersion) return null;

            return new TrakHoundLicenseCode(bytes.AsSpan(0, PayloadLength).ToArray(), bytes.AsSpan(PayloadLength).ToArray());
        }

        /// <summary>
        /// The License Code in the form it is issued in (upper case, grouped by dashes). Use it to compare codes that may have been typed differently
        /// </summary>
        public static string Format(string licenseCode)
        {
            var code = Parse(licenseCode);
            return code != null ? code.ToString() : licenseCode?.Trim();
        }

        public override string ToString() => TrakHoundBase32.Group(TrakHoundBase32.Encode(_payload.Concat(_signature).ToArray()));


        public bool IsForProduct(string productId) => !string.IsNullOrWhiteSpace(productId) && _payload.AsSpan(2, 8).SequenceEqual(GetProductHash(productId));

        /// <summary>
        /// Whether the License is for the Device (or for any Device)
        /// </summary>
        public bool IsForDevice(string deviceId)
        {
            if (IsAnyDevice) return true;

            var hash = GetDeviceHash(deviceId);
            return hash != null && _payload.AsSpan(10, 8).SequenceEqual(hash);
        }

        /// <summary>
        /// Whether the code was signed by the private key matching the public key (in its encoded form)
        /// </summary>
        public bool IsSignedBy(byte[] subjectPublicKeyInfo)
        {
            if (subjectPublicKeyInfo == null || GetKeyId(subjectPublicKeyInfo) != KeyId) return false;

            try
            {
                using (var key = ECDsa.Create())
                {
                    key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
                    return key.VerifyData(GetSignedData(_payload), _signature, HashAlgorithmName.SHA256);
                }
            }
            catch (CryptographicException)
            {
                return false;
            }
        }


        /// <summary>
        /// Whether the code was signed by the private key matching the public key (PEM)
        /// </summary>
        public bool IsSignedBy(string publicKeyPem) => IsSignedBy(ReadPublicKey(publicKeyPem));

        /// <summary>
        /// The encoded form of a public key (PEM), or null if it isn't one
        /// </summary>
        internal static byte[] ReadPublicKey(string publicKeyPem)
        {
            if (string.IsNullOrWhiteSpace(publicKeyPem)) return null;

            try
            {
                using (var key = ECDsa.Create())
                {
                    key.ImportFromPem(publicKeyPem);
                    return key.ExportSubjectPublicKeyInfo();
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is CryptographicException)
            {
                return null;
            }
        }


        internal static string Create(ECDsa key, byte keyId, string productId, byte[] deviceHash, int assignmentLimit, DateTime expiration)
        {
            // The expiration is UTC (the signer converts it)
            var seconds = (long)Math.Floor((expiration - _epoch).TotalSeconds);

            var payload = new byte[PayloadLength];
            payload[0] = FormatVersion;
            payload[1] = keyId;
            GetProductHash(productId).CopyTo(payload, 2);
            deviceHash.CopyTo(payload, 10);
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(18, 2), (ushort)assignmentLimit);
            BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(20, 4), (uint)Math.Clamp(seconds, 0, uint.MaxValue));

            var signature = key.SignData(GetSignedData(payload), HashAlgorithmName.SHA256);

            return new TrakHoundLicenseCode(payload, signature).ToString();
        }

        internal static byte GetKeyId(byte[] subjectPublicKeyInfo) => SHA256.HashData(subjectPublicKeyInfo)[0];

        internal static byte[] GetProductHash(string productId) => SHA256.HashData(Encoding.UTF8.GetBytes(productId.Trim().ToLowerInvariant())).AsSpan(0, 8).ToArray();

        /// <summary>
        /// Returns null if the value isn't a Device ID
        /// </summary>
        internal static byte[] GetDeviceHash(string deviceId)
        {
            var normalized = TrakHoundDeviceId.Normalize(deviceId);
            if (string.IsNullOrEmpty(normalized)) return null;

            return SHA256.HashData(Encoding.UTF8.GetBytes(normalized)).AsSpan(0, 8).ToArray();
        }

        private static byte[] GetSignedData(byte[] payload) => _signaturePrefix.Concat(payload).ToArray();
    }
}
