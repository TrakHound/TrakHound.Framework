// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Licenses
{
    /// <summary>
    /// Keeps one Publisher's Licenses and Assignments, saved to [publisherId].json in the Licenses folder
    /// </summary>
    /// <remarks>
    /// The Manager reads License Codes but can't check their signatures (it doesn't have the Publisher's public key),
    /// so a licensed Module checks its own License with <see cref="TrakHoundLicenseVerifier"/>.
    /// Assignments are kept in memory: a Module adds its Assignments again each time it starts, and the file keeps which License each one uses.
    /// </remarks>
    public sealed class TrakHoundLicenseManager : ITrakHoundLicenseManager
    {
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private readonly string _path;
        private readonly Dictionary<string, TrakHoundLicenseInformation> _licenses = new Dictionary<string, TrakHoundLicenseInformation>(); // LicenseCode => License
        private readonly Dictionary<string, string> _assignedLicenses = new Dictionary<string, string>(); // AssignmentId => LicenseCode
        private readonly Dictionary<string, TrakHoundLicenseAssignmentInformation> _assignments = new Dictionary<string, TrakHoundLicenseAssignmentInformation>(); // AssignmentId => Assignment
        private readonly object _lock = new object();


        public string PublisherId { get; }

        /// <summary>
        /// The file the Licenses are saved to
        /// </summary>
        public string Path => _path;


        /// <param name="publisherId">The Publisher the Licenses are for</param>
        /// <param name="directory">The Licenses folder</param>
        public TrakHoundLicenseManager(string publisherId, string directory)
        {
            PublisherId = publisherId;
            _path = System.IO.Path.Combine(directory, $"{publisherId}.json");

            Load();
        }


        public void Refresh() => Load();

        public string GetDeviceId() => TrakHoundDeviceId.Get();


        #region "Licenses"

        public IEnumerable<TrakHoundLicenseInformation> GetLicenses()
        {
            lock (_lock) return _licenses.Values.ToList();
        }

        public TrakHoundLicenseInformation GetLicenseByLicenseCode(string licenseCode)
        {
            var key = GetKey(licenseCode);
            if (key == null) return null;

            lock (_lock) return _licenses.GetValueOrDefault(key);
        }

        public IEnumerable<TrakHoundLicenseInformation> GetLicensesByProduct(string productId)
        {
            lock (_lock) return _licenses.Values.Where(o => string.Equals(o.ProductId, productId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public IEnumerable<TrakHoundLicenseInformation> GetLicensesByDevice(string deviceId)
        {
            lock (_lock) return _licenses.Values.Where(o => o.DeviceId == deviceId).ToList();
        }

        /// <summary>
        /// Add a License. Throws an ArgumentException (with a message to show) if the License Code can't be used on this Device
        /// </summary>
        public TrakHoundLicenseInformation AddLicense(string productId, string deviceId, string licenseCode, string description = null)
        {
            var license = CreateLicense(productId, deviceId, licenseCode);
            license.Description = TrakHoundLicenseInformation.NormalizeDescription(description);

            lock (_lock)
            {
                // Adding a License that is already here keeps its Assignments, and the Description it was given (ex. edited on the Licenses page)
                if (_licenses.TryGetValue(license.LicenseCode, out var existing))
                {
                    if (existing.Description == null && license.Description != null)
                    {
                        existing.Description = license.Description;
                        Save();
                    }

                    return existing;
                }

                _licenses[license.LicenseCode] = license;
                Save();
            }

            return license;
        }

        public TrakHoundLicenseInformation SetLicenseDescription(string licenseCode, string description)
        {
            var key = GetKey(licenseCode);
            if (key == null) return null;

            lock (_lock)
            {
                if (!_licenses.TryGetValue(key, out var license)) return null;

                license.Description = TrakHoundLicenseInformation.NormalizeDescription(description);
                Save();

                return license;
            }
        }

        /// <summary>
        /// Replace a License with a new License Code (ex. a renewal), keeping its Assignments
        /// </summary>
        public TrakHoundLicenseInformation RenewLicense(string productId, string deviceId, string originalLicenseCode, string newLicenseCode)
        {
            var original = GetKey(originalLicenseCode);
            var license = CreateLicense(productId, deviceId, newLicenseCode);

            lock (_lock)
            {
                if (original == null || !_licenses.ContainsKey(original)) throw new ArgumentException("The License being renewed wasn't found");

                var assignmentIds = _assignedLicenses.Where(o => o.Value == original).Select(o => o.Key).ToList();
                if (assignmentIds.Count > license.AssignmentLimit)
                {
                    throw new ArgumentException($"The new License allows {license.AssignmentLimit} Assignments, but {assignmentIds.Count} use the current License. Unassign some first");
                }

                license.Description = _licenses[original].Description;
                _licenses.Remove(original);
                _licenses[license.LicenseCode] = license;

                foreach (var assignmentId in assignmentIds)
                {
                    _assignedLicenses[assignmentId] = license.LicenseCode;
                    if (_assignments.TryGetValue(assignmentId, out var assignment)) assignment.LicenseCode = license.LicenseCode;
                }

                license.AssignmentCount = assignmentIds.Count;
                Save();
            }

            return license;
        }

        public void RemoveLicense(string licenseCode)
        {
            var key = GetKey(licenseCode);
            if (key == null) return;

            lock (_lock)
            {
                if (!_licenses.Remove(key)) return;

                UnassignLocked(key);
                Save();
            }
        }

        public void ClearLicenses()
        {
            lock (_lock)
            {
                _licenses.Clear();
                _assignedLicenses.Clear();
                foreach (var assignment in _assignments.Values) assignment.LicenseCode = null;

                Save();
            }
        }

        /// <summary>
        /// Check a License Code's Product, Device and expiration. The signature isn't checked: the Module does that with <see cref="TrakHoundLicenseVerifier"/>
        /// </summary>
        public TrakHoundLicenseValidationResult CheckLicenseCode(string licenseCode)
        {
            var license = GetLicenseByLicenseCode(licenseCode);
            if (license == null) return new TrakHoundLicenseValidationResult { Message = "License Not Found" };

            return Check(license.ProductId, license.LicenseCode, DateTime.UtcNow);
        }

        #endregion

        #region "Assignments"

        public string GetAssignmentId(string productId, string assignmentKey)
        {
            var deviceId = GetDeviceId();
            if (!string.IsNullOrEmpty(productId) && !string.IsNullOrEmpty(deviceId) && !string.IsNullOrEmpty(assignmentKey))
            {
                return $"{productId}:{deviceId}:{assignmentKey}".ToMD5Hash();
            }

            return null;
        }

        public IEnumerable<TrakHoundLicenseAssignmentInformation> GetAssignments()
        {
            lock (_lock) return _assignments.Values.ToList();
        }

        public TrakHoundLicenseAssignmentInformation GetAssignment(string assignmentId)
        {
            if (string.IsNullOrEmpty(assignmentId)) return null;

            lock (_lock) return _assignments.GetValueOrDefault(assignmentId);
        }

        public IEnumerable<TrakHoundLicenseAssignmentInformation> GetAssignmentsByProduct(string productId)
        {
            lock (_lock) return _assignments.Values.Where(o => string.Equals(o.ProductId, productId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public IEnumerable<TrakHoundLicenseAssignmentInformation> GetAssignmentsByDevice(string deviceId)
        {
            lock (_lock) return _assignments.Values.Where(o => o.DeviceId == deviceId).ToList();
        }

        public IEnumerable<TrakHoundLicenseAssignmentInformation> GetAssignmentsByLicense(string licenseCode)
        {
            var key = GetKey(licenseCode);
            if (key == null) return Enumerable.Empty<TrakHoundLicenseAssignmentInformation>();

            lock (_lock) return _assignments.Values.Where(o => o.LicenseCode == key).ToList();
        }

        public TrakHoundLicenseAssignmentInformation AddAssignment(string productId, string deviceId, string assignmentKey, string description = null)
        {
            var assignmentId = GetAssignmentId(productId, assignmentKey);
            if (assignmentId == null || string.IsNullOrEmpty(deviceId)) return null;

            lock (_lock)
            {
                var assignment = new TrakHoundLicenseAssignmentInformation();
                assignment.AssignmentId = assignmentId;
                assignment.ProductId = productId;
                assignment.DeviceId = deviceId;
                assignment.Description = description;
                assignment.Created = DateTime.UtcNow;

                // Keep the License it was assigned before the Module (or Instance) restarted
                assignment.LicenseCode = _assignedLicenses.GetValueOrDefault(assignmentId);
                if (assignment.LicenseCode != null) assignment.Assigned = _assignments.GetValueOrDefault(assignmentId)?.Assigned ?? assignment.Created;

                _assignments[assignmentId] = assignment;
                return assignment;
            }
        }

        public void RemoveAssignment(string assignmentId)
        {
            if (string.IsNullOrEmpty(assignmentId)) return;

            lock (_lock) _assignments.Remove(assignmentId);
        }

        public TrakHoundLicenseAssignmentResult AssignLicense(string licenseCode, string assignmentId)
        {
            var result = new TrakHoundLicenseAssignmentResult();
            result.AssignmentId = assignmentId;

            var key = GetKey(licenseCode);
            if (key == null || string.IsNullOrEmpty(assignmentId))
            {
                result.Message = "No License or Assignment was specified";
                return result;
            }

            lock (_lock)
            {
                var license = _licenses.GetValueOrDefault(key);
                if (license == null)
                {
                    result.Message = "License Not Found";
                    return result;
                }

                result.ProductId = license.ProductId;
                result.DeviceId = license.DeviceId;

                var assignment = _assignments.GetValueOrDefault(assignmentId);
                if (assignment != null && !string.Equals(assignment.ProductId, license.ProductId, StringComparison.OrdinalIgnoreCase))
                {
                    result.Message = $"The License is for '{license.ProductId}', not '{assignment.ProductId}'";
                    return result;
                }

                if (_assignedLicenses.GetValueOrDefault(assignmentId) == key)
                {
                    result.Message = "Assignment ID already assigned to License";
                    return result;
                }

                var check = Check(license.ProductId, key, DateTime.UtcNow);
                if (!check.IsValid)
                {
                    result.Message = check.Message;
                    return result;
                }

                if (CountLocked(key) >= license.AssignmentLimit)
                {
                    result.Message = "License units exceeded";
                    return result;
                }

                // Moving an Assignment to this License frees its seat on the other one
                var previous = _assignedLicenses.GetValueOrDefault(assignmentId);

                _assignedLicenses[assignmentId] = key;
                if (assignment != null)
                {
                    assignment.LicenseCode = key;
                    assignment.Assigned = DateTime.UtcNow;
                }

                UpdateCountLocked(key);
                if (previous != null) UpdateCountLocked(previous);
                Save();

                result.Success = true;
                return result;
            }
        }

        public void UnassignLicense(string assignmentId)
        {
            if (string.IsNullOrEmpty(assignmentId)) return;

            lock (_lock)
            {
                if (!_assignedLicenses.Remove(assignmentId, out var licenseCode)) return;

                if (_assignments.TryGetValue(assignmentId, out var assignment))
                {
                    assignment.LicenseCode = null;
                    assignment.Assigned = null;
                }

                UpdateCountLocked(licenseCode);
                Save();
            }
        }

        public void UnassignAll(string licenseCode)
        {
            var key = GetKey(licenseCode);
            if (key == null) return;

            lock (_lock)
            {
                UnassignLocked(key);
                Save();
            }
        }

        public void UnassignAll()
        {
            lock (_lock)
            {
                foreach (var licenseCode in _licenses.Keys.ToList()) UnassignLocked(licenseCode);
                Save();
            }
        }

        /// <summary>
        /// Check the License assigned to an Assignment (Product, Device and expiration). The signature isn't checked: the Module does that with <see cref="TrakHoundLicenseVerifier"/>
        /// </summary>
        public TrakHoundLicenseValidationResult CheckAssignment(string assignmentId)
        {
            string licenseCode;
            lock (_lock) licenseCode = !string.IsNullOrEmpty(assignmentId) ? _assignedLicenses.GetValueOrDefault(assignmentId) : null;

            if (licenseCode == null) return new TrakHoundLicenseValidationResult { Message = "No License is assigned" };

            return CheckLicenseCode(licenseCode);
        }

        #endregion


        private TrakHoundLicenseInformation CreateLicense(string productId, string deviceId, string licenseCode)
        {
            if (string.IsNullOrWhiteSpace(productId)) throw new ArgumentException("No Product ID was specified");

            var code = TrakHoundLicenseCode.Parse(licenseCode);
            if (code == null) throw new ArgumentException("This isn't a License Code. Check that it was copied completely");
            if (!code.IsForProduct(productId)) throw new ArgumentException($"This License isn't for Product '{productId.Trim()}'");
            if (!code.IsForDevice(deviceId)) throw new ArgumentException("This License was issued for a different Device ID");

            return new TrakHoundLicenseInformation
            {
                LicenseCode = code.ToString(),
                ProductId = productId.Trim(),
                DeviceId = deviceId,
                AssignmentLimit = code.AssignmentLimit,
                ExpirationDate = code.ExpirationDate,
                IsPermanent = code.IsPermanent,
                IsAnyDevice = code.IsAnyDevice
            };
        }

        private TrakHoundLicenseValidationResult Check(string productId, string licenseCode, DateTime now)
        {
            var deviceId = GetDeviceId();
            var result = new TrakHoundLicenseValidationResult { ProductId = productId, DeviceId = deviceId };

            var code = TrakHoundLicenseCode.Parse(licenseCode);
            if (code == null)
            {
                result.Message = "Not a License Code";
                return result;
            }

            result.ExpirationDate = code.ExpirationDate;
            result.IsPermanent = code.IsPermanent;
            result.IsAnyDevice = code.IsAnyDevice;
            result.AssignmentLimit = code.AssignmentLimit;

            if (!code.IsForProduct(productId)) result.Message = $"The License isn't for Product '{productId}'";
            else if (!code.IsForDevice(deviceId)) result.Message = "The License is for a different Device";
            else if (!code.IsPermanent && now >= code.ExpirationDate) result.Message = $"The License expired on {code.ExpirationDate:yyyy-MM-dd}";
            else result.IsValid = true;

            return result;
        }

        // License Codes are kept in the form they are issued in, so a code typed in lower case (or without dashes) finds the same License
        private static string GetKey(string licenseCode) => !string.IsNullOrWhiteSpace(licenseCode) ? TrakHoundLicenseCode.Format(licenseCode) : null;

        private int CountLocked(string licenseCode) => _assignedLicenses.Values.Count(o => o == licenseCode);

        private void UpdateCountLocked(string licenseCode)
        {
            if (_licenses.TryGetValue(licenseCode, out var license)) license.AssignmentCount = CountLocked(licenseCode);
        }

        private void UnassignLocked(string licenseCode)
        {
            foreach (var assignmentId in _assignedLicenses.Where(o => o.Value == licenseCode).Select(o => o.Key).ToList())
            {
                _assignedLicenses.Remove(assignmentId);
                if (_assignments.TryGetValue(assignmentId, out var assignment))
                {
                    assignment.LicenseCode = null;
                    assignment.Assigned = null;
                }
            }

            UpdateCountLocked(licenseCode);
        }


        private void Load()
        {
            var file = ReadFile();
            var deviceId = GetDeviceId();

            lock (_lock)
            {
                _licenses.Clear();
                _assignedLicenses.Clear();

                foreach (var entry in file?.Licenses ?? Enumerable.Empty<LicenseEntry>())
                {
                    TrakHoundLicenseInformation license;
                    try
                    {
                        license = CreateLicense(entry.ProductId, deviceId, entry.LicenseCode);
                    }
                    catch (ArgumentException)
                    {
                        // Not a License for this Device (ex. the file was copied from another machine)
                        continue;
                    }

                    license.Description = TrakHoundLicenseInformation.NormalizeDescription(entry.Description);
                    _licenses[license.LicenseCode] = license;

                    // Expired Licenses keep their Assignments so a Renew moves them to the new License. Assignments over the limit (an edited file) are dropped
                    foreach (var assignmentId in entry.AssignmentIds ?? Enumerable.Empty<string>())
                    {
                        if (string.IsNullOrEmpty(assignmentId) || _assignedLicenses.ContainsKey(assignmentId)) continue;
                        if (CountLocked(license.LicenseCode) >= license.AssignmentLimit) break;

                        _assignedLicenses[assignmentId] = license.LicenseCode;
                    }

                    UpdateCountLocked(license.LicenseCode);
                }

                // Assignments already added by Modules follow what the file now says
                foreach (var assignment in _assignments.Values)
                {
                    assignment.LicenseCode = _assignedLicenses.GetValueOrDefault(assignment.AssignmentId);
                    if (assignment.LicenseCode == null) assignment.Assigned = null;
                }
            }
        }

        private LicenseFile ReadFile()
        {
            try
            {
                if (File.Exists(_path)) return JsonSerializer.Deserialize<LicenseFile>(File.ReadAllText(_path), _jsonOptions);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException) { }

            return null;
        }

        // Called with the lock held, so two changes can't be written out of order
        private void Save()
        {
            var file = new LicenseFile();
            file.Licenses = _licenses.Values.Select(license => new LicenseEntry
            {
                ProductId = license.ProductId,
                LicenseCode = license.LicenseCode,
                Description = license.Description,
                AssignmentIds = _assignedLicenses.Where(o => o.Value == license.LicenseCode).Select(o => o.Key).OrderBy(o => o).ToList()
            }).ToList();

            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // Write a new file and swap it in, so a crash can't leave half a file
            var temporaryPath = $"{_path}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(file, _jsonOptions));
            File.Move(temporaryPath, _path, true);
        }


        private class LicenseFile
        {
            [JsonPropertyName("licenses")]
            public List<LicenseEntry> Licenses { get; set; }
        }

        private class LicenseEntry
        {
            [JsonPropertyName("productId")]
            public string ProductId { get; set; }

            [JsonPropertyName("licenseCode")]
            public string LicenseCode { get; set; }

            [JsonPropertyName("description")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Description { get; set; }

            [JsonPropertyName("assignmentIds")]
            public List<string> AssignmentIds { get; set; }
        }
    }
}
