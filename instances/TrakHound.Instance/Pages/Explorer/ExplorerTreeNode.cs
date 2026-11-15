// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;

namespace TrakHound.Instance.Pages.Explorer
{
    /// <summary>
    /// An item in the Explorer Tree (built from the Explorer data, then filtered and rendered)
    /// </summary>
    public class ExplorerTreeNode
    {
        /// <summary>
        /// Unique (per Instance) ID used to track the Expanded state
        /// </summary>
        public string Id { get; set; }

        public string Title { get; set; }

        /// <summary>
        /// Secondary text shown after the Title (ex. a count or status)
        /// </summary>
        public string Subtitle { get; set; }

        /// <summary>
        /// The Icon (Folders use an open / closed folder Icon based on their Expanded state)
        /// </summary>
        public string Icon { get; set; }

        /// <summary>
        /// Tooltip for the Icon
        /// </summary>
        public string IconLabel { get; set; }

        public bool IsFolder { get; set; }

        public string CssClass { get; set; }

        public string Link { get; set; }

        public bool MatchLinkQueryParameters { get; set; }

        /// <summary>
        /// Shown as active although the URL isn't exactly its Link (ex. a Logger opened with "view=history")
        /// </summary>
        public bool IsCurrent { get; set; }

        /// <summary>
        /// Custom Header (replaces the Icon and Title)
        /// </summary>
        public RenderFragment Header { get; set; }

        /// <summary>
        /// Additional text matched by the Filter (besides the Title)
        /// </summary>
        public string SearchText { get; set; }

        /// <summary>
        /// Loads the Children when the Node is Expanded (ex. Service Tables)
        /// </summary>
        public Func<Task> Load { get; set; }

        public bool IsLoading { get; set; }

        public List<ExplorerTreeNode> Children { get; } = new List<ExplorerTreeNode>();

        // Set by the Filter
        public bool IsVisible { get; set; } = true;
        public bool IsForcedExpanded { get; set; }


        public bool IsExpandable => IsFolder || Load != null || Children.Count > 0;


        public ExplorerTreeNode Add(ExplorerTreeNode child)
        {
            if (child != null) Children.Add(child);
            return child;
        }

        /// <summary>
        /// Applies a Filter to the Node and its descendants. Returns true if the Node or a descendant matches.
        /// A Node is visible if it, an ancestor, or a descendant matches. Nodes with matching descendants are expanded.
        /// </summary>
        public bool ApplyFilter(string filter, bool ancestorMatched = false)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                IsVisible = true;
                IsForcedExpanded = false;
                foreach (var child in Children) child.ApplyFilter(filter);
                return true;
            }

            var selfMatch = Matches(Title, filter) || Matches(SearchText, filter);

            var descendantMatch = false;
            foreach (var child in Children)
            {
                if (child.ApplyFilter(filter, ancestorMatched || selfMatch)) descendantMatch = true;
            }

            IsVisible = ancestorMatched || selfMatch || descendantMatch;
            IsForcedExpanded = descendantMatch;

            return selfMatch || descendantMatch;
        }

        /// <summary>
        /// Gets the IDs of this Node and its descendants that can be Expanded (excludes Nodes that load on Expand)
        /// </summary>
        public IEnumerable<string> GetExpandableIds()
        {
            if (!string.IsNullOrEmpty(Id) && IsExpandable && Load == null) yield return Id;

            foreach (var child in Children)
            {
                foreach (var id in child.GetExpandableIds()) yield return id;
            }
        }

        private static bool Matches(string text, string filter)
        {
            return !string.IsNullOrEmpty(text) && text.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
