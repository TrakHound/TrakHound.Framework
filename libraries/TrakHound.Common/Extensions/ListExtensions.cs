// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace TrakHound
{
    /// <summary>
    /// Extension methods for collections
    /// </summary>
    public static class ListExtensions
    {
        /// <summary>
        /// Determines whether the collection is null or contains no elements.
        /// </summary>
        /// <typeparam name="T">The IEnumerable type.</typeparam>
        /// <param name="enumerable">The enumerable, which may be null or empty.</param>
        /// <returns>
        ///     <c>true</c> if the IEnumerable is null or empty; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsNullOrEmpty<T>([NotNullWhen(false)] this IEnumerable<T> enumerable)
        {
            if (enumerable == null) return true;

            // O(1) for ICollection<T>, IReadOnlyCollection<T>, ICollection, arrays and sized LINQ iterators
            if (enumerable.TryGetNonEnumeratedCount(out var count)) return count < 1;

            return !enumerable.Any();
        }

        /// <summary>
        /// Returns the distinct strings in the collection, preserving first-seen order.
        /// Returns null if the input is null.
        /// </summary>
        public static IEnumerable<string> ToDistinct(this IEnumerable<string> strings, IEqualityComparer<string> comparer = null)
        {
            return strings == null ? null : strings.Distinct(comparer);
        }
    }
}
