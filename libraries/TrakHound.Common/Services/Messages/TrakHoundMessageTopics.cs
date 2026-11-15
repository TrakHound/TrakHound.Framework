// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services.Messages
{
    /// <summary>
    /// Helper methods for working with Message Topics and MQTT Topic Filters
    /// </summary>
    public static class TrakHoundMessageTopics
    {
        /// <summary>
        /// Returns true if <paramref name="topic"/> matches the MQTT Topic Filter <paramref name="pattern"/>.
        /// "+" matches exactly one level, "#" (only valid as the last level) matches the parent level and any number of
        /// child levels, and Topics starting with "$" are not matched by a leading wildcard.
        /// </summary>
        public static bool IsMatch(string pattern, string topic)
        {
            if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(topic)) return false;
            if (pattern == topic) return true;

            // Topics starting with '$' (ex. $SYS) are not matched by Filters starting with a wildcard
            if (topic[0] == '$' && (pattern[0] == '+' || pattern[0] == '#')) return false;

            var patternLevels = pattern.Split('/');
            var topicLevels = topic.Split('/');

            for (var i = 0; i < patternLevels.Length; i++)
            {
                var patternLevel = patternLevels[i];

                if (patternLevel == "#")
                {
                    // Multi-level wildcard must be the last level ("a/#" matches "a", "a/b", "a/b/c")
                    return i == patternLevels.Length - 1;
                }

                if (i >= topicLevels.Length) return false;

                if (patternLevel != "+" && patternLevel != topicLevels[i]) return false;
            }

            return patternLevels.Length == topicLevels.Length;
        }

        /// <summary>
        /// Checks the Topic and QoS a Publish Stream publishes with. Returns why they are invalid (null if they are valid).
        /// The Topic can't contain CR or LF because they delimit the Message frame; the Message Service checks the rest when it publishes
        /// </summary>
        public static string GetPublishError(string topic, int qos, bool requireTopic = true)
        {
            if (string.IsNullOrEmpty(topic))
            {
                if (requireTopic) return "No Topic Specified";
            }
            else if (topic.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                return "Invalid Topic : Can't contain CR or LF";
            }

            if (qos < 0 || qos > 2) return $"Invalid QoS ({qos}) : Must be 0, 1, or 2";

            return null;
        }
    }
}
