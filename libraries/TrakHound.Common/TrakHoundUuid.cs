// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TrakHound
{
    /// <summary>
    /// Helpers for creating and converting Uuids (SHA256 hashes stored as 32 bytes)
    /// </summary>
    public static class TrakHoundUuid
    {
        private const byte _separator = 58;


        /// <summary>
        /// Creates a Uuid from the SHA256 hash of the parts joined with ':' (null parts are skipped)
        /// </summary>
        public static byte[] Create(params object[] parts)
        {
            if (parts != null)
            {
                var length = 0;
                string part;
                for (var i = 0; i < parts.Length; i++)
                {
                    if (parts[i] != null)
                    {
                        part = parts[i].ToString();
                        length += part.Length;
                        if (i < parts.Length - 1) length++;
                    }
                }

                var b = new byte[length];
                var x = 0;

                for (var i = 0; i < parts.Length; i++)
                {
                    if (parts[i] != null)
                    {
                        part = parts[i].ToString();
                        var partBytes = Encoding.UTF8.GetBytes(part);
                        for (var j = 0; j < part.Length; j++)
                        {
                            b[x] = partBytes[j];
                            x++;
                        }

                        if (i < parts.Length - 1)
                        {
                            b[x] = _separator;
                            x++;
                        }
                    }
                }

                return System.Security.Cryptography.SHA256.HashData(b);
            }

            return null;
        }

        /// <summary>
        /// Creates a Uuid from the SHA256 hash of the Guid bytes
        /// </summary>
        public static byte[] Create(Guid guid)
        {
            var bytes = guid.ToByteArray();
            if (bytes != null)
            {
                return SHA256.HashData(bytes);
            }

            return null;
        }

        /// <summary>
        /// Creates a Uuid from the SHA256 hash of the UTF-8 bytes of the string (null if empty)
        /// </summary>
        public static byte[] Create(string uuid)
        {
            if (!string.IsNullOrEmpty(uuid))
            {
                var bytes = Encoding.UTF8.GetBytes(uuid);
                return SHA256.HashData(bytes);
            }

            return null;
        }

        /// <summary>
        /// Converts the Uuid bytes to a lowercase hex string (null if the Uuid is null)
        /// </summary>
        public static string ToString(byte[] uuid)
        {
            if (uuid != null)
            {
                try
                {
                    return string.Concat(uuid.Select(o => o.ToString("x2")));
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Converts a hex string back to Uuid bytes (null if the string is empty or not valid hex)
        /// </summary>
        public static byte[] FromString(string uuid)
        {
            if (!string.IsNullOrEmpty(uuid))
            {
                try
                {
                    if (uuid.Length % 2 == 1) throw new Exception("The binary key cannot have an odd number of digits");

                    var a = new byte[uuid.Length >> 1];

                    for (int i = 0; i < uuid.Length >> 1; ++i)
                    {
                        a[i] = (byte)((GetHexVal(uuid[i << 1]) << 4) + (GetHexVal(uuid[(i << 1) + 1])));
                    }

                    return a;
                }
                catch { }
            }

            return null;
        }

        private static int GetHexVal(char hex)
        {
            int val = (int)hex;
            //For uppercase A-F letters:
            //return val - (val < 58 ? 48 : 55);
            //For lowercase a-f letters:
            //return val - (val < 58 ? 48 : 87);
            //Or the two combined, but a bit slower:
            return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }

        /// <summary>
        /// Returns true if both Uuids contain the same bytes
        /// </summary>
        public static bool IsEqual(byte[] x, byte[] y)
        {
            return ObjectExtensions.ByteArraysEqual(x, y);
        }

        /// <summary>
        /// Equality comparer for Uuid byte arrays (for use as Dictionary / HashSet keys)
        /// </summary>
        public class UuidComparer : IEqualityComparer<byte[]>
        {
            /// <summary>
            /// Returns true if both Uuids contain the same bytes
            /// </summary>
            public bool Equals(byte[] left, byte[] right) => IsEqual(left, right);

            /// <summary>
            /// Gets a hash code for the Uuid bytes (throws if the key is null)
            /// </summary>
            public int GetHashCode(byte[] key)
            {
                if (key == null) throw new ArgumentNullException("key");
                return key.Sum(b => b);
            }
        }
    }
}
