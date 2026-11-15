// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Services
{
    /// <summary>
    /// Configuration used to configure Routes to Services and Routers
    /// </summary>
    public class TrakHoundServiceRouteConfiguration
    {
        /// <summary>
        /// A unique Identifier for the Route
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// List of patterns used to match the desired route(s)
        /// </summary>
        public IEnumerable<string> Patterns { get; set; }

        /// <summary>
        /// Filters for the Route (not currently used by the Service Router)
        /// </summary>
        public IEnumerable<string> Filters { get; set; }

        /// <summary>
        /// List of Targets to use when the Route pattern(s) are matched
        /// </summary>
        public IEnumerable<TrakHoundServiceTargetConfiguration> Targets { get; set; }


        /// <summary>
        /// Creates a Route Configuration with a new unique Id
        /// </summary>
        public TrakHoundServiceRouteConfiguration()
        {
            Id = Guid.NewGuid().ToString();
        }


        /// <summary>
        /// Adds a Pattern used to match Routes
        /// </summary>
        public void AddPattern(string pattern)
        {
            if (pattern != null)
            {
                var patterns = Patterns?.ToList();
                if (patterns == null) patterns = new List<string>();
                patterns.Add(pattern);
                Patterns = patterns;
            }
        }

        /// <summary>
        /// Removes the specified Pattern (Patterns is set to null when none remain)
        /// </summary>
        public void RemovePattern(string pattern)
        {
            if (!string.IsNullOrEmpty(pattern) && !Patterns.IsNullOrEmpty())
            {
                var patterns = Patterns.ToList();
                patterns.RemoveAll(o => o == pattern);
                if (patterns.Count > 0)
                {
                    Patterns = patterns;
                }
                else Patterns = null;
            }
        }



        /// <summary>
        /// Adds a Filter
        /// </summary>
        public void AddFilter(string filter)
        {
            if (filter != null)
            {
                var filters = Filters?.ToList();
                if (filters == null) filters = new List<string>();
                filters.Add(filter);
                Filters = filters;
            }
        }

        /// <summary>
        /// Removes the specified Filter (Filters is set to null when none remain)
        /// </summary>
        public void RemoveFilter(string filter)
        {
            if (!string.IsNullOrEmpty(filter) && !Filters.IsNullOrEmpty())
            {
                var filters = Filters.ToList();
                filters.RemoveAll(o => o == filter);
                if (filters.Count > 0)
                {
                    Filters = filters;
                }
                else Filters = null;
            }
        }


        /// <summary>
        /// Adds a Target to use when the Route is matched
        /// </summary>
        public void AddTarget(TrakHoundServiceTargetConfiguration target)
        {
            if (target != null)
            {
                var targets = Targets?.ToList();
                if (targets == null) targets = new List<TrakHoundServiceTargetConfiguration>();
                targets.Add(target);
                Targets = targets;
            }
        }

        /// <summary>
        /// Removes the Target with the specified Id (Targets is set to null when none remain)
        /// </summary>
        public void RemoveTarget(string id)
        {
            if (!string.IsNullOrEmpty(id) && !Targets.IsNullOrEmpty())
            {
                var targets = Targets.ToList();
                targets.RemoveAll(o => o.Id == id);
                if (targets.Count > 0)
                {
                    Targets = targets;
                }
                else Targets = null;
            }
        }
    }
}
