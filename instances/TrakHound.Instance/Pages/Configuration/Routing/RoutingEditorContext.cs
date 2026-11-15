// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services;

namespace TrakHound.Instance.Pages.Configuration.Routing
{
    /// <summary>
    /// Shared by the Routing page and its node widgets (so the nodes show the current names and issues)
    /// </summary>
    public class RoutingEditorContext
    {
        private IReadOnlyDictionary<string, List<RoutingIssue>> _issues = new Dictionary<string, List<RoutingIssue>>();


        /// <summary>
        /// The name of the Service or Router a Target sends Requests to (null if it doesn't exist)
        /// </summary>
        public Func<TargetItem, string> GetTargetName { get; set; }

        /// <summary>
        /// The Service a Target sends Requests to (null if it's a Router or the Service doesn't exist)
        /// </summary>
        public Func<TargetItem, ITrakHoundServiceConfiguration> GetTargetService { get; set; }

        /// <summary>
        /// The Modules whose Service calls go through the Router
        /// </summary>
        public Func<RouterItem, List<RoutingModule>> GetModules { get; set; }

        /// <summary>
        /// Raised when items or their issues change (so the widgets are drawn again)
        /// </summary>
        public event Action Changed;


        public IReadOnlyList<RoutingIssue> GetIssues(string itemId)
        {
            return itemId != null ? (IReadOnlyList<RoutingIssue>)_issues.GetValueOrDefault(itemId) ?? Array.Empty<RoutingIssue>() : Array.Empty<RoutingIssue>();
        }

        public void SetIssues(IEnumerable<RoutingIssue> issues)
        {
            // Issues about Modules that aren't about one Router have no item, so no node shows them
            _issues = (issues ?? Enumerable.Empty<RoutingIssue>()).Where(o => o.ItemId != null).GroupBy(o => o.ItemId).ToDictionary(o => o.Key, o => o.ToList());
        }

        public void NotifyChanged() => Changed?.Invoke();
    }
}
