// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Apps;

namespace TrakHound.Instance.Pages.Explorer
{
    /// <summary>
    /// Holds the Explorer Tree state (Expanded items, Filter) for the current user. Expanded items are saved to Local Storage
    /// </summary>
    public class ExplorerTreeViewService
    {
        private const string ExpandedStorageKey = "trakhound.admin.explorer.expanded";
        private const string SidebarCollapsedStorageKey = "trakhound.admin.explorer.sidebarCollapsed";
        private const int MaxSavedExpanded = 500;

        private readonly ITrakHoundAppLocalStorageService _localStorage;
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly HashSet<string> _loading = new HashSet<string>();
        private readonly HashSet<string> _knownIds = new HashSet<string>();
        private readonly object _lock = new object();
        private string _filter;
        private bool _loaded;


        /// <summary>
        /// Raised when the Tree needs to be re-rendered (ex. Filter changed, Expand / Collapse All)
        /// </summary>
        public event EventHandler Changed;


        /// <summary>
        /// Text used to filter the Tree (matching items, their ancestors, and their descendants are shown)
        /// </summary>
        public string Filter
        {
            get => _filter;
            set
            {
                if (_filter != value)
                {
                    _filter = value;
                    NotifyChanged();
                }
            }
        }

        public bool IsFiltering => !string.IsNullOrWhiteSpace(_filter);


        public ExplorerTreeViewService(ITrakHoundAppLocalStorageService localStorage)
        {
            _localStorage = localStorage;
        }


        /// <summary>
        /// Loads the saved Expanded items (call after the first render, as Local Storage requires JS Interop)
        /// </summary>
        public async Task Load()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                var ids = await _localStorage.GetItem<string[]>(ExpandedStorageKey);
                if (!ids.IsNullOrEmpty())
                {
                    lock (_lock)
                    {
                        foreach (var id in ids) _expanded.Add(id);
                    }

                    NotifyChanged();
                }
            }
            catch { }
        }

        public void NotifyChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }


        public void CollapseAll()
        {
            lock (_lock) _expanded.Clear();
            Save();
            NotifyChanged();
        }

        /// <summary>
        /// Expands every item in the Tree (except those that load on Expand)
        /// </summary>
        public void ExpandAll()
        {
            lock (_lock)
            {
                foreach (var id in _knownIds) _expanded.Add(id);
            }

            Save();
            NotifyChanged();
        }

        /// <summary>
        /// Sets the IDs of the items currently in the Tree (used by Expand All)
        /// </summary>
        public void SetKnownIds(IEnumerable<string> ids)
        {
            lock (_lock)
            {
                _knownIds.Clear();
                if (ids != null)
                {
                    foreach (var id in ids) _knownIds.Add(id);
                }
            }
        }


        public bool IsExpanded(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            lock (_lock) return _expanded.Contains(id);
        }

        public void SetExpanded(string id, bool expanded)
        {
            if (string.IsNullOrEmpty(id)) return;

            bool changed;
            lock (_lock)
            {
                changed = expanded ? _expanded.Add(id) : _expanded.Remove(id);
            }

            if (changed) Save();
        }

        public static string GetFolderIcon(bool expanded)
        {
            return expanded ? "fa-sharp fa-solid fa-folder-open" : "fa-sharp fa-solid fa-folder";
        }


        public bool IsLoading(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            lock (_lock) return _loading.Contains(id);
        }

        public void SetLoading(string id, bool loading)
        {
            if (string.IsNullOrEmpty(id)) return;

            lock (_lock)
            {
                if (loading) _loading.Add(id);
                else _loading.Remove(id);
            }
        }


        /// <summary>
        /// Gets whether the user collapsed the Explorer Sidebar (call after the first render, as Local Storage requires JS Interop)
        /// </summary>
        public async Task<bool> GetSidebarCollapsed()
        {
            try
            {
                return await _localStorage.GetItem<bool>(SidebarCollapsedStorageKey);
            }
            catch { }

            return false;
        }

        public async Task SetSidebarCollapsed(bool collapsed)
        {
            try
            {
                await _localStorage.SetItem(SidebarCollapsedStorageKey, collapsed);
            }
            catch { }
        }


        private async void Save()
        {
            string[] ids;
            lock (_lock)
            {
                // Prefer IDs that are currently in the Tree, and limit the size (removed items would otherwise accumulate)
                ids = _expanded.OrderByDescending(o => _knownIds.Contains(o)).Take(MaxSavedExpanded).ToArray();
            }

            try
            {
                await _localStorage.SetItem(ExpandedStorageKey, ids);
            }
            catch { }
        }
    }
}
