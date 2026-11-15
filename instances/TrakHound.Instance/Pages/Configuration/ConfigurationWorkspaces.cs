// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.RegularExpressions;
using TrakHound.Configurations;
using TrakHound.Instances;
using TrakHound.Licenses;

namespace TrakHound.Instance.Pages.Configuration
{
    /// <summary>
    /// The Deployment Workspaces of the Instance (one directory each in the 'workspaces' directory, next to the 'config' directory).
    /// Each Workspace is opened once and shared by every browser session, so they all see the same changes
    /// </summary>
    public class ConfigurationWorkspaces : IDisposable
    {
        public const string DirectoryName = "workspaces";

        private static readonly Regex _idRegex = new Regex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$", RegexOptions.Compiled);

        private readonly ITrakHoundInstance _instance;
        private readonly Dictionary<string, WorkspaceConfigurationTarget> _opened = new Dictionary<string, WorkspaceConfigurationTarget>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();


        /// <summary>
        /// The directory the Workspaces are kept in
        /// </summary>
        public string Path { get; }


        public ConfigurationWorkspaces(ITrakHoundInstance instance)
        {
            _instance = instance;

            // {instance}/config/{profile} => {instance}/workspaces (outside 'config', so Deployments and Backups of the Instance leave them out)
            var profilePath = instance.ConfigurationProfile?.Path?.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            var configurationPath = !string.IsNullOrEmpty(profilePath) ? System.IO.Path.GetDirectoryName(profilePath) : null;
            var basePath = !string.IsNullOrEmpty(configurationPath) ? System.IO.Path.GetDirectoryName(configurationPath) : null;
            Path = System.IO.Path.Combine(basePath ?? AppContext.BaseDirectory, DirectoryName);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var workspace in _opened.Values) workspace.Dispose();
                _opened.Clear();
            }
        }


        /// <summary>
        /// Returns true if the ID can be used for a Workspace (letters, numbers, '_', '.' and '-', starting with a letter or number)
        /// </summary>
        public static bool IsValidId(string id) => !string.IsNullOrEmpty(id) && _idRegex.IsMatch(id);

        /// <summary>
        /// The IDs of the Workspaces (ordered by ID)
        /// </summary>
        public IEnumerable<string> GetIds()
        {
            if (!Directory.Exists(Path)) return Enumerable.Empty<string>();

            return Directory.GetDirectories(Path).Select(System.IO.Path.GetFileName).Where(IsValidId).OrderBy(o => o, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public bool Exists(string id) => IsValidId(id) && Directory.Exists(System.IO.Path.Combine(Path, id));

        /// <summary>
        /// Gets the Workspace (opening it the first time). Null if it doesn't exist
        /// </summary>
        public WorkspaceConfigurationTarget Get(string id)
        {
            if (!Exists(id)) return null;

            lock (_lock)
            {
                if (!_opened.TryGetValue(id, out var workspace))
                {
                    workspace = new WorkspaceConfigurationTarget(id, System.IO.Path.Combine(Path, id), _instance.PackageManager);
                    _opened[id] = workspace;
                }
                return workspace;
            }
        }

        /// <summary>
        /// Creates an empty Workspace. Returns the error, or null if it was created
        /// </summary>
        public string Create(string id)
        {
            var denial = TrakHound.Instance.Security.TrakHoundAdminContext.GetDenial(TrakHound.Instance.Security.TrakHoundAdminSecurity.Deployments, TrakHound.Instance.Security.TrakHoundAdminSecurity.WritePermission);
            if (denial != null) return denial;

            if (!IsValidId(id)) return $"'{id}' can't be used as a Workspace ID : use letters, numbers, '_', '.' and '-', starting with a letter or number";
            if (Exists(id)) return $"A Workspace with the ID '{id}' already exists";

            try
            {
                Directory.CreateDirectory(System.IO.Path.Combine(Path, id));
                return null;
            }
            catch (Exception ex)
            {
                return $"The Workspace could not be created : {ex.Message}";
            }
        }

        #region "Licenses"

        // Kept beside the Workspaces, not in one : Licenses are for a Device, so they aren't configuration ('.licenses' isn't a valid Workspace ID)
        private string GetLicensesPath(string id) => System.IO.Path.Combine(Path, ".licenses", $"{id}{TrakHoundLicenseFile.FileExtension}");

        /// <summary>
        /// The Licenses imported into the Workspace (for the Devices it is exported for)
        /// </summary>
        public TrakHoundLicenseFile GetLicenses(string id)
        {
            var path = IsValidId(id) ? GetLicensesPath(id) : null;
            if (path == null || !File.Exists(path)) return new TrakHoundLicenseFile();

            lock (_lock) return TrakHoundLicenseFile.Parse(File.ReadAllText(path));
        }

        /// <summary>
        /// Adds Licenses to the Workspace (a License Code it already has is skipped). Returns how many were added
        /// </summary>
        public int AddLicenses(string id, IEnumerable<TrakHoundLicenseFileEntry> licenses)
        {
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(TrakHound.Instance.Security.TrakHoundAdminSecurity.Deployments);
            if (!Exists(id)) throw new ArgumentException($"The Workspace '{id}' doesn't exist");

            lock (_lock)
            {
                var file = GetLicenses(id);
                var added = 0;

                foreach (var license in licenses ?? Enumerable.Empty<TrakHoundLicenseFileEntry>())
                {
                    var code = TrakHoundLicenseCode.Parse(license?.LicenseCode)?.ToString();
                    if (code == null || string.IsNullOrWhiteSpace(license.ProductId) || file.Licenses.Any(o => TrakHoundLicenseCode.Format(o.LicenseCode) == code)) continue;

                    file.Licenses.Add(new TrakHoundLicenseFileEntry
                    {
                        PublisherId = license.PublisherId?.Trim().ToLowerInvariant(),
                        DeviceId = license.DeviceId != null ? TrakHoundDeviceId.Normalize(license.DeviceId) ?? license.DeviceId : null,
                        ProductId = license.ProductId.Trim(),
                        LicenseCode = code
                    });
                    added++;
                }

                Save(id, file);
                return added;
            }
        }

        /// <summary>
        /// Removes a License from the Workspace
        /// </summary>
        public void RemoveLicense(string id, string licenseCode)
        {
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(TrakHound.Instance.Security.TrakHoundAdminSecurity.Deployments);

            lock (_lock)
            {
                var file = GetLicenses(id);
                var code = TrakHoundLicenseCode.Format(licenseCode);
                file.Licenses.RemoveAll(o => TrakHoundLicenseCode.Format(o.LicenseCode) == code);
                Save(id, file);
            }
        }

        private void Save(string id, TrakHoundLicenseFile file)
        {
            var path = GetLicensesPath(id);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            File.WriteAllText(path, file.ToJson());
        }

        #endregion

        #region "Devices"

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        // Kept beside the Workspaces like the Licenses ('.devices' isn't a valid Workspace ID)
        private string GetDevicesPath(string id) => System.IO.Path.Combine(Path, ".devices", $"{id}.json");

        /// <summary>
        /// The Devices the Workspace is exported for
        /// </summary>
        public List<WorkspaceDevice> GetDevices(string id)
        {
            var path = IsValidId(id) ? GetDevicesPath(id) : null;
            if (path == null || !File.Exists(path)) return new List<WorkspaceDevice>();

            lock (_lock)
            {
                try
                {
                    return JsonSerializer.Deserialize<List<WorkspaceDevice>>(File.ReadAllText(path), _jsonOptions) ?? new List<WorkspaceDevice>();
                }
                catch (JsonException)
                {
                    return new List<WorkspaceDevice>();
                }
            }
        }

        /// <summary>
        /// Replaces the Workspace's Devices. Returns the errors (nothing is saved if there are any)
        /// </summary>
        public List<string> SaveDevices(string id, IEnumerable<WorkspaceDevice> devices)
        {
            var errors = new List<string>();

            var denial = TrakHound.Instance.Security.TrakHoundAdminContext.GetDenial(TrakHound.Instance.Security.TrakHoundAdminSecurity.Deployments, TrakHound.Instance.Security.TrakHoundAdminSecurity.WritePermission);
            if (denial != null)
            {
                errors.Add(denial);
                return errors;
            }

            if (!Exists(id))
            {
                errors.Add($"The Workspace '{id}' doesn't exist");
                return errors;
            }

            var saved = new List<WorkspaceDevice>();
            foreach (var device in devices ?? Enumerable.Empty<WorkspaceDevice>())
            {
                var label = device.Label?.Trim();
                var deviceId = TrakHoundDeviceId.Normalize(device.DeviceId);
                var name = string.IsNullOrEmpty(label) ? device.DeviceId : label;

                if (string.IsNullOrEmpty(label)) errors.Add("Every device needs a Label");
                else if (saved.Any(o => string.Equals(o.Label, label, StringComparison.OrdinalIgnoreCase))) errors.Add($"More than one device is labeled '{label}'");

                if (deviceId == null) errors.Add($"'{name}' : '{device.DeviceId}' isn't a Device ID");
                else if (saved.Any(o => o.DeviceId == deviceId)) errors.Add($"'{name}' : another device has the Device ID {deviceId}");

                // Empty values use the Workspace's. Secret values are encrypted (with this Instance's key, like the Workspace's Variables)
                var variables = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var variable in device.Variables ?? new Dictionary<string, string>())
                {
                    if (string.IsNullOrEmpty(variable.Value)) continue;
                    variables[variable.Key] = TrakHoundSecrets.IsSecretKey(variable.Key) && !TrakHoundConfigurationVariables.IsReference(variable.Value)
                        ? TrakHoundSecrets.Encrypt(variable.Value)
                        : variable.Value;
                }

                saved.Add(new WorkspaceDevice { Label = label, DeviceId = deviceId, Variables = variables });
            }

            if (errors.Count > 0) return errors;

            lock (_lock)
            {
                var path = GetDevicesPath(id);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonSerializer.Serialize(saved, _jsonOptions));
            }

            return errors;
        }

        #endregion

        /// <summary>
        /// Deletes the Workspace and its files. Returns the error, or null if it was deleted
        /// </summary>
        public string Delete(string id)
        {
            var denial = TrakHound.Instance.Security.TrakHoundAdminContext.GetDenial(TrakHound.Instance.Security.TrakHoundAdminSecurity.Deployments, TrakHound.Instance.Security.TrakHoundAdminSecurity.WritePermission);
            if (denial != null) return denial;

            if (!Exists(id)) return $"The Workspace '{id}' doesn't exist";

            lock (_lock)
            {
                if (_opened.Remove(id, out var workspace)) workspace.Dispose();
            }

            try
            {
                Directory.Delete(System.IO.Path.Combine(Path, id), true);
                if (File.Exists(GetLicensesPath(id))) File.Delete(GetLicensesPath(id));
                if (File.Exists(GetDevicesPath(id))) File.Delete(GetDevicesPath(id));
                return null;
            }
            catch (Exception ex)
            {
                return $"The Workspace could not be deleted : {ex.Message}";
            }
        }
    }
}
