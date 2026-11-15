// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// Stores the Admin Roles an Owner adds to the built in ones (one file per Role in the "security/admin-roles" directory of the
    /// Configuration Profile). The Roles it loads are the ones TrakHoundAdminSecurity.GetRole finds, next to the built in Roles
    /// </summary>
    public sealed class TrakHoundAdminRoleStore
    {
        public const string DirectoryName = "admin-roles";
        public const int IdMaxLength = 50;
        public const int NameMaxLength = 100;
        private const string _securityDirectory = "security";
        private const string _fileExtension = ".yaml";

        private readonly string _directory;
        private readonly Dictionary<string, TrakHoundAdminRole> _roles = new Dictionary<string, TrakHoundAdminRole>(StringComparer.OrdinalIgnoreCase); // Id => Role
        private readonly object _lock = new object();


        /// <summary>
        /// Raised when a Role is saved or deleted
        /// </summary>
        public event EventHandler Changed;


        public TrakHoundAdminRoleStore(string directory)
        {
            _directory = directory;
            Load();
        }

        /// <summary>
        /// The directory the Roles of a Configuration Profile are kept in
        /// </summary>
        public static string GetDirectory(string configurationProfilePath)
        {
            return Path.Combine(configurationProfilePath, _securityDirectory, DirectoryName);
        }


        public void Load()
        {
            var roles = new List<TrakHoundAdminRole>();

            if (Directory.Exists(_directory))
            {
                var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

                foreach (var path in Directory.GetFiles(_directory, $"*{_fileExtension}"))
                {
                    try
                    {
                        // A file that was edited into something that can't be used gives no Permissions (it isn't loaded)
                        var role = deserializer.Deserialize<TrakHoundAdminRole>(File.ReadAllText(path));
                        if (role != null && Validate(role) == null) roles.Add(role);
                    }
                    catch { }
                }
            }

            lock (_lock)
            {
                _roles.Clear();
                foreach (var role in roles) _roles[role.Id] = role;
                Publish();
            }
        }

        public IEnumerable<TrakHoundAdminRole> GetRoles()
        {
            lock (_lock) return _roles.Values.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).Select(o => o.Clone()).ToList();
        }

        public TrakHoundAdminRole GetRole(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_lock) return _roles.GetValueOrDefault(id)?.Clone();
        }


        /// <summary>
        /// Returns the reason the Role can't be used (null if it can)
        /// </summary>
        public static string Validate(TrakHoundAdminRole role)
        {
            if (role == null) return "A Role is required";
            if (string.IsNullOrEmpty(role.Id)) return "An ID is required";
            if (role.Id.Length > IdMaxLength) return $"The ID must be {IdMaxLength} characters or less";
            if (!char.IsAsciiLetterOrDigit(role.Id[0]) || role.Id.Any(o => !(char.IsAsciiLetterLower(o) || char.IsAsciiDigit(o) || o == '-'))) return "The ID can only have lowercase letters, numbers and '-', starting with a letter or number";
            if (TrakHoundAdminSecurity.Roles.Any(o => string.Equals(o.Id, role.Id, StringComparison.OrdinalIgnoreCase))) return $"'{role.Id}' is a built in Role";
            if (string.IsNullOrWhiteSpace(role.Name)) return "A Name is required";
            if (role.Name.Length > NameMaxLength) return $"The Name must be {NameMaxLength} characters or less";

            foreach (var grant in role.Grants ?? new List<TrakHoundAdminGrant>())
            {
                if (grant == null || TrakHoundAdminSecurity.GetResource(grant.Resource) == null) return $"'{grant?.Resource}' isn't an Admin Resource";

                // Only Owners manage the Admin Accounts and Roles : a Role that could would let its holders make themselves Owners
                if (grant.Resource == TrakHoundAdminSecurity.Admins) return "Only the Owner Role can include the Admin Accounts";

                if (grant.Permissions.IsNullOrEmpty() || grant.Permissions.Any(o => o != TrakHoundAdminSecurity.ReadPermission && o != TrakHoundAdminSecurity.WritePermission))
                {
                    return $"The Permissions for '{grant.Resource}' must be 'read' or 'write'";
                }
            }

            return null;
        }

        /// <summary>
        /// Adds the Role, or replaces the Role with the same ID. A change applies to what the Accounts that hold it do next.
        /// Returns the reason it wasn't saved (null when it was)
        /// </summary>
        public string Save(TrakHoundAdminRole role)
        {
            var message = TrakHoundAdminContext.GetDenial(TrakHoundAdminSecurity.Admins, TrakHoundAdminSecurity.WritePermission) ?? Validate(role);
            if (message != null) return message;

            var saved = role.Clone();
            saved.Name = saved.Name.Trim();
            saved.Description = string.IsNullOrWhiteSpace(saved.Description) ? null : saved.Description.Trim();
            saved.Grants = saved.Grants?.Where(o => !o.Permissions.IsNullOrEmpty()).ToList() ?? new List<TrakHoundAdminGrant>();
            saved.GrantableRoles = saved.GrantableRoles?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).Distinct().ToList();
            if (saved.GrantableRoles.IsNullOrEmpty()) saved.GrantableRoles = null;

            lock (_lock)
            {
                message = Write(saved);
                if (message != null) return message;

                _roles[saved.Id] = saved;
                Publish();
            }

            OnChanged();
            return null;
        }

        /// <summary>
        /// Deletes the Role, unless an Account holds it. Returns the reason it wasn't deleted (null when it was)
        /// </summary>
        public string Delete(string id, IEnumerable<TrakHoundAdminAccount> accounts)
        {
            var message = TrakHoundAdminContext.GetDenial(TrakHoundAdminSecurity.Admins, TrakHoundAdminSecurity.WritePermission);
            if (message != null) return message;

            var holders = accounts?.Where(o => o.RoleIds.Any(r => string.Equals(r, id, StringComparison.OrdinalIgnoreCase))).Select(o => o.Username).ToList();
            if (!holders.IsNullOrEmpty()) return $"These Admin Accounts have the Role, so it can't be deleted : {string.Join(", ", holders)}";

            lock (_lock)
            {
                if (!_roles.ContainsKey(id ?? "")) return "Admin Role Not Found";

                try
                {
                    var path = GetRolePath(id);
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex)
                {
                    return $"The Admin Role could not be deleted : {ex.Message}";
                }

                _roles.Remove(id);
                Publish();
            }

            OnChanged();
            return null;
        }

        /// <summary>
        /// The file a Role is kept in (ex. to record a change to it in the Audit Log)
        /// </summary>
        public string GetRolePath(string id)
        {
            // Checked when a Role is saved, but never let an ID leave the directory
            if (string.IsNullOrEmpty(id) || id.Any(o => !(char.IsAsciiLetterOrDigit(o) || o == '-'))) throw new ArgumentException("Invalid Admin Role ID");
            return Path.Combine(_directory, $"{id.ToLowerInvariant()}{_fileExtension}");
        }


        // Inside the lock : what TrakHoundAdminSecurity.GetRole finds
        private void Publish() => TrakHoundAdminSecurity.SetCustomRoles(_roles.Values);

        // Written to a temporary file first, so a failed write never leaves a partial file
        private string Write(TrakHoundAdminRole role)
        {
            var path = GetRolePath(role.Id);
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(_directory);

                var serializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
                File.WriteAllText(tempPath, serializer.Serialize(role));
                File.Move(tempPath, path, true);
                return null;
            }
            catch (Exception ex)
            {
                try { File.Delete(tempPath); } catch { }
                return $"The Admin Role could not be saved : {ex.Message}";
            }
        }

        private void OnChanged()
        {
            try { Changed?.Invoke(this, EventArgs.Empty); } catch { }
        }
    }
}
