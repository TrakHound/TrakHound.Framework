// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Apps;

namespace TrakHound.Instance.Apps
{
    public class TrakHoundAppConnectionManager
    {
        private readonly Dictionary<string, TrakHoundAppConnection> _connections = new();
        private readonly object _lock = new();


        /// <summary>
        /// Raised when a Connection is added, removed, or updated (ex. navigated to another Page)
        /// </summary>
        public event Action Changed;


        /// <summary>
        /// The App Connections diagnostics page (for every App, or only the connections to one App)
        /// </summary>
        public static string GetLink(string instanceId, string appId = null)
        {
            var link = $"_admin/explorer/{instanceId}/diagnostics/connections/apps/pages";
            if (!string.IsNullOrEmpty(appId)) link += $"?app={Uri.EscapeDataString(appId)}";
            return link;
        }


        public IEnumerable<TrakHoundAppConnection> GetConnections()
        {
            lock (_lock)
            {
                return _connections.Values.ToList();
            }
        }

        public void AddConnection(string connectionId, TrakHoundAppConnection connection)
        {
            if (!string.IsNullOrEmpty(connectionId))
            {
                lock (_lock)
                {
                    if (!_connections.TryAdd(connectionId, connection)) return;
                }

                OnChanged();
            }
        }

        public void RemoveConnection(string connectionId)
        {
            if (!string.IsNullOrEmpty(connectionId))
            {
                lock (_lock)
                {
                    if (!_connections.Remove(connectionId)) return;
                }

                OnChanged();
            }
        }

        public void SetPath(string connectionId, string name, string requestPath, string appId = null, string packageVersion = null, string packageHash = null)
        {
            if (!string.IsNullOrEmpty(connectionId))
            {
                lock (_lock)
                {
                    var connection = _connections.GetValueOrDefault(connectionId);
                    if (connection == null) return;

                    connection.Name = name;
                    connection.RequestedPath = requestPath;
                    connection.AppId = appId;
                    connection.PackageVersion = packageVersion;
                    connection.PackageHash = packageHash;
                    connection.LastUpdated = DateTime.UtcNow;
                }

                OnChanged();
            }
        }

        public void SetHostname(string connectionId, string hostname)
        {
            if (!string.IsNullOrEmpty(connectionId))
            {
                lock (_lock)
                {
                    var connection = _connections.GetValueOrDefault(connectionId);
                    if (connection == null) return;

                    connection.ClientHostname = hostname;
                }

                OnChanged();
            }
        }

        /// <summary>
        /// Record the page's Activity (reported by the Browser)
        /// </summary>
        public void SetActivity(string connectionId, DateTime lastActivity, bool isEditing, bool hasInput, bool isHidden)
        {
            if (!string.IsNullOrEmpty(connectionId))
            {
                lock (_lock)
                {
                    var connection = _connections.GetValueOrDefault(connectionId);
                    if (connection == null) return;

                    connection.LastActivity = lastActivity;
                    connection.IsEditing = isEditing;
                    connection.HasInput = hasInput;
                    connection.IsHidden = isHidden;
                }

                OnChanged();
            }
        }

        /// <summary>
        /// Record whether the current page has unsaved changes, as declared by the App (null when it isn't declared)
        /// </summary>
        public void SetUnsavedChanges(string connectionId, bool? hasUnsavedChanges)
        {
            if (!string.IsNullOrEmpty(connectionId))
            {
                lock (_lock)
                {
                    var connection = _connections.GetValueOrDefault(connectionId);
                    if (connection == null || connection.HasUnsavedChanges == hasUnsavedChanges) return;

                    connection.HasUnsavedChanges = hasUnsavedChanges;
                }

                OnChanged();
            }
        }

        private void OnChanged()
        {
            try { Changed?.Invoke(); } catch { }
        }
    }
}
