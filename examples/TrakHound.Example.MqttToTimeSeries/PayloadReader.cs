// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Globalization;
using System.Text.Json;

namespace TrakHound.Example.MqttToTimeSeries
{
    /// <summary>
    /// Reads the value of a Message's payload : a number (ex. 71.3), true or false, or a JSON object with the value at a path (ex. {"value": 71.3})
    /// </summary>
    public static class PayloadReader
    {
        /// <summary>
        /// Reads a number (as a double) or a bool from the payload. With a JSON object, the value is the property at the path (properties
        /// separated by '.', ex. "value" or "data.temperature"). Returns false, with the reason, for anything else
        /// </summary>
        public static bool TryRead(string payload, string valuePath, out object value, out string error)
        {
            value = null;
            error = null;

            var text = payload?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                error = "The payload is empty";
                return false;
            }

            if (text.StartsWith('{'))
            {
                return TryReadJson(text, valuePath, out value, out error);
            }

            if (TryReadText(text, out value)) return true;

            error = $"The payload isn't a number, true or false, or a JSON object ({Shorten(text)})";
            return false;
        }

        private static bool TryReadText(string text, out object value)
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
            {
                value = number;
                return true;
            }

            if (bool.TryParse(text, out var flag))
            {
                value = flag;
                return true;
            }

            value = null;
            return false;
        }

        private static bool TryReadJson(string text, string valuePath, out object value, out string error)
        {
            value = null;
            error = null;

            if (string.IsNullOrWhiteSpace(valuePath))
            {
                error = "The payload is a JSON object, and no valuePath is set";
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(text);

                var element = document.RootElement;
                foreach (var name in valuePath.Split('.'))
                {
                    if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
                    {
                        error = $"The JSON payload has no '{valuePath}' property ({Shorten(text)})";
                        return false;
                    }
                }

                switch (element.ValueKind)
                {
                    case JsonValueKind.Number:
                        // Always a double, so a Field's type doesn't depend on whether its first value had a decimal point
                        value = element.GetDouble();
                        return true;

                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        value = element.GetBoolean();
                        return true;

                    case JsonValueKind.String:
                        if (TryReadText(element.GetString().Trim(), out value)) return true;
                        break;
                }

                error = $"The JSON payload's '{valuePath}' isn't a number, true or false ({Shorten(text)})";
                return false;
            }
            catch (JsonException ex)
            {
                error = $"The payload isn't valid JSON : {ex.Message}";
                return false;
            }
        }

        private static string Shorten(string text) => text.Length > 100 ? text.Substring(0, 100) + "..." : text;
    }
}
