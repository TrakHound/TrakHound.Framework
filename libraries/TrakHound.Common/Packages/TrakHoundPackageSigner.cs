// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// The contents of a Package's signature file: an ECDSA P-256 signature of the exact bytes of its Manifest
    /// </summary>
    public class TrakHoundPackageSignature
    {
        public const string EcdsaP256Sha256 = "ecdsa-p256-sha256";


        [JsonPropertyName("algorithm")]
        public string Algorithm { get; set; }

        /// <summary>
        /// ID of the key that signed the Package. Only used to find the key: the signature is checked with the Trusted Publisher's own key
        /// </summary>
        [JsonPropertyName("keyId")]
        public string KeyId { get; set; }

        /// <summary>
        /// The signature (Base64)
        /// </summary>
        [JsonPropertyName("signature")]
        public string Signature { get; set; }


        internal byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Signs Package Manifests with a publisher's private key
    /// </summary>
    public sealed class TrakHoundPackageSigner : IDisposable
    {
        private const string _p256Oid = "1.2.840.10045.3.1.7";

        private readonly ECDsa _key;


        /// <summary>
        /// ID of the signing key (see <see cref="GetKeyId(byte[])"/>)
        /// </summary>
        public string KeyId { get; }


        private TrakHoundPackageSigner(ECDsa key)
        {
            _key = key;
            KeyId = GetKeyId(key.ExportSubjectPublicKeyInfo());
        }

        public void Dispose() => _key.Dispose();


        /// <summary>
        /// Create a signer with a new key
        /// </summary>
        public static TrakHoundPackageSigner Generate() => new TrakHoundPackageSigner(ECDsa.Create(ECCurve.NamedCurves.nistP256));

        /// <summary>
        /// Create a signer from a private key in PEM format. Throws if the value isn't a P-256 private key
        /// </summary>
        public static TrakHoundPackageSigner FromPem(string privateKeyPem)
        {
            var key = ECDsa.Create();
            try
            {
                key.ImportFromPem(privateKeyPem);

                if (key.ExportParameters(false).Curve.Oid?.Value != _p256Oid) throw new ArgumentException("The signing key must be an ECDSA P-256 key");

                // Importing a public key also succeeds, but it can't sign
                if (privateKeyPem.Contains("PUBLIC KEY")) throw new ArgumentException("This is a public key. Sign with the private key file instead");

                return new TrakHoundPackageSigner(key);
            }
            catch
            {
                key.Dispose();
                throw;
            }
        }

        public string ExportPrivateKeyPem() => _key.ExportPkcs8PrivateKeyPem();

        public string ExportPublicKeyPem() => _key.ExportSubjectPublicKeyInfoPem();


        internal TrakHoundPackageSignature Sign(byte[] manifestBytes)
        {
            return new TrakHoundPackageSignature
            {
                Algorithm = TrakHoundPackageSignature.EcdsaP256Sha256,
                KeyId = KeyId,
                Signature = Convert.ToBase64String(_key.SignData(manifestBytes, HashAlgorithmName.SHA256))
            };
        }


        /// <summary>
        /// Get the ID of a key: the first 16 hex characters of the SHA-256 of its public key
        /// </summary>
        public static string GetKeyId(byte[] subjectPublicKeyInfo) => Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo)).Substring(0, 16);

        /// <summary>
        /// Read a P-256 public key in PEM format, returning its encoded form. Throws if the value isn't one
        /// </summary>
        public static byte[] ReadPublicKeyPem(string publicKeyPem)
        {
            using (var key = ECDsa.Create())
            {
                key.ImportFromPem(publicKeyPem);
                if (key.ExportParameters(false).Curve.Oid?.Value != _p256Oid) throw new ArgumentException("The public key must be an ECDSA P-256 key");

                return key.ExportSubjectPublicKeyInfo();
            }
        }

        internal static bool Verify(byte[] subjectPublicKeyInfo, byte[] manifestBytes, byte[] signature)
        {
            using (var key = ECDsa.Create())
            {
                key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
                return key.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256);
            }
        }
    }
}
