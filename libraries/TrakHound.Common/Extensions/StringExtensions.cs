// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TrakHound.Extensions;

namespace TrakHound
{
    /// <summary>
    /// String conversion, formatting, and hashing helper methods (mostly extension methods)
    /// </summary>
    public static partial class StringFunctions
    {
        private static readonly Encoding _utf8 = Encoding.UTF8;
        private static readonly NameGenerator _nameGenerator = new NameGenerator();
        private static readonly string[] _dayNames = { "sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday" };


        [GeneratedRegex(@"^(.+)-ago$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex AgoRegex();

        // Long forms (ex. "5 minutes") and short forms (ex. "5m", "1h30m"). Longer units are listed first so "ms" isn't read as "m"
        [GeneratedRegex(@"(?<value>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>years?|months?|weeks?|days?|hours?|minutes?|milliseconds?|microseconds?|seconds?|ticks?|ms|µs|y|w|d|h|m|s)(?![a-zµ])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex DurationRegex();

        [GeneratedRegex(@"<\s*([^ >]+)[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
        private static partial Regex HtmlRegex();


        /// <summary>
        /// Parses the string as an int (Invariant Culture). Returns -1 if empty or not parsable
        /// </summary>
        public static int ToInt(this string s)
        {
            if (!string.IsNullOrEmpty(s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) return x;
            else return -1;
        }

        /// <summary>
        /// Parses the string as a long (Invariant Culture). Returns -1 if empty or not parsable
        /// </summary>
        public static long ToLong(this string s)
        {
            if (!string.IsNullOrEmpty(s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) return x;
            else return -1;
        }

        /// <summary>
        /// Parses the string as a double (Invariant Culture). Returns -1 if empty or not parsable
        /// </summary>
        public static double ToDouble(this string s)
        {
            if (!string.IsNullOrEmpty(s) && double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var x)) return x;
            else return -1;
        }

        /// <summary>
        /// Parses the string as a double (Invariant Culture) rounded to the specified number of decimal places (clamped to 0 to 15). Returns -1 if empty or not parsable
        /// </summary>
        public static double ToDouble(this string s, int decimalPlaces)
        {
            if (!string.IsNullOrEmpty(s) && double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var x))
            {
                return Math.Round(x, Math.Clamp(decimalPlaces, 0, 15));
            }
            else return -1;
        }

        /// <summary>
        /// Decodes the bytes as a UTF-8 string. Returns null if the bytes are empty or can't be decoded
        /// </summary>
        public static string GetUtf8String(byte[] bytes)
        {
            if (!bytes.IsNullOrEmpty())
            {
                try
                {
                    return Encoding.UTF8.GetString(bytes);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Decodes the bytes as a UTF-8 string. Returns null if the bytes are empty or can't be decoded
        /// </summary>
        public static string GetUtf8String(ReadOnlySpan<byte> bytes)
        {
            if (!bytes.IsEmpty)
            {
                try
                {
                    return Encoding.UTF8.GetString(bytes);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Encodes the string as UTF-8 bytes. Returns null if the string is empty
        /// </summary>
        public static byte[] ToUtf8Bytes(this string s)
        {
            if (!string.IsNullOrEmpty(s))
            {
                try
                {
                    return Encoding.UTF8.GetBytes(s);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Encodes the string as UTF-8 into the buffer and returns the number of bytes written (0 if the string is empty or the buffer is too small)
        /// </summary>
        public static int ToUtf8Bytes(this string s, byte[] buffer)
        {
            if (!string.IsNullOrEmpty(s))
            {
                try
                {
                    return Encoding.UTF8.GetBytes(s, buffer);
                }
                catch { }
            }

            return 0;
        }

        /// <summary>
        /// Parses the string as a DateTime. Supports Unix timestamps (seconds, milliseconds, or nanoseconds, detected by magnitude), date strings (Invariant Culture),
        /// relative keywords in local time (ex. "now", "today", "yesterday", "monday", "next-week", "last-month") and durations ago (ex. "5m-ago").
        /// Returns DateTime.MinValue if not parsable
        /// </summary>
        public static DateTime ToDateTime(this string s)
        {
            if (TryParseDateTime(s, DateTime.Now, DateTimeKind.Local, out var result)) return result;
            return DateTime.MinValue;
        }

        /// <summary>
        /// Parses the string as a DateTime using the same formats as ToDateTime, except that relative keywords and "-ago" values are relative to UTC Now (and returned as UTC).
        /// Returns null if not parsable
        /// </summary>
        public static DateTime? ParseDateTime(this string s)
        {
            if (TryParseDateTime(s, DateTime.UtcNow, DateTimeKind.Utc, out var result)) return result;
            return null;
        }

        private static bool TryParseDateTime(string s, DateTime now, DateTimeKind kind, out DateTime result)
        {
            result = default;
            if (string.IsNullOrEmpty(s)) return false;

            // Detect if Unix Timestamp
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                if (n > 9999999999999) result = UnixTimeExtensions.FromUnixTime(n); // Nanoseconds
                else if (n > 9999999999) result = UnixTimeExtensions.FromUnixTimeMilliseconds(n); // Milliseconds
                else result = UnixTimeExtensions.FromUnixTimeSeconds(n); // Seconds
                return true;
            }

            // Other numbers (ex. "1.5") aren't valid timestamps or dates
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return false;

            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out result)) return true;

            var today = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, kind);
            var week = GetStartOfWeek(today, DayOfWeek.Sunday); // Monday?
            var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, kind);
            var year = new DateTime(now.Year, 1, 1, 0, 0, 0, kind);

            var key = s.Trim().ToLowerInvariant();
            switch (key)
            {
                case "now": result = now; return true;
                case "today": result = today; return true;
                case "noon": result = today.AddHours(12); return true;
                case "yesterday": result = today.AddDays(-1); return true;
                case "tomorrow": result = today.AddDays(1); return true;

                case "this-week": result = week; return true;
                case "this-month": result = month; return true;
                case "this-year": result = year; return true;
                case "next-week": result = week.AddDays(7); return true;
                case "next-month": result = month.AddMonths(1); return true;
                case "next-year": result = year.AddYears(1); return true;
                case "last-week": result = week.AddDays(-7); return true;
                case "last-month": result = month.AddMonths(-1); return true;
                case "last-year": result = year.AddYears(-1); return true;
            }

            // Day of the Week (ex. "monday", "next-monday", "last-monday")
            var dayName = key;
            var weekOffset = 0;
            if (dayName.StartsWith("next-")) { dayName = dayName.Substring(5); weekOffset = 7; }
            else if (dayName.StartsWith("last-")) { dayName = dayName.Substring(5); weekOffset = -7; }

            var dayIndex = Array.IndexOf(_dayNames, dayName);
            if (dayIndex >= 0)
            {
                result = week.AddDays(weekOffset + dayIndex);
                return true;
            }

            // Duration Ago (ex. "5m-ago")
            var match = AgoRegex().Match(key);
            if (match.Success)
            {
                result = now.Add(ToTimeSpan(match.Groups[1].Value).Negate());
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the date (at midnight) of the start of the week containing the DateTime
        /// </summary>
        public static DateTime GetStartOfWeek(this DateTime dt, DayOfWeek startOfWeek)
        {
            int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
            return dt.AddDays(-1 * diff).Date;
        }


        /// <summary>
        /// Parses the string as a TimeSpan. Supports a number of nanoseconds, TimeSpan strings (ex. "01:30:00"),
        /// and durations (ex. "5 minutes", "2h 30m", "1h30m", "1.5 hours", "500ms", "2w", "day"). Months are 30 days and Years are 365 days.
        /// Returns TimeSpan.Zero if not parsable
        /// </summary>
        public static TimeSpan ToTimeSpan(this string s)
        {
            if (string.IsNullOrEmpty(s)) return TimeSpan.Zero;

            // Detect if Nanoseconds
            if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                return TimeSpan.FromTicks(n / 100);
            }

            // Other numbers (ex. "1.5") aren't valid durations
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) return TimeSpan.Zero;

            if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var x)) return x;

            switch (s.Trim().ToLowerInvariant())
            {
                case "year": return TimeSpan.FromDays(365);
                case "month": return TimeSpan.FromDays(30);
                case "week": return TimeSpan.FromDays(7);
                case "day": return TimeSpan.FromDays(1);
                case "hour": return TimeSpan.FromHours(1);
                case "minute": return TimeSpan.FromMinutes(1);
                case "second": return TimeSpan.FromSeconds(1);
            }

            var ts = TimeSpan.Zero;
            foreach (Match match in DurationRegex().Matches(s))
            {
                var value = double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
                ts = ts.Add(GetDuration(value, match.Groups["unit"].Value.ToLowerInvariant()));
            }

            return ts;
        }

        private static TimeSpan GetDuration(double value, string unit)
        {
            switch (unit)
            {
                case "y": case "year": case "years": return TimeSpan.FromDays(365 * value);
                case "month": case "months": return TimeSpan.FromDays(30 * value);
                case "w": case "week": case "weeks": return TimeSpan.FromDays(7 * value);
                case "d": case "day": case "days": return TimeSpan.FromDays(value);
                case "h": case "hour": case "hours": return TimeSpan.FromHours(value);
                case "m": case "minute": case "minutes": return TimeSpan.FromMinutes(value);
                case "s": case "second": case "seconds": return TimeSpan.FromSeconds(value);
                case "ms": case "millisecond": case "milliseconds": return TimeSpan.FromMilliseconds(value);
                case "µs": case "microsecond": case "microseconds": return TimeSpan.FromTicks((long)(10 * value));
                case "tick": case "ticks": return TimeSpan.FromTicks((long)value);
                default: return TimeSpan.Zero;
            }
        }


        /// <summary>
        /// Parses the string as a Version. Returns null if it is empty, contains no '.', or is not parsable
        /// </summary>
        public static Version ToVersion(this string s)
        {
            if (!string.IsNullOrEmpty(s) && s.Contains('.') && Version.TryParse(s, out var x)) return x;
            else return null;
        }

        /// <summary>
        /// Parses the string as a Version, ignoring any Prerelease Label or Build Metadata (ex. "1.2.3-beta" is 1.2.3). Returns null if it is empty or the rest is not parsable.
        /// Use <see cref="Extensions.VersionComparer"/> to order Version strings, since this can't tell 1.2.3-beta from 1.2.3
        /// </summary>
        public static Version ToBaseVersion(this string s)
        {
            if (string.IsNullOrEmpty(s)) return null;

            var labelIndex = s.IndexOfAny(new[] { '-', '+' });
            return (labelIndex >= 0 ? s.Substring(0, labelIndex) : s).ToVersion();
        }

        /// <summary>
        /// Converts the string to PascalCase (ex. "my_value" to "MyValue"). Each word has its first character uppercased and the rest lowercased. Returns null if empty
        /// </summary>
        public static string ToPascalCase(this string s)
        {
            return JoinWords(s, "", (word, i) => word.UppercaseFirstCharacter());
        }

        /// <summary>
        /// Converts the string to Title Case with the words separated by spaces (ex. "my_value" or "MyValue" to "My Value").
        /// Each word has its first character uppercased and the rest lowercased. Returns null if empty
        /// </summary>
        public static string ToTitleCase(this string s)
        {
            return JoinWords(s, " ", (word, i) => word.UppercaseFirstCharacter());
        }

        /// <summary>
        /// Converts the string to camelCase (ex. "my_value" to "myValue"). Returns null if empty
        /// </summary>
        public static string ToCamelCase(this string s)
        {
            return JoinWords(s, "", (word, i) => i > 0 ? word.UppercaseFirstCharacter() : word.ToLowerInvariant());
        }

        /// <summary>
        /// Converts the string to snake_case (ex. "MyValue" to "my_value", "HTTPServer" to "http_server").
        /// Strings that are all uppercase or already contain '_', '-' or '.' are only lowercased. Returns null if empty
        /// </summary>
        public static string ToSnakeCase(this string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (IsAllUppercase(s) || s.Contains('_') || s.Contains('-') || s.Contains('.')) return s.ToLowerInvariant();
            return JoinWords(s, "_", (word, i) => word.ToLowerInvariant());
        }

        /// <summary>
        /// Converts the string to snake_case. The splitOnUppercase parameter is not used
        /// </summary>
        [Obsolete("Use ToSnakeCase(string). The splitOnUppercase parameter is not used")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static string ToSnakeCase(this string s, bool splitOnUppercase) => s.ToSnakeCase();

        /// <summary>
        /// Converts the string to kebab-case (ex. "MyValue" to "my-value", "HTTPServer" to "http-server").
        /// Strings that are all uppercase or already contain '_', '-' or '.' are only lowercased. Returns null if empty
        /// </summary>
        public static string ToKebabCase(this string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (IsAllUppercase(s) || s.Contains('_') || s.Contains('-') || s.Contains('.')) return s.ToLowerInvariant();
            return JoinWords(s, "-", (word, i) => word.ToLowerInvariant());
        }

        /// <summary>
        /// Converts the string to kebab-case. The splitOnUppercase parameter is not used
        /// </summary>
        [Obsolete("Use ToKebabCase(string). The splitOnUppercase parameter is not used")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static string ToKebabCase(this string s, bool splitOnUppercase) => s.ToKebabCase();

        /// <summary>
        /// Converts the string to UPPER_SNAKE_CASE (ex. "MyValue" to "MY_VALUE", "HTTPServer" to "HTTP_SERVER"). Returns null if empty
        /// </summary>
        public static string ToUnderscoreUpper(this string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (IsAllUppercase(s)) return s.ToUpperInvariant();
            return JoinWords(s, "_", (word, i) => word.ToUpperInvariant());
        }

        /// <summary>
        /// Converts the string to UPPER_SNAKE_CASE. The splitOnUppercase parameter is not used
        /// </summary>
        [Obsolete("Use ToUnderscoreUpper(string). The splitOnUppercase parameter is not used")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static string ToUnderscoreUpper(this string s, bool splitOnUppercase) => s.ToUnderscoreUpper();

        private static string JoinWords(string s, string separator, Func<string, int, string> transform)
        {
            if (string.IsNullOrEmpty(s)) return null;

            var words = s.SplitOnWord()?.Select(o => o.Trim()).Where(o => o.Length > 0).ToArray();
            if (words.IsNullOrEmpty()) return null;

            var sb = new StringBuilder(s.Length + words.Length);
            for (var i = 0; i < words.Length; i++)
            {
                if (i > 0) sb.Append(separator);
                sb.Append(transform(words[i], i));
            }
            return sb.ToString();
        }

        private static bool IsAllUppercase(string s) => s == s.ToUpperInvariant();

        /// <summary>
        /// Splits the string into words on spaces, else underscores, else dashes, else Uppercase characters and numbers. Returns null if empty
        /// </summary>
        public static string[] SplitOnWord(this string s)
        {
            if (!string.IsNullOrEmpty(s))
            {
                if (s.Contains(' ')) return s.Split(' ');
                else if (s.Contains('_')) return s.Split('_');
                else if (s.Contains('-')) return s.Split('-');
                else return SplitOnUppercase(s);
            }

            return null;
        }

        /// <summary>
        /// Splits the string before each Uppercase word and before each number that follows a non-number.
        /// Runs of Uppercase characters are kept together as an acronym (ex. "HTTPServer" to "HTTP", "Server" and "ApiID" to "Api", "ID").
        /// An all uppercase string is not split. Returns null if empty
        /// </summary>
        public static string[] SplitOnUppercase(this string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (IsAllUppercase(s)) return new string[] { s };

            var parts = new List<string>();
            var start = 0;
            for (var i = 1; i < s.Length; i++)
            {
                var c = s[i];
                var previous = s[i - 1];

                var split = false;
                if (char.IsDigit(c) && !char.IsDigit(previous)) split = true;
                else if (char.IsUpper(c))
                {
                    // Start of a new word, or the last Uppercase character of an acronym that starts the next word (ex. the "S" in "HTTPServer")
                    split = !char.IsUpper(previous) || (i + 1 < s.Length && char.IsLower(s[i + 1]));
                }

                if (split)
                {
                    parts.Add(s.Substring(start, i - start));
                    start = i;
                }
            }
            parts.Add(s.Substring(start));

            return parts.ToArray();
        }


        /// <summary>
        /// Uppercases the first character and lowercases the rest of the string
        /// </summary>
        public static string UppercaseFirstCharacter(this string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        /// <summary>
        /// Lowercases the first character of the string (the rest is unchanged)
        /// </summary>
        public static string LowercaseFirstCharacter(this string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToLowerInvariant(s[0]) + s.Substring(1);
        }


        /// <summary>
        /// Generates a cryptographically secure random string of the specified length using uppercase letters and digits
        /// </summary>
        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return RandomNumberGenerator.GetString(chars, length);
        }

        /// <summary>
        /// Generates a random name using the NameGenerator
        /// </summary>
        public static string GetRandomName()
        {
            return _nameGenerator.GetRandomName();
        }


        /// <summary>
        /// Hashes the UTF-8 bytes of the string with the specified HashAlgorithm and XORs the hash into a 64-bit value.
        /// The HashAlgorithm is not disposed
        /// </summary>
        public static ulong GetUInt64Hash(this string text, HashAlgorithm hasher)
        {
            var bytes = hasher.ComputeHash(_utf8.GetBytes(text));
            Array.Resize(ref bytes, (bytes.Length + 7) & ~7); // Pad to a multiple of 8 bytes (ex. SHA1 creates 20 bytes)

            ulong result = 0;
            for (var i = 0; i < bytes.Length; i += 8)
            {
                result ^= BitConverter.ToUInt64(bytes, i);
            }
            return result;
        }

        /// <summary>
        /// Formats a number of bytes as a file size using the largest unit, rounded to one decimal place (ex. "1.5MB", Invariant Culture)
        /// </summary>
        public static string ToFileSize(this long byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            if (byteCount == 0)
                return "0" + suf[0];
            double bytes = Math.Abs((double)byteCount); // double so long.MinValue doesn't overflow
            int place = Math.Min(Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024))), suf.Length - 1);
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return (Math.Sign(byteCount) * num).ToString(CultureInfo.InvariantCulture) + suf[place];
        }

        /// <summary>
        /// Formats a number of bytes as a file size using the largest unit, rounded to one decimal place (ex. "1.5MB", Invariant Culture)
        /// </summary>
        public static string ToFileSize(this decimal byteCount)
        {
            var x = (long)byteCount;
            return x.ToFileSize();
        }


        /// <summary>
        /// Returns true if the string contains a matching opening and closing HTML tag pair (which may span multiple lines)
        /// </summary>
        public static bool IsHtml(this string s)
        {
            if (!string.IsNullOrEmpty(s))
            {
                try
                {
                    return HtmlRegex().IsMatch(s);
                }
                catch (RegexMatchTimeoutException) { }
            }

            return false;
        }

        /// <summary>
        /// Parses the value as an Enum of type T (case-insensitive). Returns default if null or not parsable
        /// </summary>
        public static T ConvertEnum<T>(this object value) where T : struct, Enum
        {
            if (value != null && Enum.TryParse<T>(value.ToString(), true, out var result)) return result;
            return default;
        }

        /// <summary>
        /// Parses the string as an Enum of type T (case-insensitive). Returns default if null or not parsable
        /// </summary>
        public static T ConvertEnum<T>(this string value) where T : struct, Enum
        {
            if (value != null && Enum.TryParse<T>(value, true, out var result)) return result;
            return default;
        }


        /// <summary>
        /// Formats the Version as a wildcard matching any Minor version (ex. "1.*"). Returns null if the Version is null
        /// </summary>
        public static string ToWildcardMinor(this Version version)
        {
            if (version != null)
            {
                return $"{version.Major}.*";
            }

            return null;
        }

        /// <summary>
        /// Formats the Version as a wildcard matching any Build version (ex. "1.2.*"). Returns null if the Version is null
        /// </summary>
        public static string ToWildcardBuild(this Version version)
        {
            if (version != null)
            {
                return $"{version.Major}.{version.Minor}.*";
            }

            return null;
        }

        /// <summary>
        /// Returns true if the target Version matches the match Version. In a wildcard match Version, the parts before the '*' must equal
        /// the target's parts (ex. "1.*" matches 1.x.x but not 2.0.0, "1.2.*" matches 1.2.x, "*" matches any Version), and the target's Prerelease Label is ignored
        /// (ex. "1.*" matches 1.2.3-beta). A "-*" suffix doesn't change what matches (ex. "*-*" is the same as "*", and "1.2.3-*" matches 1.2.3 and its Prereleases).
        /// It only changes which match <see cref="Packages.TrakHoundPackage.SelectVersion"/> selects. Otherwise the strings must be equal
        /// </summary>
        public static bool MatchVersion(string targetVersion, string matchVersion)
        {
            if (string.IsNullOrEmpty(targetVersion) || string.IsNullOrEmpty(matchVersion)) return false;

            if (!matchVersion.Contains('*')) return targetVersion == matchVersion;

            var target = targetVersion.ToBaseVersion();
            if (target == null) return false;

            var parts = matchVersion.EndsWith(Packages.TrakHoundPackage.PrereleaseWildcardSuffix)
                ? matchVersion.Substring(0, matchVersion.Length - Packages.TrakHoundPackage.PrereleaseWildcardSuffix.Length).Split('.')
                : matchVersion.Split('.');

            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "*") return true;

                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value)) return false;

                var targetPart = i switch
                {
                    0 => target.Major,
                    1 => target.Minor,
                    2 => target.Build,
                    3 => target.Revision,
                    _ => -1
                };
                if (targetPart != value) return false;
            }

            return true;
        }

        /// <summary>
        /// Encodes the bytes as a Base64 string. Returns null if the bytes are empty
        /// </summary>
        public static string Base64Encode(byte[] input)
        {
            if (!input.IsNullOrEmpty())
            {
                try
                {
                    return System.Convert.ToBase64String(input);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Decodes a Base64 string and returns the result as a UTF-8 string. Returns null if empty or invalid
        /// </summary>
        public static string Base64Decode(string input)
        {
            if (!string.IsNullOrEmpty(input))
            {
                try
                {
                    var bytes = System.Convert.FromBase64String(input);
                    return System.Text.Encoding.UTF8.GetString(bytes);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Converts a hexadecimal string (upper or lower case) to bytes. Returns null if the string is null, has an odd number of characters, or contains non-hexadecimal characters
        /// </summary>
        public static byte[] ConvertHexadecimalToBytes(string hex)
        {
            if (hex != null)
            {
                try
                {
                    return Convert.FromHexString(hex);
                }
                catch (FormatException) { }
            }

            return null;
        }

        /// <summary>
        /// Converts a hexadecimal string to bytes (misspelled name kept for compatibility)
        /// </summary>
        [Obsolete("Use ConvertHexadecimalToBytes")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static byte[] ConvertHexidecimalToBytes(string hex) => ConvertHexadecimalToBytes(hex);


        #region "MD5"

        /// <summary>
        /// Gets the MD5 hash of the UTF-8 bytes of the string as a lowercase hex string (null if the string is null)
        /// </summary>
        public static string ToMD5Hash(this string s)
        {
            return s != null ? Convert.ToHexStringLower(MD5.HashData(_utf8.GetBytes(s))) : null;
        }

        /// <summary>
        /// Gets the MD5 hash of the bytes as a lowercase hex string (null if the bytes are null)
        /// </summary>
        public static string ToMD5Hash(this byte[] bytes)
        {
            return bytes != null ? Convert.ToHexStringLower(MD5.HashData(bytes)) : null;
        }

        /// <summary>
        /// Formats hash bytes as a lowercase hex string (null if the bytes are null)
        /// </summary>
        public static string ToMD5HashString(this byte[] hashBytes)
        {
            return hashBytes != null ? Convert.ToHexStringLower(hashBytes) : null;
        }

        /// <summary>
        /// Gets the MD5 hash of the UTF-8 bytes of the string (null if the string is null)
        /// </summary>
        public static byte[] ToMD5HashBytes(this string s)
        {
            return s != null ? MD5.HashData(_utf8.GetBytes(s)) : null;
        }

        /// <summary>
        /// Gets the MD5 hash of the bytes (null if the bytes are null)
        /// </summary>
        public static byte[] ToMD5HashBytes(this byte[] bytes)
        {
            return bytes != null ? MD5.HashData(bytes) : null;
        }

        /// <summary>
        /// Gets a combined MD5 hash of the lines by chaining the hash of each line onto the previous hash (null if there are no lines)
        /// </summary>
        public static string ToMD5Hash(string[] lines)
        {
            return ChainHash(lines, ToMD5Hash);
        }

        /// <summary>
        /// Combines the hashes into a single MD5 hash by chaining each hash onto the previous result (null elements are skipped, null if there are no hashes)
        /// </summary>
        public static byte[] ToMD5HashBytes(byte[][] hashBytes)
        {
            return ChainHash(hashBytes, MD5.HashData);
        }

        #endregion

        #region "SHA1"

        /// <summary>
        /// Gets the SHA1 hash of the UTF-8 bytes of the string as a lowercase hex string (null if the string is null)
        /// </summary>
        public static string ToSHA1Hash(this string s)
        {
            return s != null ? Convert.ToHexStringLower(SHA1.HashData(_utf8.GetBytes(s))) : null;
        }

        /// <summary>
        /// Gets the SHA1 hash of the bytes as a lowercase hex string (null if the bytes are null)
        /// </summary>
        public static string ToSHA1Hash(this byte[] bytes)
        {
            return bytes != null ? Convert.ToHexStringLower(SHA1.HashData(bytes)) : null;
        }

        /// <summary>
        /// Formats hash bytes as a lowercase hex string (null if the bytes are null)
        /// </summary>
        public static string ToSHA1HashString(this byte[] hashBytes)
        {
            return hashBytes != null ? Convert.ToHexStringLower(hashBytes) : null;
        }

        /// <summary>
        /// Gets the SHA1 hash of the UTF-8 bytes of the string (null if the string is null)
        /// </summary>
        public static byte[] ToSHA1HashBytes(this string s)
        {
            return s != null ? SHA1.HashData(_utf8.GetBytes(s)) : null;
        }

        /// <summary>
        /// Gets the SHA1 hash of the bytes (null if the bytes are null)
        /// </summary>
        public static byte[] ToSHA1HashBytes(this byte[] bytes)
        {
            return bytes != null ? SHA1.HashData(bytes) : null;
        }

        /// <summary>
        /// Gets a combined SHA1 hash of the lines by chaining the hash of each line onto the previous hash (null if there are no lines)
        /// </summary>
        public static string ToSHA1Hash(string[] lines)
        {
            return ChainHash(lines, ToSHA1Hash);
        }

        /// <summary>
        /// Combines the hashes into a single SHA1 hash by chaining each hash onto the previous result (null elements are skipped, null if there are no hashes)
        /// </summary>
        public static byte[] ToSHA1HashBytes(byte[][] hashBytes)
        {
            return ChainHash(hashBytes, SHA1.HashData);
        }

        #endregion

        #region "SHA256"

        /// <summary>
        /// Gets the SHA256 hash of the UTF-8 bytes of the string as a lowercase hex string (null if the string is null)
        /// </summary>
        public static string ToSHA256Hash(this string s)
        {
            return s != null ? Convert.ToHexStringLower(SHA256.HashData(_utf8.GetBytes(s))) : null;
        }

        /// <summary>
        /// Gets the SHA256 hash of the bytes as a lowercase hex string (null if the bytes are null)
        /// </summary>
        public static string ToSHA256Hash(this byte[] bytes)
        {
            return bytes != null ? Convert.ToHexStringLower(SHA256.HashData(bytes)) : null;
        }

        /// <summary>
        /// Formats hash bytes as a lowercase hex string (null if the bytes are null)
        /// </summary>
        public static string ToSHA256HashString(this byte[] hashBytes)
        {
            return hashBytes != null ? Convert.ToHexStringLower(hashBytes) : null;
        }

        /// <summary>
        /// Gets the SHA256 hash of the UTF-8 bytes of the string (null if the string is null)
        /// </summary>
        public static byte[] ToSHA256HashBytes(this string s)
        {
            return s != null ? SHA256.HashData(_utf8.GetBytes(s)) : null;
        }

        /// <summary>
        /// Gets the SHA256 hash of the bytes (null if the bytes are null)
        /// </summary>
        public static byte[] ToSHA256HashBytes(this byte[] bytes)
        {
            return bytes != null ? SHA256.HashData(bytes) : null;
        }

        /// <summary>
        /// Gets a combined SHA256 hash of the lines by chaining the hash of each line onto the previous hash (null if there are no lines)
        /// </summary>
        public static string ToSHA256Hash(string[] lines)
        {
            return ChainHash(lines, ToSHA256Hash);
        }

        /// <summary>
        /// Combines the hashes into a single SHA256 hash by chaining each hash onto the previous result (null elements are skipped, null if there are no hashes)
        /// </summary>
        public static byte[] ToSHA256HashBytes(byte[][] hashBytes)
        {
            return ChainHash(hashBytes, SHA256.HashData);
        }

        #endregion

        // Hashes the first line, then for each following line hashes (previous hash + hash of the line)
        private static string ChainHash(string[] lines, Func<string, string> hash)
        {
            if (lines == null || lines.Length == 0) return null;

            var h = hash(lines[0]);
            for (var i = 1; i < lines.Length; i++)
            {
                h = hash(h + hash(lines[i]));
            }
            return h;
        }

        // Starts with the first hash, then for each following hash computes hash(previous result + hash)
        private static byte[] ChainHash(byte[][] hashBytes, Func<byte[], byte[]> hash)
        {
            if (hashBytes == null) return null;

            byte[] result = null;
            foreach (var bytes in hashBytes)
            {
                if (bytes == null) continue;

                if (result == null)
                {
                    result = bytes;
                }
                else
                {
                    var combined = new byte[result.Length + bytes.Length];
                    Array.Copy(result, 0, combined, 0, result.Length);
                    Array.Copy(bytes, 0, combined, result.Length, bytes.Length);
                    result = hash(combined);
                }
            }
            return result;
        }
    }
}
