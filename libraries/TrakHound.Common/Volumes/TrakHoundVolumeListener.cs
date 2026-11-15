// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrakHound.Volumes
{
    /// <summary>
    /// A Listener that uses a FileSystemWatcher to monitor a local Volume for changes to Files. Changes are batched and raised after a short delay
    /// </summary>
    public class TrakHoundVolumeListener : ITrakHoundVolumeListener
    {
        private readonly string _basePath;
        private readonly string _path;
        private readonly FileSystemWatcher _watcher;
        private readonly Dictionary<string, ChangeItem> _changedItems = new Dictionary<string, ChangeItem>();
        private readonly DelayEvent _delayEvent = new DelayEvent(100);
        private readonly object _lock = new object();

        struct ChangeItem
        {
            public string Path { get; set; }
            public TrakHoundVolumeOnChangeType Type { get; set; }

            public ChangeItem(string path, TrakHoundVolumeOnChangeType type)
            {
                Path = path;
                Type = type;
            }
        }


        /// <summary>
        /// Raised when a File is created, modified, or deleted
        /// </summary>
        public event TrakHoundVolumeOnChangeHandler Changed;


        /// <summary>
        /// Initializes a new Listener for the specified base Path, Path, and optional File filter (defaults to "*.*")
        /// </summary>
        public TrakHoundVolumeListener(string basePath, string path, string filter = null)
        {
            _basePath = basePath;
            _path = path;
            
            var fullPath = string.IsNullOrEmpty(_path) && _path != "/" ? Path.Combine(basePath, _path) : basePath;
            var filterExpression = !string.IsNullOrEmpty(filter) ? filter : "*.*";

            if (!Directory.Exists(fullPath)) Directory.CreateDirectory(fullPath);

            _watcher = new FileSystemWatcher(fullPath, filterExpression);
            _watcher.IncludeSubdirectories = true;
            _watcher.Created += FileSystemWatcherChanged;
            _watcher.Changed += FileSystemWatcherChanged;
            _watcher.Deleted += FileSystemWatcherChanged;
            _watcher.Renamed += FileSystemWatcherChanged;

            _delayEvent.Elapsed += DelayEventElapsed;
        }

        /// <summary>
        /// Starts listening for changes
        /// </summary>
        public void Start()
        {
            _watcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// Stops listening for changes
        /// </summary>
        public void Stop()
        {
            _watcher.EnableRaisingEvents = false;
        }

        /// <summary>
        /// Stops listening and releases the underlying FileSystemWatcher
        /// </summary>
        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.Created -= FileSystemWatcherChanged;
                _watcher.Changed -= FileSystemWatcherChanged;
                _watcher.Deleted -= FileSystemWatcherChanged;
                _watcher.Renamed -= FileSystemWatcherChanged;

                _watcher.Dispose();
            }
        }

        private void FileSystemWatcherChanged(object sender, FileSystemEventArgs args)
        {
            var relativePath = Path.GetRelativePath(_basePath, args.FullPath);
            if (relativePath != null) relativePath = relativePath.Replace("\\", "/");

            TrakHoundVolumeOnChangeType changeType;
            switch (args.ChangeType)
            {
                case WatcherChangeTypes.Created: changeType = TrakHoundVolumeOnChangeType.Create; break;
                case WatcherChangeTypes.Deleted: changeType = TrakHoundVolumeOnChangeType.Delete; break;
                default: changeType = TrakHoundVolumeOnChangeType.Modify; break;
            }

            lock (_lock)
            {
                _changedItems.Remove(relativePath);
                _changedItems.Add(relativePath, new ChangeItem(relativePath, changeType));
            }

            _delayEvent.Set();
        }

        private void DelayEventElapsed(object sender, System.EventArgs e)
        {
            IEnumerable<ChangeItem> items;
            lock (_lock)
            {
                items = _changedItems.Values.ToList();
                _changedItems.Clear();
            }

            if (!items.IsNullOrEmpty())
            {
                foreach (var item in items)
                {
                    if (Changed != null) Changed.Invoke(item.Path, item.Type);
                }
            }
        }
    }
}
