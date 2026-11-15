// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Packages
{
    /// <summary>
    /// Orders a group of Packages so that each one comes after the Packages in the group that it depends on
    /// </summary>
    public static class TrakHoundPackageDependencyOrder
    {
        /// <summary>
        /// Order the items so that each one comes after the items it depends on. Items keep their original order unless a Dependency requires otherwise.
        /// Dependencies on Packages that aren't in the group are ignored
        /// </summary>
        /// <param name="items">The items to order</param>
        /// <param name="getId">Gets the Package ID of an item (an item without one has nothing depending on it)</param>
        /// <param name="getDependencyIds">Gets the IDs of the Packages an item depends on</param>
        /// <param name="cyclic">The items that were placed before one of their Dependencies because the Dependencies form a cycle</param>
        public static List<T> Sort<T>(IEnumerable<T> items, Func<T, string> getId, Func<T, IEnumerable<string>> getDependencyIds, out List<T> cyclic)
        {
            cyclic = new List<T>();

            var remaining = items != null ? items.ToList() : new List<T>();
            var sorted = new List<T>(remaining.Count);

            // The IDs that haven't been placed yet (an ID listed more than once is placed when its last item is)
            var pending = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in remaining)
            {
                var id = getId(item);
                if (!string.IsNullOrEmpty(id)) pending[id] = pending.GetValueOrDefault(id) + 1;
            }

            while (remaining.Count > 0)
            {
                // The first item (in the original order) whose Dependencies have all been placed
                var index = remaining.FindIndex(item => !HasPendingDependency(item, getId, getDependencyIds, pending));
                if (index < 0)
                {
                    // Every remaining item waits on another one, so break the cycle at the first
                    index = 0;
                    cyclic.Add(remaining[0]);
                }

                var next = remaining[index];
                remaining.RemoveAt(index);
                sorted.Add(next);

                var nextId = getId(next);
                if (!string.IsNullOrEmpty(nextId) && pending.TryGetValue(nextId, out var count))
                {
                    if (count > 1) pending[nextId] = count - 1;
                    else pending.Remove(nextId);
                }
            }

            return sorted;
        }

        private static bool HasPendingDependency<T>(T item, Func<T, string> getId, Func<T, IEnumerable<string>> getDependencyIds, Dictionary<string, int> pending)
        {
            var dependencyIds = getDependencyIds(item);
            if (dependencyIds == null) return false;

            var id = getId(item);
            foreach (var dependencyId in dependencyIds)
            {
                if (string.IsNullOrEmpty(dependencyId)) continue;

                // A Package that lists itself doesn't wait on itself
                if (string.Equals(dependencyId, id, StringComparison.OrdinalIgnoreCase)) continue;

                if (pending.ContainsKey(dependencyId)) return true;
            }

            return false;
        }
    }
}
