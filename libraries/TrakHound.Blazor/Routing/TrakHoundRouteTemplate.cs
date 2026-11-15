// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Globalization;

namespace TrakHound.Blazor.Routing
{
    /// <summary>
    /// A Page's @page Template, matched the way Blazor's Router matches it : constraints ({id:int}), optional ({id?})
    /// and catch-all ({*path}) Parameters, with the Route Values URL decoded. Literal Segments are matched case-sensitively
    /// (as they were before), so a differently cased Path (ex. "/_ADMIN") never reaches a Page
    /// </summary>
    public class TrakHoundRouteTemplate
    {
        private readonly Segment[] _segments;


        public string Template { get; }

        /// <summary>
        /// The names of the Template's Parameters (without constraints or '?')
        /// </summary>
        public IReadOnlyList<string> ParameterNames { get; }


        private enum SegmentKind
        {
            Literal = 0,
            ConstrainedParameter = 1,
            Parameter = 2,
            OptionalParameter = 3,
            CatchAll = 4
        }

        private class Segment
        {
            public SegmentKind Kind { get; init; }
            public string Value { get; init; }
            public string[] Constraints { get; init; }
            public bool IsOptional { get; init; }
        }


        private TrakHoundRouteTemplate(string template, Segment[] segments)
        {
            Template = template;
            _segments = segments;
            ParameterNames = segments.Where(o => o.Kind != SegmentKind.Literal).Select(o => o.Value).ToArray();
        }

        public static TrakHoundRouteTemplate Parse(string template)
        {
            var segments = new List<Segment>();

            if (!string.IsNullOrEmpty(template))
            {
                foreach (var part in template.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Length > 2 && part[0] == '{' && part[part.Length - 1] == '}')
                    {
                        var text = part.Substring(1, part.Length - 2);

                        var catchAll = text.StartsWith('*');
                        if (catchAll) text = text.Substring(1);

                        var optional = text.EndsWith('?');
                        if (optional) text = text.Substring(0, text.Length - 1);

                        var parts = text.Split(':');
                        var constraints = parts.Skip(1).Where(o => !string.IsNullOrEmpty(o)).ToArray();

                        SegmentKind kind;
                        if (catchAll) kind = SegmentKind.CatchAll;
                        else if (optional) kind = SegmentKind.OptionalParameter;
                        else if (constraints.Length > 0) kind = SegmentKind.ConstrainedParameter;
                        else kind = SegmentKind.Parameter;

                        segments.Add(new Segment { Kind = kind, Value = parts[0], Constraints = constraints, IsOptional = optional || catchAll });
                    }
                    else
                    {
                        segments.Add(new Segment { Kind = SegmentKind.Literal, Value = part, Constraints = Array.Empty<string>() });
                    }
                }
            }

            return new TrakHoundRouteTemplate(template, segments.ToArray());
        }

        /// <summary>
        /// Matches the Path (without its query string) and returns the decoded Route Values (keyed case-insensitively)
        /// </summary>
        public bool TryMatch(string path, out Dictionary<string, string> values)
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var pathSegments = (path ?? string.Empty).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            var i = 0;
            foreach (var segment in _segments)
            {
                if (segment.Kind == SegmentKind.CatchAll)
                {
                    if (i < pathSegments.Length)
                    {
                        var value = string.Join('/', pathSegments.Skip(i).Select(Decode));
                        if (!IsMatch(segment.Constraints, value)) return false;

                        values[segment.Value] = value;
                    }

                    i = pathSegments.Length;
                    break;
                }

                if (i >= pathSegments.Length)
                {
                    // Only Optional Parameters can be left over
                    if (segment.IsOptional) continue;
                    return false;
                }

                if (segment.Kind == SegmentKind.Literal)
                {
                    if (!string.Equals(segment.Value, pathSegments[i], StringComparison.Ordinal)) return false;
                }
                else
                {
                    var value = Decode(pathSegments[i]);
                    if (!IsMatch(segment.Constraints, value)) return false;

                    values[segment.Value] = value;
                }

                i++;
            }

            return i == pathSegments.Length;
        }

        private static string Decode(string value)
        {
            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch
            {
                return value;
            }
        }

        /// <summary>
        /// The Route Constraints that Blazor supports. An unknown Constraint doesn't restrict the value
        /// </summary>
        private static bool IsMatch(string[] constraints, string value)
        {
            foreach (var constraint in constraints)
            {
                var culture = CultureInfo.InvariantCulture;

                var isMatch = constraint.ToLowerInvariant() switch
                {
                    "bool" => bool.TryParse(value, out _),
                    "datetime" => DateTime.TryParse(value, culture, DateTimeStyles.None, out _),
                    "decimal" => decimal.TryParse(value, NumberStyles.Number, culture, out _),
                    "double" => double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, culture, out _),
                    "float" => float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, culture, out _),
                    "guid" => Guid.TryParse(value, out _),
                    "int" => int.TryParse(value, NumberStyles.Integer, culture, out _),
                    "long" => long.TryParse(value, NumberStyles.Integer, culture, out _),
                    "nonfile" => !IsFileName(value),
                    _ => true
                };

                if (!isMatch) return false;
            }

            return true;
        }

        private static bool IsFileName(string value)
        {
            var lastSegment = value.Substring(value.LastIndexOf('/') + 1);
            var dotIndex = lastSegment.LastIndexOf('.');
            return dotIndex > 0 && dotIndex < lastSegment.Length - 1;
        }


        /// <summary>
        /// Orders Templates the way they are matched : Templates without Parameters first, then at the first Segment that differs,
        /// a literal before a constrained Parameter, before a Parameter, before an optional Parameter, before a catch-all
        /// (so "/orders/{id:int}" is matched before "/orders/{name}")
        /// </summary>
        public class Comparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                if (x == y) return 0;
                if (x == null) return 1;
                if (y == null) return -1;

                if (x.Contains('{') && !y.Contains('{')) return 1;
                if (!x.Contains('{') && y.Contains('{')) return -1;

                var xSegments = Parse(x)._segments;
                var ySegments = Parse(y)._segments;
                for (var i = 0; i < Math.Min(xSegments.Length, ySegments.Length); i++)
                {
                    var xSegment = xSegments[i];
                    var ySegment = ySegments[i];

                    if (xSegment.Kind != ySegment.Kind) return xSegment.Kind.CompareTo(ySegment.Kind);
                    if (xSegment.Kind == SegmentKind.Literal && !string.Equals(xSegment.Value, ySegment.Value, StringComparison.OrdinalIgnoreCase)) break;
                }

                return string.CompareOrdinal(x, y);
            }
        }
    }
}
