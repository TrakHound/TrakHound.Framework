// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Security.Cryptography;
using System.Text;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// How the Instance hashes the Passwords it stores (the Admin User and the built in Users Identity Provider): PBKDF2-HMAC-SHA256 with a random Salt
    /// </summary>
    public static class TrakHoundPasswordHash
    {
        public const int Iterations = 600000; // OWASP recommendation for PBKDF2-HMAC-SHA256
        private const int _saltLength = 16;
        private const int _hashLength = 32;


        public static string CreateSalt()
        {
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(_saltLength));
        }

        /// <summary>
        /// The hex encoded Hash of the Password (null if the Password or Salt is null)
        /// </summary>
        public static string Hash(string password, string salt, int iterations)
        {
            if (password == null || salt == null || iterations < 1) return null;

            var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(salt), iterations, HashAlgorithmName.SHA256, _hashLength);
            return Convert.ToHexStringLower(hash);
        }

        /// <summary>
        /// Fixed time compare, so a Hash can't be probed by timing
        /// </summary>
        public static bool IsMatch(string hash, string expectedHash)
        {
            return hash != null && expectedHash != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hash), Encoding.UTF8.GetBytes(expectedHash));
        }
    }
}
