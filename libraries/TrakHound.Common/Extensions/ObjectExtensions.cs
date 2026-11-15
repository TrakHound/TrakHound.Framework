// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.ComponentModel;
using System.Globalization;

namespace TrakHound
{
    /// <summary>
    /// Extension methods for converting Objects (by parsing their Invariant Culture string representation)
    /// </summary>
    public static class ObjectExtensions
    {
        private const NumberStyles _doubleStyles = NumberStyles.Float | NumberStyles.AllowThousands;


        /// <summary>
        /// Returns true if the value's string representation can be parsed as a finite double (Invariant Culture, no thousands separators)
        /// </summary>
        public static bool IsNumeric(this object value)
        {
            var s = ToInvariantString(value);
            return !string.IsNullOrEmpty(s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.IsFinite(x);
        }

        /// <summary>
        /// Converts the value to a bool. Accepts bools, "true" / "false" (case-insensitive), and numbers (non-zero is true). Returns false if null or not parsable
        /// </summary>
        public static bool ToBoolean(this object o)
        {
            if (o is bool b) return b;

            var s = ToInvariantString(o);
            if (string.IsNullOrEmpty(s)) return false;
            if (bool.TryParse(s, out var x)) return x;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return n != 0 && !double.IsNaN(n);
            return false;
        }

        /// <summary>
        /// Parses the value as a byte (Invariant Culture). Returns 0 if null or not parsable
        /// </summary>
        public static byte ToByte(this object o)
        {
            if (o is byte b) return b;

            var s = ToInvariantString(o);
            if (!string.IsNullOrEmpty(s) && byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) return x;
            else return 0;
        }

        /// <summary>
        /// Parses the value as an int (Invariant Culture). Returns -1 if null or not parsable
        /// </summary>
        public static int ToInt(this object o)
        {
            if (o is int i) return i;

            var s = ToInvariantString(o);
            if (!string.IsNullOrEmpty(s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) return x;
            else return -1;
        }

        /// <summary>
        /// Returns the value unchanged
        /// </summary>
        [Obsolete("Returns the value unchanged")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static uint ToUInt(this uint o) => o;

        /// <summary>
        /// Parses the value as a uint (Invariant Culture). Returns 0 if null or not parsable
        /// </summary>
        public static uint ToUInt(this object o)
        {
            if (o is uint u) return u;

            var s = ToInvariantString(o);
            if (!string.IsNullOrEmpty(s) && uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) return x;
            else return 0;
        }

        /// <summary>
        /// Parses the value as a long (Invariant Culture). Returns -1 if null or not parsable
        /// </summary>
        public static long ToLong(this object o)
        {
            if (o is long l) return l;

            var s = ToInvariantString(o);
            if (!string.IsNullOrEmpty(s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) return x;
            else return -1;
        }

        /// <summary>
        /// Converts the long to a ulong (negative values return 0)
        /// </summary>
        public static ulong ToULong(this long o)
        {
            if (o > 0) return (ulong)o;
            else return 0;
        }

        /// <summary>
        /// Converts the double to a ulong (rounded to the nearest whole number, midpoints away from zero). Returns 0 if negative, NaN, or out of range
        /// </summary>
        public static ulong ToULong(this double o)
        {
            var x = Math.Round(o, MidpointRounding.AwayFromZero);
            if (double.IsNaN(x) || x < 0 || x >= 18446744073709551616.0) return 0; // 2^64
            return (ulong)x;
        }

        /// <summary>
        /// Parses the value as a ulong (Invariant Culture). Returns 0 if null or not parsable
        /// </summary>
        public static ulong ToULong(this object o)
        {
            if (o is ulong u) return u;

            var s = ToInvariantString(o);
            if (!string.IsNullOrEmpty(s) && ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) return x;
            else return 0;
        }

        /// <summary>
        /// Parses the value as a double (Invariant Culture). Returns -1 if null or not parsable
        /// </summary>
        public static double ToDouble(this object o)
        {
            if (o is double d) return d;

            var s = ToInvariantString(o);
            if (!string.IsNullOrEmpty(s) && double.TryParse(s, _doubleStyles, CultureInfo.InvariantCulture, out var x)) return x;
            else return -1;
        }

        /// <summary>
        /// Parses the value as a double (Invariant Culture) rounded to the specified number of decimal places (clamped to 0 to 15). Returns -1 if null or not parsable
        /// </summary>
        public static double ToDouble(this object o, int decimalPlaces)
        {
            var s = ToInvariantString(o);
            if (!string.IsNullOrEmpty(s) && double.TryParse(s, _doubleStyles, CultureInfo.InvariantCulture, out var x))
            {
                return Math.Round(x, Math.Clamp(decimalPlaces, 0, 15));
            }
            else return -1;
        }

        /// <summary>
        /// Converts the value to a Sort Order from its numeric value (1 / -1) or abbreviation ("asc" / "desc"). Defaults to Ascending
        /// </summary>
        public static TrakHoundSortOrder ToSortOrder(this object o)
        {
            if (o is TrakHoundSortOrder sortOrder) return Enum.IsDefined(sortOrder) ? sortOrder : TrakHoundSortOrder.Ascending;

            if (o != null)
            {
                if (o.IsNumeric())
                {
                    var x = o.ToDouble();
                    if (x == (int)TrakHoundSortOrder.Descending) return TrakHoundSortOrder.Descending;
                }
                else
                {
                    switch (o.ToString().ToLowerInvariant())
                    {
                        case SortOrderAbbreviations.Ascending: return TrakHoundSortOrder.Ascending;
                        case SortOrderAbbreviations.Descending: return TrakHoundSortOrder.Descending;
                    }
                }
            }

            return TrakHoundSortOrder.Ascending;
        }

        /// <summary>
        /// Serializes the Object to a JSON string
        /// </summary>
        public static string ToJson(this object o, bool indented = false)
        {
            return Json.Convert(o, null, indented);
        }

        /// <summary>
        /// Serializes the Object to UTF-8 JSON bytes (null if the Object is null)
        /// </summary>
        public static byte[] ToJsonBytes(this object o, bool indented = false)
        {
            return Json.ConvertToUtf8Bytes(o, indented);
        }

        /// <summary>
        /// Gets the number of digits before the decimal point, ignoring the sign (0 for values between -1 and 1, NaN, and Infinity)
        /// </summary>
        public static int GetDigitCount(this double num)
        {
            if (!double.IsFinite(num)) return 0;

            var x = Math.Abs(num);
            if (x < 1) return 0;

            // Correct for Log10 rounding near powers of 10
            var digits = (int)Math.Floor(Math.Log10(x)) + 1;
            if (Math.Pow(10, digits - 1) > x) digits--;
            else if (Math.Pow(10, digits) <= x) digits++;
            return digits;
        }

        /// <summary>
        /// Gets the number of digits after the decimal point in the value's shortest round-trip representation
        /// (ex. 1.25 = 2, 1.5E-05 = 6). Returns 0 for NaN and Infinity
        /// </summary>
        public static int GetDigitCountAfterDecimal(this double value)
        {
            if (!double.IsFinite(value)) return 0;

            var s = value.ToString("R", CultureInfo.InvariantCulture);

            // Split off the exponent (ex. "1.5E-05")
            var exponent = 0;
            var e = s.IndexOfAny(new[] { 'E', 'e' });
            if (e >= 0)
            {
                exponent = int.Parse(s.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                s = s.Substring(0, e);
            }

            var point = s.IndexOf('.');
            var fractionDigits = point >= 0 ? s.Length - point - 1 : 0;

            return Math.Max(0, fractionDigits - exponent);
        }

        /// <summary>
        /// Parses the value as an Enum of type T (case-insensitive). Returns default if null or not parsable
        /// </summary>
        [Obsolete("Use the ConvertEnum<T>() extension method")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static T ConvertEnum<T>(object value) where T : struct, Enum => StringFunctions.ConvertEnum<T>(value);

        /// <summary>
        /// Returns true if both byte arrays are null, or have the same length and contents
        /// </summary>
        public static bool ByteArraysEqual(byte[] b1, byte[] b2)
        {
            if (b1 == null || b2 == null) return b1 == b2;
            return b1.AsSpan().SequenceEqual(b2);
        }


        // Formats the value using the Invariant Culture so it can be parsed with the Invariant Culture (ex. a boxed 1.5 is "1.5" not "1,5")
        private static string ToInvariantString(object o)
        {
            return o switch
            {
                null => null,
                string s => s,
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => o.ToString()
            };
        }
    }
}
