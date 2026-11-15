// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.RegularExpressions;
using TrakHound.Services;

namespace TrakHound.Instance.Pages.Configuration.Routing
{
    public enum RoutingItemKind
    {
        Router,
        Route,
        Target
    }

    /// <summary>
    /// An item being edited on the Routing page (a Router, Route or Target). Items form the same tree as the Router Configurations:
    /// Router → Route → Targets (tried in order)
    /// </summary>
    public abstract class RoutingItem
    {
        internal readonly List<RoutingItem> ChildList = new List<RoutingItem>();


        public string Id { get; }

        public abstract RoutingItemKind Kind { get; }

        /// <summary>
        /// The item this one is connected to (null for a Router, or for an item that isn't connected yet)
        /// </summary>
        public RoutingItem Parent { get; internal set; }

        public IReadOnlyList<RoutingItem> Children => ChildList;


        protected RoutingItem(string id)
        {
            Id = !string.IsNullOrEmpty(id) ? id : Guid.NewGuid().ToString();
        }


        /// <summary>
        /// The Router this item is connected to (directly or through other items). Null if it isn't connected to one
        /// </summary>
        public RouterItem GetRouter()
        {
            var item = this;
            while (item.Parent != null) item = item.Parent;
            return item as RouterItem;
        }

        public bool IsAncestorOf(RoutingItem item)
        {
            for (var parent = item?.Parent; parent != null; parent = parent.Parent)
            {
                if (parent == this) return true;
            }
            return false;
        }

        public IEnumerable<RoutingItem> GetDescendants()
        {
            foreach (var child in ChildList)
            {
                yield return child;
                foreach (var descendant in child.GetDescendants()) yield return descendant;
            }
        }

        /// <summary>
        /// The values of the item (used to tell if anything has changed)
        /// </summary>
        internal abstract string Describe();
    }

    public class RouterItem : RoutingItem
    {
        public override RoutingItemKind Kind => RoutingItemKind.Router;

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The Name the Router was loaded with (null for a new Router). Modules that use the Router by Name still use this one until they are changed
        /// </summary>
        public string SavedName { get; set; }

        public IEnumerable<RouteItem> Routes => ChildList.OfType<RouteItem>();


        public RouterItem(string id = null) : base(id) { }

        internal override string Describe() => $"{Name}|{Description}";
    }

    public class RouteItem : RoutingItem
    {
        public override RoutingItemKind Kind => RoutingItemKind.Route;

        /// <summary>
        /// The kinds of Services the Route handles (ex. "*" or "KeyValues")
        /// </summary>
        public List<string> Patterns { get; set; } = new List<string>();

        public List<string> Filters { get; set; } = new List<string>();

        public IEnumerable<TargetItem> Targets => ChildList.OfType<TargetItem>();


        public RouteItem(string id = null) : base(id) { }

        internal override string Describe() => $"{string.Join('\u001f', Patterns)}|{string.Join('\u001f', Filters)}";
    }

    public class TargetItem : RoutingItem
    {
        public const string ServiceType = "Service";
        public const string RouterType = "Router";


        public override RoutingItemKind Kind => RoutingItemKind.Target;

        /// <summary>
        /// "Service" or "Router"
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// The ID of the Service or Router that Requests are sent to
        /// </summary>
        public string Source { get; set; }

        public bool IsRouter => string.Equals(Type, RouterType, StringComparison.OrdinalIgnoreCase);

        public bool IsService => string.Equals(Type, ServiceType, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The results (names of TrakHoundServiceResultType, ex. "NotFound") that send the Request on to the Route's next Target.
        /// Null uses the default (TrakHoundServiceFallback.Default)
        /// </summary>
        public List<string> FallbackOn { get; set; }

        /// <summary>
        /// The results the Target falls back on (the default if it doesn't list its own)
        /// </summary>
        public IReadOnlyList<string> EffectiveFallbackOn => FallbackOn ?? TrakHoundServiceFallback.Default.Select(o => o.ToString()).ToList();


        public TargetItem(string id = null) : base(id) { }

        internal override string Describe() => $"{Type}|{Source}|{(FallbackOn != null ? string.Join('\u001f', FallbackOn) : "default")}";
    }


