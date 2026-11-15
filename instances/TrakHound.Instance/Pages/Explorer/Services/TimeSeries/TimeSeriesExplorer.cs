// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Globalization;
using System.Text;

namespace TrakHound.Instance.Pages.Explorer.Services.TimeSeries
{
    /// <summary>
    /// What the Time Series pages calculate: the text of the Write panel, the Interval for a time range, and the lines of a chart
    /// </summary>
    public static class TimeSeriesExplorer
    {
        public const string TimeInputFormat = "yyyy-MM-ddTHH:mm:ss";

        // Long ranges are aggregated, so a chart never asks for more Rows than it can show
        public const int MaxIntervals = 300;

        // A selected Window can show more detail than the automatic one, up to the Rows that a Read can return
        public const int MaxWindowIntervals = 10000;

        public static IReadOnlyList<TimeSpan> Intervals => _intervals;

        private static readonly TimeSpan[] _intervals = new[]
        {
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
            TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30), TimeSpan.FromDays(365)
        };


        /// <summary>
        /// Gets the shortest round Interval that keeps a range within MaxIntervals
        /// </summary>
        public static TimeSpan GetInterval(TimeSpan range)
        {
            foreach (var interval in _intervals)
            {
                if (range.Ticks / interval.Ticks <= MaxIntervals) return interval;
            }

            return _intervals[^1];
        }

        /// <summary>
        /// Gets the Window that was selected, or the next longer one if the selected one would make more than MaxWindowIntervals for the range
        /// </summary>
        public static TimeSpan GetWindow(TimeSpan range, TimeSpan selected)
        {
            foreach (var interval in _intervals)
            {
                if (interval >= selected && range.Ticks / interval.Ticks <= MaxWindowIntervals) return interval;
            }

            return _intervals[^1];
        }

        public static string FormatInterval(TimeSpan interval)
        {
            if (interval.TotalDays >= 1) return $"{interval.TotalDays:0.#} d";
            if (interval.TotalHours >= 1) return $"{interval.TotalHours:0.#} h";
            if (interval.TotalMinutes >= 1) return $"{interval.TotalMinutes:0.#} min";
            return $"{interval.TotalSeconds:0.#} s";
        }

        /// <summary>
        /// Parses Tags or Fields with one name=value on each line. Returns a message if a line isn't valid
        /// </summary>
        public static string ParseLines(string text, out Dictionary<string, string> values)
        {
            values = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(text)) return null;

            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length < 1) continue;

                var index = trimmed.IndexOf('=');
                if (index < 1 || index == trimmed.Length - 1) return $"\"{trimmed}\" must be name=value";

                values[trimmed.Substring(0, index).Trim()] = trimmed.Substring(index + 1).Trim();
            }

            return null;
        }

        /// <summary>
        /// Converts the text of a Field. A number without a decimal point is a whole number, true and false are a bool, and anything else is a string.
        /// Double quotes keep text that looks like one of the others as a string
        /// </summary>
        public static object ParseFieldValue(string text)
        {
            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') return text.Substring(1, text.Length - 2);
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;
            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)) return whole;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return number;
            return text;
        }

        /// <summary>
        /// Reads a time typed as UTC. Returns null if it isn't valid
        /// </summary>
        public static DateTimeOffset? ParseTime(string text)
        {
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)) return new DateTimeOffset(time, TimeSpan.Zero);
            return null;
        }

        public static string FormatValue(object value)
        {
            switch (value)
            {
                case null: return "";
                case DateTimeOffset timestamp: return timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                case double number: return number.ToString("0.######", CultureInfo.InvariantCulture);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default: return value.ToString();
            }
        }

        public static bool IsNumber(object value) => value is double || value is long;

        /// <summary>
        /// Gets the points of each polyline of a chart, in a viewBox of width by height. A null Value breaks the line, and a line that's denser than
        /// the width keeps the lowest and highest Value at each position, so a short spike still shows
        /// </summary>
        public static List<string> GetLines(IReadOnlyList<(DateTimeOffset Time, double? Value)> points, DateTimeOffset start, DateTimeOffset stop, int width, int height, out double? min, out double? max)
        {
            min = null;
            max = null;

            foreach (var point in points)
            {
                if (point.Value == null) continue;
                if (min == null || point.Value < min) min = point.Value;
                if (max == null || point.Value > max) max = point.Value;
            }

            var lines = new List<string>();
            if (min == null) return lines;

            // Timestamps can come before Start (an Interval's start, or the last Point before the range)
            var total = Math.Max(1, (stop - start).Ticks);
            var lowest = min.Value;
            var span = max.Value - lowest;

            var line = new StringBuilder();
            var position = -1;
            double low = 0, high = 0;

            void FlushPosition()
            {
                if (position < 0) return;

                var lowY = GetY(low, lowest, span, height);
                var highY = GetY(high, lowest, span, height);
                Append(line, position, lowY);
                if (high != low) Append(line, position, highY);
            }

            void FlushLine()
            {
                FlushPosition();
                position = -1;
                if (line.Length > 0)
                {
                    // A line needs two points to draw, so a Value on its own is repeated and its round end shows as a dot
                    var text = line.ToString().TrimEnd();
                    lines.Add(text.Contains(' ') ? text : $"{text} {text}");
                }

                line.Clear();
            }

            foreach (var point in points.OrderBy(o => o.Time))
            {
                if (point.Value == null)
                {
                    FlushLine();
                    continue;
                }

                var x = (int)Math.Clamp((point.Time - start).Ticks * width / total, 0L, width);
                if (x != position)
                {
                    FlushPosition();
                    position = x;
                    low = point.Value.Value;
                    high = point.Value.Value;
                }
                else
                {
                    low = Math.Min(low, point.Value.Value);
                    high = Math.Max(high, point.Value.Value);
                }
            }

            FlushLine();
            return lines;
        }


        private static double GetY(double value, double min, double span, int height)
        {
            // A flat line sits in the middle
            if (span <= 0) return height / 2d;
            return height - (value - min) / span * height;
        }

        private static void Append(StringBuilder line, int x, double y)
        {
            line.Append(x.ToString(CultureInfo.InvariantCulture)).Append(',').Append(y.ToString("0.##", CultureInfo.InvariantCulture)).Append(' ');
        }
    }
}
