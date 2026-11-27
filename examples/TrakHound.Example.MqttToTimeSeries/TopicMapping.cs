// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TrakHound.Example.MqttToTimeSeries
{
    /// <summary>
    /// Maps MQTT Topics to Time Series Tags and a Field, from a Topic Pattern (ex. "plant/{line}/{machine}/{field}").
    /// Each level of the Pattern is one of:
    /// <list type="bullet">
    /// <item>A name in braces (ex. {machine}) : that level of the Topic is the value of a Tag with that name</item>
    /// <item>{field} : that level of the Topic is the name of the Field (ex. temperature)</item>
    /// <item>+ : any value, not used</item>
    /// <item># : any number of levels, not used (only as the last level)</item>
    /// <item>Anything else : the Topic must have that exact level</item>
    /// </list>
    /// </summary>
    public class TopicMapping
    {
        /// <summary>
        /// The Field name used when the Pattern has no {field} level
        /// </summary>
        public const string DefaultField = "value";

        private const string FieldLevel = "field";
        private const int MaxTagLength = 63;
        private const int MaxFieldLength = 60;

        private static readonly Regex _nameRegex = new Regex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
        private static readonly string[] _reservedNames = { "time", "timestamp", "quality" };

        private readonly Level[] _levels;
        private readonly bool _multiLevel;


        /// <summary>
        /// The Pattern the Mapping was created from
        /// </summary>
        public string Pattern { get; }

        /// <summary>
        /// The Topic to Subscribe to : the Pattern with each named level replaced by '+'
        /// </summary>
        public string SubscribeTopic { get; }

        /// <summary>
        /// The names of the Tags the Pattern sets, in order
        /// </summary>
        public IReadOnlyList<string> TagNames { get; }


        private TopicMapping(string pattern, Level[] levels, bool multiLevel)
        {
            Pattern = pattern;
            _levels = levels;
            _multiLevel = multiLevel;

            var subscribeLevels = levels.Select(o => o.Kind == LevelKind.Literal ? o.Value : "+").ToList();
            if (multiLevel) subscribeLevels.Add("#");
            SubscribeTopic = string.Join("/", subscribeLevels);

            TagNames = levels.Where(o => o.Kind == LevelKind.Tag).Select(o => o.Value).ToList();
        }

        /// <summary>
        /// Creates a Mapping from a Pattern. Throws an ArgumentException that says what's wrong with the Pattern
        /// </summary>
        public static TopicMapping Parse(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("The Topic Pattern is empty");

            var parts = pattern.Trim().Split('/');
            var levels = new List<Level>();
            var multiLevel = false;
            var tagNames = new HashSet<string>(StringComparer.Ordinal);
            var hasField = false;

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];

                if (part == "#")
                {
                    if (i != parts.Length - 1) throw new ArgumentException($"'#' must be the last level of the Topic Pattern ({pattern})");
                    multiLevel = true;
                }
                else if (part == "+")
                {
                    levels.Add(new Level(LevelKind.Any, null));
                }
                else if (part.StartsWith("{") && part.EndsWith("}"))
                {
                    var name = part.Substring(1, part.Length - 2);
                    if (name == FieldLevel)
                    {
                        if (hasField) throw new ArgumentException($"The Topic Pattern has more than one {{field}} level ({pattern})");
                        hasField = true;
                        levels.Add(new Level(LevelKind.Field, null));
                    }
                    else
                    {
                        if (!IsValidName(name, MaxTagLength)) throw new ArgumentException($"'{name}' isn't a valid Tag name : it must start with a letter or underscore, have only letters, numbers and underscores, have up to {MaxTagLength} characters, and not be time, timestamp or quality ({pattern})");
                        if (!tagNames.Add(name)) throw new ArgumentException($"The Topic Pattern has the Tag '{name}' more than once ({pattern})");
                        levels.Add(new Level(LevelKind.Tag, name));
                    }
                }
                else if (part.Contains('+') || part.Contains('#') || part.Contains('{') || part.Contains('}'))
                {
                    throw new ArgumentException($"'{part}' isn't a valid level of the Topic Pattern : use a whole level of '+', '#' or {{name}} ({pattern})");
                }
                else
                {
                    levels.Add(new Level(LevelKind.Literal, part));
                }
            }

            if (levels.Count == 0) throw new ArgumentException($"The Topic Pattern has no levels before '#' ({pattern})");

            return new TopicMapping(pattern.Trim(), levels.ToArray(), multiLevel);
        }

        /// <summary>
        /// Gets the Tags and the Field for a Topic. Returns false if the Topic doesn't match the Pattern, or has an empty Tag value or Field
        /// </summary>
        public bool TryMap(string topic, out Dictionary<string, string> tags, out string field)
        {
            tags = null;
            field = null;
            if (string.IsNullOrEmpty(topic)) return false;

            var parts = topic.Split('/');
            if (parts.Length < _levels.Length) return false;
            if (parts.Length > _levels.Length && !_multiLevel) return false;

            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string fieldName = null;

            for (var i = 0; i < _levels.Length; i++)
            {
                var level = _levels[i];
                var part = parts[i];

                switch (level.Kind)
                {
                    case LevelKind.Literal:
                        if (part != level.Value) return false;
                        break;

                    case LevelKind.Tag:
                        if (string.IsNullOrEmpty(part)) return false;
                        result[level.Value] = part;
                        break;

                    case LevelKind.Field:
                        fieldName = ToFieldName(part);
                        if (fieldName == null) return false;
                        break;
                }
            }

            tags = result;
            field = fieldName ?? DefaultField;
            return true;
        }

        /// <summary>
        /// A valid Field name for a level of a Topic : characters other than letters, numbers and underscores become underscores, a name
        /// that starts with a number gets an underscore first, and a reserved name (time, timestamp, quality) gets "_value" after it.
        /// Null for an empty level
        /// </summary>
        public static string ToFieldName(string level)
        {
            if (string.IsNullOrEmpty(level)) return null;

            var builder = new StringBuilder(level.Length + 1);
            foreach (var c in level)
            {
                builder.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
            }
            if (char.IsAsciiDigit(builder[0])) builder.Insert(0, '_');

            var name = builder.ToString();
            if (_reservedNames.Contains(name.ToLowerInvariant())) name += "_value";
            if (name.Length > MaxFieldLength) name = name.Substring(0, MaxFieldLength);

            return name;
        }

        private static bool IsValidName(string name, int maxLength)
        {
            return !string.IsNullOrEmpty(name)
                && name.Length <= maxLength
                && _nameRegex.IsMatch(name)
                && !_reservedNames.Contains(name.ToLowerInvariant());
        }


        private enum LevelKind { Literal, Any, Tag, Field }

        private readonly record struct Level(LevelKind Kind, string Value);
    }
}