    public enum RoutingIssueSeverity
    {
        Error,
        Warning
    }

    /// <summary>
    /// A problem with an item. Errors prevent the Routing from being saved
    /// </summary>
    public class RoutingIssue
    {
        public string ItemId { get; }

        public RoutingIssueSeverity Severity { get; }

        public string Message { get; }


        public RoutingIssue(string itemId, RoutingIssueSeverity severity, string message)
        {
            ItemId = itemId;
            Severity = severity;
            Message = message;
        }

        public override string ToString() => $"{Severity} : {ItemId} : {Message}";
    }


    /// <summary>
    /// The Routers being edited on the Routing page. Read from (and written to) the Router Configurations, independent of the diagram that shows them
    /// </summary>
    public class RoutingModel
    {
        private readonly Dictionary<string, RoutingItem> _items = new Dictionary<string, RoutingItem>(StringComparer.Ordinal);


        public IEnumerable<RoutingItem> Items => _items.Values;

        public IEnumerable<RouterItem> Routers => _items.Values.OfType<RouterItem>();

        /// <summary>
        /// Items (other than Routers) that aren't connected to anything. They aren't saved
        /// </summary>
        public IEnumerable<RoutingItem> Unconnected => _items.Values.Where(o => o.Parent == null && o.Kind != RoutingItemKind.Router);


        #region "Items"

        public RoutingItem Get(string id) => id != null ? _items.GetValueOrDefault(id) : null;

        public bool Contains(RoutingItem item) => item != null && _items.TryGetValue(item.Id, out var existing) && existing == item;

        public T Add<T>(T item) where T : RoutingItem
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (_items.ContainsKey(item.Id)) throw new ArgumentException($"An item with the ID ({item.Id}) has already been added");

            _items.Add(item.Id, item);
            return item;
        }

        /// <summary>
        /// Removes the item. Its children are kept, but are no longer connected
        /// </summary>
        public void Remove(RoutingItem item)
        {
            if (!Contains(item)) return;

            Disconnect(item);
            foreach (var child in item.ChildList.ToList()) Disconnect(child);

            _items.Remove(item.Id);
        }

        #endregion

        #region "Connections"

        /// <summary>
        /// Whether an item of the Child Kind can be connected to an item of the Parent Kind
        /// </summary>
        public static bool IsAllowed(RoutingItemKind parent, RoutingItemKind child)
        {
            switch (parent)
            {
                case RoutingItemKind.Router: return child == RoutingItemKind.Route;
                case RoutingItemKind.Route: return child == RoutingItemKind.Target;
                default: return false;
            }
        }

        /// <summary>
        /// Whether the Child can be connected to the Parent: the Kinds must be allowed, the Child can only have one Parent, and the connection can't create a loop
        /// </summary>
        public bool CanConnect(RoutingItem parent, RoutingItem child)
        {
            return Contains(parent) && Contains(child) && parent != child
                && IsAllowed(parent.Kind, child.Kind)
                && child.Parent == null
                && !child.IsAncestorOf(parent);
        }

        public bool Connect(RoutingItem parent, RoutingItem child)
        {
            if (!CanConnect(parent, child)) return false;

            child.Parent = parent;
            parent.ChildList.Add(child);
            return true;
        }

        public void Disconnect(RoutingItem child)
        {
            if (child?.Parent == null) return;

            child.Parent.ChildList.Remove(child);
            child.Parent = null;
        }

        /// <summary>
        /// Moves the item earlier (negative Offset) or later among its Parent's children (ex. the order a Route's Targets are tried in)
        /// </summary>
        public bool Move(RoutingItem child, int offset)
        {
            var siblings = child?.Parent?.ChildList;
            if (siblings == null) return false;

            var index = siblings.IndexOf(child);
            var newIndex = index + offset;
            if (index < 0 || newIndex < 0 || newIndex >= siblings.Count || newIndex == index) return false;

            siblings.RemoveAt(index);
            siblings.Insert(newIndex, child);
            return true;
        }

        #endregion

