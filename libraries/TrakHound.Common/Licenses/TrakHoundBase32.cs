// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text;

namespace TrakHound.Licenses
{
    /// <summary>
    /// Crockford Base32 (0-9, A-Z without I, L, O and U), used for License Codes and Device IDs because it reads back
    /// the same however it is typed: case doesn't matter, I and L read as 1, O reads as 0, and dashes and spaces are skipped
    /// </summary>
    internal static class TrakHoundBase32
    {
        private const string _alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";


        public static string Encode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return string.Empty;

            var sb = new StringBuilder((bytes.Length * 8 + 4) / 5);
            var buffer = 0;
            var bits = 0;

            foreach (var b in bytes)
            {
                buffer = (buffer << 8) | b;
                bits += 8;

                while (bits >= 5)
                {
                    sb.Append(_alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }

            // The last character is padded with zero bits
            if (bits > 0) sb.Append(_alphabet[(buffer << (5 - bits)) & 31]);

            return sb.ToString();
        }

        /// <summary>
        /// Decode a value written by Encode. Returns null if it has a character that isn't Base32 or padding bits that aren't zero
        /// </summary>
        public static byte[] Decode(string value)
        {
            if (value == null) return null;

            var normalized = Normalize(value);
            if (normalized == null) return null;

            var bytes = new byte[normalized.Length * 5 / 8];
            var buffer = 0;
            var bits = 0;
            var index = 0;

            foreach (var c in normalized)
            {
                buffer = (buffer << 5) | _alphabet.IndexOf(c);
                bits += 5;

                if (bits >= 8)
                {
                    bytes[index++] = (byte)(buffer >> (bits - 8));
                    bits -= 8;
                }

                buffer &= (1 << bits) - 1;
            }

            // A different padding would be a second spelling of the same code
            if (buffer != 0) return null;

            return bytes;
        }

        /// <summary>
        /// Upper case, without dashes and spaces, with I and L read as 1 and O as 0. Returns null if a character isn't Base32
        /// </summary>
        public static string Normalize(string value)
        {
            if (value == null) return null;

            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (ch == '-' || char.IsWhiteSpace(ch)) continue;

                var c = char.ToUpperInvariant(ch);
                if (c == 'I' || c == 'L') c = '1';
                else if (c == 'O') c = '0';

                if (_alphabet.IndexOf(c) < 0) return null;
                sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Split a value into groups (ex. ABCDE-FGHJK-MN) so it is easier to read and type
        /// </summary>
        public static string Group(string value, int size = 5)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var sb = new StringBuilder(value.Length + value.Length / size);
            for (var i = 0; i < value.Length; i++)
            {
                if (i > 0 && i % size == 0) sb.Append('-');
                sb.Append(value[i]);
            }

            return sb.ToString();
        }
    }
}
