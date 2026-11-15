// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace TrakHound.Licenses
{
    /// <summary>
    /// A set of Licenses (Product ID + License Code) read from a License File or pasted text
    /// </summary>
    /// <remarks>
    /// Accepted formats:
    /// <list type="bullet">
    /// <item>{ "publisherId": "..", "deviceId": "..", "licenses": [ { "productId": "..", "licenseCode": "..", "description": ".." } ] }</item>
    /// <item>[ { "productId": "..", "licenseCode": ".." } ]</item>
    /// <item>{ "productId": "..", "licenseCode": ".." }</item>
    /// <item>Plain text with one "ProductId LicenseCode" pair per line (separated by spaces, tabs, commas, or semicolons)</item>
    /// </list>
    /// An Entry's "description" is optional (the plain text format has none).
    /// </remarks>
    public class TrakHoundLicenseFile
    {
        /// <summary>
        /// The file extension used for License Files
        /// </summary>
        public const string FileExtension = ".json";

        private static readonly char[] _textSeparators = new[] { ' ', '\t', ',', ';' };
        private static readonly JsonDocumentOptions _jsonOptions = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };


        /// <summary>
        /// Gets or sets the default Publisher ID for the Licenses (an Entry's own Publisher ID takes precedence)
        /// </summary>
        public string PublisherId { get; set; }

        /// <summary>
        /// Gets or sets the default Device ID the Licenses were issued for (an Entry's own Device ID takes precedence)
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// Gets the Licenses that were read
        /// </summary>
        public List<TrakHoundLicenseFileEntry> Licenses { get; } = new List<TrakHoundLicenseFileEntry>();

        /// <summary>
        /// Gets the errors for anything that could not be read
        /// </summary>
        public List<string> Errors { get; } = new List<string>();


        /// <summary>
        /// Reads Licenses from JSON or plain text. Never throws; problems are added to Errors.
        /// </summary>
        public static TrakHoundLicenseFile Parse(string text)
        {
            var file = new TrakHoundLicenseFile();

            var trimmed = text?.Trim();
            if (string.IsNullOrEmpty(trimmed)) return file;

            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            {
                file.ParseJson(trimmed);
            }
            else
            {
                file.ParseText(trimmed);
            }

            // Entries inherit the file level Publisher / Device
            foreach (var entry in file.Licenses)
            {
                entry.PublisherId ??= file.PublisherId;
                entry.DeviceId ??= file.DeviceId;
            }

            return file;
        }


        /// <summary>
        /// Writes the Licenses as a License File that Parse can read back:
        /// { "publisherId": "..", "deviceId": "..", "licenses": [ { "productId": "..", "licenseCode": "..", "description": ".." } ] }
        /// </summary>
        /// <remarks>
        /// An Entry's Publisher ID and Device ID are only written when they differ from the file level ones.
        /// Set a file level Device ID when every License is for one Device, or leave it null and set one per Entry
        /// so a single file can hold the Licenses for several Devices (each Device skips the Licenses that aren't its own).
        /// </remarks>
        public string ToJson(bool indented = true)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented }))
                {
                    writer.WriteStartObject();
                    if (!string.IsNullOrEmpty(PublisherId)) writer.WriteString("publisherId", PublisherId);
                    if (!string.IsNullOrEmpty(DeviceId)) writer.WriteString("deviceId", DeviceId);

                    writer.WriteStartArray("licenses");
                    foreach (var entry in Licenses)
                    {
                        if (entry == null) continue;

                        writer.WriteStartObject();
                        if (!string.IsNullOrEmpty(entry.PublisherId) && entry.PublisherId != PublisherId) writer.WriteString("publisherId", entry.PublisherId);
                        if (!string.IsNullOrEmpty(entry.DeviceId) && !string.Equals(entry.DeviceId, DeviceId, StringComparison.OrdinalIgnoreCase)) writer.WriteString("deviceId", entry.DeviceId);
                        writer.WriteString("productId", entry.ProductId);
                        writer.WriteString("licenseCode", entry.LicenseCode);
                        var description = TrakHoundLicenseInformation.NormalizeDescription(entry.Description);
                        if (description != null) writer.WriteString("description", description);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();

                    writer.WriteEndObject();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }


        private void ParseJson(string json)
        {
            try
            {
                using (var document = JsonDocument.Parse(json, _jsonOptions))
                {
                    var root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        ReadEntries(root);
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (TryGetProperty(root, "licenses", out var licenses))
                        {
                            PublisherId = GetString(root, "publisherId");
                            DeviceId = GetString(root, "deviceId");

                            if (licenses.ValueKind == JsonValueKind.Array) ReadEntries(licenses);
                            else Errors.Add("'licenses' must be an array");
                        }
                        else
                        {
                            ReadEntry(root, 1);
                        }
                    }
                    else
                    {
                        Errors.Add("Expected a JSON object or array");
                    }
                }
            }
            catch (JsonException ex)
            {
                Errors.Add($"Invalid JSON : {ex.Message}");
            }
        }

        private void ReadEntries(JsonElement array)
        {
            var index = 1;
            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object) ReadEntry(element, index);
                else Errors.Add($"License {index} : Expected an object with 'productId' and 'licenseCode'");

                index++;
            }
        }

        private void ReadEntry(JsonElement element, int index)
        {
            var productId = GetString(element, "productId");
            var licenseCode = GetString(element, "licenseCode");

            if (string.IsNullOrEmpty(productId)) Errors.Add($"License {index} : No 'productId' was specified");
            else if (string.IsNullOrEmpty(licenseCode)) Errors.Add($"License {index} : No 'licenseCode' was specified");
            else AddEntry(new TrakHoundLicenseFileEntry
            {
                PublisherId = GetString(element, "publisherId"),
                DeviceId = GetString(element, "deviceId"),
                ProductId = productId,
                LicenseCode = licenseCode,
                Description = TrakHoundLicenseInformation.NormalizeDescription(GetString(element, "description"))
            }, $"License {index}");
        }


        private void ParseText(string text)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//")) continue;

                var parts = line.Split(_textSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    AddEntry(new TrakHoundLicenseFileEntry { ProductId = parts[0], LicenseCode = parts[1] }, $"Line {i + 1}");
                }
                else
                {
                    Errors.Add($"Line {i + 1} : Expected 'ProductId LicenseCode'");
                }
            }
        }


        private void AddEntry(TrakHoundLicenseFileEntry entry, string location)
        {
            if (Licenses.Any(o => o.LicenseCode == entry.LicenseCode))
            {
                Errors.Add($"{location} : Duplicate License Code for '{entry.ProductId}'");
            }
            else
            {
                Licenses.Add(entry);
            }
        }

        // Property names are matched case-insensitively (productId, ProductId, productid)
        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private static string GetString(JsonElement element, string name)
        {
            if (TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var s = value.GetString()?.Trim();
                return !string.IsNullOrEmpty(s) ? s : null;
            }

            return null;
        }
    }

    /// <summary>
    /// A single License read from a License File
    /// </summary>
    public class TrakHoundLicenseFileEntry
    {
        /// <summary>
        /// Gets or sets the ID of the Publisher of the Product (null to use the default Publisher)
        /// </summary>
        public string PublisherId { get; set; }

        /// <summary>
        /// Gets or sets the ID of the Device the License was issued for (null if not specified)
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// Gets or sets the ID of the Product the License is for
        /// </summary>
        public string ProductId { get; set; }

        /// <summary>
        /// Gets or sets the License Code
        /// </summary>
        public string LicenseCode { get; set; }

        /// <summary>
        /// Gets or sets a Description of the License (null if not specified). It isn't signed, so it is only a label
        /// </summary>
        public string Description { get; set; }
    }
}
