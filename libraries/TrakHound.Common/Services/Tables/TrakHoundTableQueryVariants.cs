// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// A Query written for one or more SQL Dialects. The Table Service that receives it runs the version for the first of its
    /// Dialects that has one, so a module doesn't need to know which database it is using
    /// </summary>
    public class TrakHoundTableQueryVariants : Dictionary<string, string>
    {
        /// <summary>
        /// Creates empty Query Variants
        /// </summary>
        public TrakHoundTableQueryVariants() : base(StringComparer.OrdinalIgnoreCase) { }


        /// <summary>
        /// Creates Query Variants with a Query written in the portable "ansi" subset of SQL
        /// </summary>
        public static TrakHoundTableQueryVariants Ansi(string query)
        {
            return new TrakHoundTableQueryVariants { [TrakHoundTableDialects.Ansi] = query };
        }

        /// <summary>
        /// Loads a script from a directory with one file for each Dialect (ex. sqlserver.sql, postgres.sql). Throws a
        /// DirectoryNotFoundException if the directory doesn't exist and a FileNotFoundException if it has no scripts,
        /// so a script can't be skipped without anyone noticing
        /// </summary>
        public static TrakHoundTableQueryVariants FromDirectory(string path)
        {
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"Script Directory Not Found : {path}");

            var variants = new TrakHoundTableQueryVariants();
            foreach (var file in Directory.GetFiles(path, "*.sql"))
            {
                variants[Path.GetFileNameWithoutExtension(file)] = File.ReadAllText(file);
            }

            if (variants.Count == 0) throw new FileNotFoundException($"No Scripts Found in Directory : {path}");

            return variants;
        }


        /// <summary>
        /// Gets the Query for the first of the Dialects that has one (null if none do)
        /// </summary>
        public string Select(IEnumerable<string> dialects)
        {
            if (dialects != null)
            {
                foreach (var dialect in dialects)
                {
                    if (TryGetValue(dialect, out var query) && !string.IsNullOrEmpty(query)) return query;
                }
            }

            return null;
        }
    }
}
