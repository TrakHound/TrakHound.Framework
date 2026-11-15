// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TrakHound.Licenses
{
    /// <summary>
    /// The License Managers for each Publisher, keyed by Publisher ID. A Manager is created the first time its Publisher is asked for
    /// </summary>
    public static class TrakHoundLicenseManagers
    {
        public const string DirectoryName = "licenses";

        /// <summary>
        /// The Publisher of a License that doesn't name one
        /// </summary>
        public const string DefaultPublisherId = "trakhound";

        // Used as a file name, so only simple IDs (ex. trakhound, contoso.tools)
        private static readonly Regex _publisherIdPattern = new Regex("^[a-z0-9][a-z0-9._-]{0,99}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly static Dictionary<string, TrakHoundLicenseManager> _managers = new Dictionary<string, TrakHoundLicenseManager>(StringComparer.OrdinalIgnoreCase); // PublisherId => LicenseManager
        private readonly static object _lock = new object();
        private static string _directory = Path.Combine(AppContext.BaseDirectory, "config", DirectoryName);


        /// <summary>
        /// Raised when a License Manager is added (the argument is the Publisher ID)
        /// </summary>
        public static event EventHandler<string> ManagerAdded;


        /// <summary>
        /// The folder the Licenses are saved in
        /// </summary>
        public static string Directory
        {
            get { lock (_lock) return _directory; }
        }


        /// <summary>
        /// Keep Licenses in the specified folder (ex. in the Instance's configuration profile), and load the Publishers that already have Licenses there
        /// </summary>
        public static void Initialize(string directory)
        {
            lock (_lock)
            {
                _directory = directory;
                _managers.Clear();
            }

            try
            {
                if (System.IO.Directory.Exists(directory))
                {
                    foreach (var file in System.IO.Directory.GetFiles(directory, "*.json"))
                    {
                        Get(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }


        /// <summary>
        /// Gets all of the loaded License Managers
        /// </summary>
        public static IEnumerable<ITrakHoundLicenseManager> Get()
        {
            lock (_lock)
            {
                return _managers.Values.OrderBy(o => o.PublisherId).ToList();
            }
        }

        /// <summary>
        /// Gets the License Manager for the specified Publisher ID, creating it if needed (null if the Publisher ID isn't valid)
        /// </summary>
        public static ITrakHoundLicenseManager Get(string publisherId)
        {
            if (!IsValidPublisherId(publisherId)) return null;

            TrakHoundLicenseManager manager;
            lock (_lock)
            {
                if (_managers.TryGetValue(publisherId, out manager)) return manager;

                manager = new TrakHoundLicenseManager(publisherId.ToLowerInvariant(), _directory);
                _managers.Add(publisherId, manager);
            }

            ManagerAdded?.Invoke(null, manager.PublisherId);
            return manager;
        }

        public static bool IsValidPublisherId(string publisherId) => !string.IsNullOrEmpty(publisherId) && _publisherIdPattern.IsMatch(publisherId);
    }
}