        #region "Names"

        public string GetUniqueRouterName()
        {
            var names = new HashSet<string>(Routers.Select(o => o.Name).Where(o => o != null), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(TrakHoundServiceRouter.Default)) return TrakHoundServiceRouter.Default;
            if (!names.Contains("router")) return "router";

            var i = 2;
            while (names.Contains($"router-{i}")) i++;
            return $"router-{i}";
        }

        #endregion

        #region "Configurations"

        /// <summary>
        /// Reads the Router Configurations. Every Router, Route and Target in them is included
        /// </summary>
        public static RoutingModel FromConfigurations(IEnumerable<TrakHoundServiceRouterConfiguration> configurations)
        {
            var model = new RoutingModel();

            if (configurations != null)
            {
                foreach (var configuration in configurations.Where(o => o != null))
                {
                    var router = model.Add(new RouterItem(model.GetAvailableId(configuration.Id)));
                    router.Name = configuration.Name;
                    router.SavedName = configuration.Name;
                    router.Description = configuration.Description;

                    foreach (var routeConfiguration in configuration.Routes ?? Enumerable.Empty<TrakHoundServiceRouteConfiguration>())
                    {
                        if (routeConfiguration == null) continue;

                        var route = model.Add(new RouteItem(model.GetAvailableId(routeConfiguration.Id)));
                        route.Patterns = routeConfiguration.Patterns?.Where(o => o != null).ToList() ?? new List<string>();
                        route.Filters = routeConfiguration.Filters?.Where(o => o != null).ToList() ?? new List<string>();
                        model.Connect(router, route);

                        // The same order the Router tries them in
                        foreach (var targetConfiguration in routeConfiguration.Targets ?? Enumerable.Empty<TrakHoundServiceTargetConfiguration>())
                        {
                            if (targetConfiguration == null) continue;

                            var target = model.Add(new TargetItem(model.GetAvailableId(targetConfiguration.Id)));
                            target.Type = targetConfiguration.Type;
                            target.Source = targetConfiguration.Source;
                            target.FallbackOn = targetConfiguration.FallbackOn?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList();
                            model.Connect(route, target);
                        }
                    }
                }
            }

            return model;
        }

        // IDs must be unique on the diagram (a Target could have been saved under more than one Route with the same ID)
        private string GetAvailableId(string id)
        {
            return !string.IsNullOrEmpty(id) && !_items.ContainsKey(id) ? id : Guid.NewGuid().ToString();
        }

        /// <summary>
        /// Creates a Configuration for each Router (with everything connected to it)
        /// </summary>
        public List<TrakHoundServiceRouterConfiguration> ToConfigurations()
        {
            return Routers.OrderBy(o => o.Id, StringComparer.Ordinal).Select(ToConfiguration).ToList();
        }

        private static TrakHoundServiceRouterConfiguration ToConfiguration(RouterItem router)
        {
            var configuration = new TrakHoundServiceRouterConfiguration();
            configuration.Id = router.Id;
            configuration.Name = router.Name;
            configuration.Description = !string.IsNullOrEmpty(router.Description) ? router.Description : null;

            foreach (var route in router.Routes)
            {
                var routeConfiguration = new TrakHoundServiceRouteConfiguration();
                routeConfiguration.Id = route.Id;
                routeConfiguration.Patterns = ToList(route.Patterns);
                routeConfiguration.Filters = ToList(route.Filters);
                foreach (var target in route.Targets) routeConfiguration.AddTarget(ToConfiguration(target));

                configuration.AddRoute(routeConfiguration);
            }

            return configuration;
        }

        private static TrakHoundServiceTargetConfiguration ToConfiguration(TargetItem target)
        {
            var configuration = new TrakHoundServiceTargetConfiguration();
            configuration.Id = target.Id;
            configuration.Type = target.Type;
            configuration.Source = target.Source;

            // Null uses the default. An empty list is saved as empty (never falls back)
            configuration.FallbackOn = target.FallbackOn?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList();

            return configuration;
        }

        private static List<string> ToList(IEnumerable<string> values)
        {
            var list = values?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).ToList();
            return list?.Count > 0 ? list : null;
        }

