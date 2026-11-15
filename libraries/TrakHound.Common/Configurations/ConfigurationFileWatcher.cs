// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TrakHound.Configurations
{
    class ConfigurationFileWatcher<T> : IDisposable
    {
        protected const int DefaultInterval = 2000;

        private readonly string _category;
        private readonly string _path;
        private readonly Func<string, T> _readFunction;
        private readonly int _interval;
        private readonly List<string> _changed = new List<string>();
        private readonly Dictionary<string, DateTime> _deleted = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase); // Path => Time Deleted
        private readonly object _lock = new object();
        private FileSystemWatcher _watcher;
        private System.Timers.Timer _timer;
        private bool _update = false;


        public EventHandler<T> ConfigurationUpdated { get; set; }

        public EventHandler<ConfigurationFileRemovedArgs> ConfigurationRemoved { get; set; }

        public EventHandler<string> ErrorReceived { get; set; }


        public ConfigurationFileWatcher(string category, string path, Func<string, T> readFunction, int interval = DefaultInterval)
        {
            _category = category;
            _path = path;
            _readFunction = readFunction;
            _interval = interval;
        }

        /// <summary>
        /// Starts watching the directory (call after subscribing to the events so that errors starting the watcher are reported)
        /// </summary>
        public void Start()
        {
            try
            {
                if (!Directory.Exists(_path)) Directory.CreateDirectory(_path);

                _watcher = new FileSystemWatcher();
                _watcher.Path = _path;
                _watcher.Filter = "*";
                _watcher.Created += FileChanged;
                _watcher.Changed += FileChanged;
                _watcher.Deleted += FileDeleted;
                _watcher.Renamed += FileRenamed;
                _watcher.Error += FileWatcherError;
                _watcher.EnableRaisingEvents = true;

                _timer = new System.Timers.Timer();
                _timer.Interval = _interval;
                _timer.Elapsed += UpdateTimerElapsed;
                _timer.Enabled = true;
            }
            catch (Exception ex)
            {
                if (ErrorReceived != null) ErrorReceived.Invoke(this, ex.Message);
            }
        }

        private void Update()
        {
            if (_update)
            {
                _update = false;

                IEnumerable<string> changed;
                lock (_lock)
                {
                    changed = _changed.ToList().Distinct();
                    _changed.Clear();
                }

                if (!changed.IsNullOrEmpty())
                {
                    foreach (var path in changed)
                    {
                        var configuration = ReadFile(path);
                        if (configuration != null)
                        {
                            if (ConfigurationUpdated != null) ConfigurationUpdated.Invoke(this, configuration);
                        }
                    }
                }
            }

            RemoveDeleted();
        }

        /// <summary>
        /// Raises ConfigurationRemoved for files that were deleted and haven't come back. Many editors (and tools like "sed -i") save by
        /// writing a temporary file and renaming it over the original, which is reported as the file being deleted and then created again
        /// </summary>
        private void RemoveDeleted()
        {
            var now = DateTime.UtcNow;
            var removed = new List<string>();

            lock (_lock)
            {
                foreach (var deleted in _deleted.ToList())
                {
                    // Wait for the rest of a save (the rename or create usually follows within a few milliseconds)
                    if (now - deleted.Value < TimeSpan.FromMilliseconds(_interval / 2)) continue;

                    _deleted.Remove(deleted.Key);
                    if (!File.Exists(deleted.Key)) removed.Add(deleted.Key);
                }
            }

            foreach (var path in removed)
            {
                // The Configuration is identified by its file (the file's name may not match the Id in its content)
                if (ConfigurationRemoved != null) ConfigurationRemoved.Invoke(this, new ConfigurationFileRemovedArgs(_category, path));
            }
        }

        private void UpdateTimerElapsed(object sender, EventArgs args)
        {
            Update();
        }


        private void FileChanged(object sender, FileSystemEventArgs args)
        {
            if (args.ChangeType == WatcherChangeTypes.Changed || args.ChangeType == WatcherChangeTypes.Created)
            {
                AddChanged(args.FullPath);
            }
        }

        private void FileRenamed(object sender, RenamedEventArgs args)
        {
            // The old name is gone (unless something else replaces it) and the new name has new content
            if (!string.IsNullOrEmpty(args.OldFullPath)) AddDeleted(args.OldFullPath);
            if (!string.IsNullOrEmpty(args.FullPath)) AddChanged(args.FullPath);
        }

        private void FileDeleted(object sender, FileSystemEventArgs args)
        {
            // Checked later (see RemoveDeleted) since the file may be replaced as part of a save
            if (!string.IsNullOrEmpty(args.Name)) AddDeleted(args.FullPath);
        }

        private void AddChanged(string path)
        {
            lock (_lock) _changed.Add(path);
            _update = true;
        }

        private void AddDeleted(string path)
        {
            lock (_lock) _deleted[path] = DateTime.UtcNow;
        }

        private void FileWatcherError(object sender, ErrorEventArgs e)
        {
            if (ErrorReceived != null) ErrorReceived.Invoke(this, e.GetException()?.Message);
        }

        private T ReadFile(string path)
        {
            try
            {
                if (_readFunction != null)
                {
                    return _readFunction(path);
                }
            }
            catch (Exception ex)
            {
                if (ErrorReceived != null) ErrorReceived.Invoke(this, ex.Message);
            }

            return default;
        }


        public void Dispose()
        {
            if (_watcher != null) _watcher.Dispose();
            if (_timer != null) _timer.Dispose();
        }
    }
}