        /// <summary>
        /// Every item and its values (used to tell if anything has changed since it was loaded or saved)
        /// </summary>
        public string GetSnapshot()
        {
            return string.Join('\u001e', _items.Values
                .OrderBy(o => o.Id, StringComparer.Ordinal)
                .Select(o => $"{o.Kind}|{o.Id}|{o.Parent?.Id}|{o.Parent?.ChildList.IndexOf(o)}|{o.Describe()}"));
        }

        #endregion

        #region "Validation"

        /// <summary>
        /// Describes why a Route Pattern never matches (null if it matches at least one Route). Patterns are case-sensitive
        /// Regular Expressions, so a typo (ex. "keyvalues") is saved but never used. A "!" Pattern excludes Services instead.
        /// </summary>
        public static string GetPatternWarning(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return null;

            var value = pattern.StartsWith('!') ? pattern.Substring(1) : pattern;
            if (value == TrakHoundServiceRoutes.Wildcard) return null;

            try
            {
                if (TrakHoundServiceRoutes.Names.Any(o => o == value || Regex.IsMatch(o, value))) return null;
            }
            catch (ArgumentException)
            {
                return $"The Pattern \"{pattern}\" isn't a valid Regular Expression";
            }

            return $"The Pattern \"{pattern}\" doesn't match any Route ({string.Join(", ", TrakHoundServiceRoutes.Names)}). Patterns are case-sensitive";
        }

        /// <summary>
        /// The Router that Service calls naming the Router Key go through once the Routers are saved (null for none, ex. a Key that is a Service's ID).
        /// The same lookup the Instance uses: by ID, then by Name. With no Key, the Router named "default" (or the first Router)
        /// </summary>
        public RouterItem FindRouter(string routerKey)
        {
            var routers = Routers.ToList();
            var configurations = routers.Select(o => new TrakHoundServiceRouterConfiguration { Id = o.Id, Name = o.Name?.Trim() });
            var configuration = TrakHoundServiceRouteResolver.FindRouter(configurations, routerKey);
            return configuration != null ? routers.FirstOrDefault(o => o.Id == configuration.Id) : null;
        }

        /// <summary>
        /// Checks every item. Services that don't exist are only checked if ServiceExists is set, and the Routers Modules use only if Modules is set
        /// </summary>
        public List<RoutingIssue> Validate(Func<string, bool> serviceExists = null, IEnumerable<RoutingModule> modules = null)
        {
            var issues = new List<RoutingIssue>();
            void Error(RoutingItem item, string message) => issues.Add(new RoutingIssue(item.Id, RoutingIssueSeverity.Error, message));
            void Warning(RoutingItem item, string message) => issues.Add(new RoutingIssue(item?.Id, RoutingIssueSeverity.Warning, message));

            if (modules != null) ValidateModules(modules, serviceExists, Warning);

            // Routers are used by Name, so Names must be unique
            var names = Routers.Where(o => !string.IsNullOrWhiteSpace(o.Name)).GroupBy(o => o.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(o => o.Count() > 1).Select(o => o.Key);
            var duplicateNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

            var loops = GetRouterLoops();

            foreach (var item in _items.Values)
            {
                if (item.Kind != RoutingItemKind.Router && item.GetRouter() == null)
                {
                    Warning(item, "Not connected to a Router, so it won't be saved");
                }

                switch (item)
                {
                    case RouterItem router:
                        if (string.IsNullOrWhiteSpace(router.Name)) Error(router, "A Name is required");
                        else if (duplicateNames.Contains(router.Name.Trim())) Error(router, $"Another Router is also named \"{router.Name.Trim()}\"");
                        if (!router.Routes.Any()) Warning(router, "Has no Routes, so it won't handle any Requests");
                        break;

                    case RouteItem route:
                        if (!route.Patterns.Any(o => !string.IsNullOrWhiteSpace(o))) Error(route, "At least one Pattern is required (use * for every Request)");
                        if (!route.Targets.Any()) Warning(route, "Has no Targets, so its Requests aren't sent anywhere");
                        foreach (var pattern in route.Patterns)
                        {
                            var patternWarning = GetPatternWarning(pattern);
                            if (patternWarning != null) Warning(route, patternWarning);
                        }
                        break;

                    case TargetItem target:
                        if (string.IsNullOrEmpty(target.Source))
                        {
                            Error(target, "A Service or Router is required");
                        }
                        else if (target.IsRouter)
                        {
                            var targetRouter = Get(target.Source) as RouterItem;
                            if (targetRouter == null) Error(target, $"The Router ({target.Source}) doesn't exist");
                            else if (loops.Contains(target)) Error(target, $"Requests would loop back to the \"{target.GetRouter()?.Name}\" Router");
                        }
                        else if (target.IsService)
                        {
                            if (serviceExists != null && !serviceExists(target.Source)) Warning(target, $"The Service ({target.Source}) isn't configured");
                        }
                        else
                        {
                            Error(target, $"The Target Type ({target.Type}) isn't supported");
                        }
                        break;
                }
            }

            return issues;
        }

        /// <summary>
        /// Warns about Modules whose Service calls wouldn't go through the Router they did when the Routers were loaded (a Router was renamed or removed),
        /// or that name a Router that doesn't exist. Warnings that aren't about one Router have no item
        /// </summary>
        private void ValidateModules(IEnumerable<RoutingModule> modules, Func<string, bool> serviceExists, Action<RoutingItem, string> warning)
        {
            var routers = Routers.ToList();

            foreach (var group in modules.Where(o => o != null).GroupBy(o => o.RouterKey ?? "", StringComparer.OrdinalIgnoreCase))
            {
                var key = group.Key;
                var count = group.Count();
                var names = RoutingModules.Describe(group);
                var uses = count == 1 ? "uses" : "use";
                var router = FindRouter(key != "" ? key : null);

                if (key == "")
                {
                    if (router == null)
                    {
                        warning(null, $"There are no Routers, so the Service calls of {names} (which {uses} the default Router) aren't routed");
                    }
                    else if (router.Name?.Trim() != TrakHoundServiceRouter.Default)
                    {
                        warning(router, $"No Router is named \"{TrakHoundServiceRouter.Default}\", so {names} (which {uses} the default Router) {uses} this one");
                    }
                }
                else if (router == null && (serviceExists == null || !serviceExists(key)))
                {
                    // Renamed : the Router the Modules used is still here, under another Name
                    var renamed = routers.FirstOrDefault(o => string.Equals(o.SavedName?.Trim(), key, StringComparison.OrdinalIgnoreCase));
                    if (renamed != null)
                    {
                        warning(renamed, $"{names} {uses} this Router by its saved Name \"{key}\". After renaming it, their Service calls aren't routed");
                    }
                    else
                    {
                        warning(null, $"{names} {uses} the Router \"{key}\", which doesn't exist");
                    }
                }
            }
        }

        /// <summary>
        /// The Router Targets that send Requests back to the Router they are in (directly, or through other Routers)
        /// </summary>
        private HashSet<TargetItem> GetRouterLoops()
        {
            // Router ID => the Routers its Targets send Requests to
            var routerTargets = Items.OfType<TargetItem>()
                .Where(o => o.IsRouter && o.GetRouter() != null && Get(o.Source) is RouterItem)
                .ToList();

            var edges = routerTargets
                .GroupBy(o => o.GetRouter().Id)
                .ToDictionary(o => o.Key, o => o.Select(t => t.Source).Distinct().ToList());

            bool Reaches(string fromRouterId, string toRouterId)
            {
                var visited = new HashSet<string>();
                var queue = new Queue<string>();
                queue.Enqueue(fromRouterId);

                while (queue.Count > 0)
                {
                    var id = queue.Dequeue();
                    if (id == toRouterId) return true;
                    if (!visited.Add(id)) continue;

                    foreach (var next in edges.GetValueOrDefault(id) ?? new List<string>()) queue.Enqueue(next);
                }

                return false;
            }

            return routerTargets.Where(o => Reaches(o.Source, o.GetRouter().Id)).ToHashSet();
        }

        #endregion
    }
}
