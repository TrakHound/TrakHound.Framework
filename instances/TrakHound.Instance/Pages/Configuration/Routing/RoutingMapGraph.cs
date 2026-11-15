// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services;

namespace TrakHound.Instance.Pages.Configuration.Routing
{
    public enum RoutingMapNodeKind
    {
        /// <summary>
        /// The Modules of one type (ex. Workers) that use the same Router
        /// </summary>
        Modules,

        Router,

        /// <summary>
        /// A Router that Modules name, but that doesn't exist
        /// </summary>
        MissingRouter,

        Route,

        /// <summary>
        /// One kind of Request (ex. "KeyValues") a Route handles, linked to the Targets that handle it
        /// </summary>
        Lane,

        Target,

        Service
    }

    public enum RoutingMapEdgeKind
    {
        /// <summary>
        /// Modules use the Router (or call a Service directly)
        /// </summary>
        Uses,

        /// <summary>
        /// A Router's Route, a Route's kinds of Request (Lanes), or a Lane's Targets
        /// </summary>
        Connection,

        /// <summary>
        /// A Router Target sends Requests on to another Router
        /// </summary>
        RouterTarget,

        /// <summary>
        /// A Service Target sends Requests to the Service
        /// </summary>
        Service
    }

    public class RoutingMapNode
    {
        public string Key { get; init; }

        public RoutingMapNodeKind Kind { get; init; }

        public string Title { get; set; }

        public string Subtitle { get; set; }

        /// <summary>
        /// The Router, Route or Target (null for other nodes)
        /// </summary>
        public RoutingItem Item { get; init; }

        /// <summary>
        /// The Modules (Modules nodes only)
        /// </summary>
        public List<RoutingModule> Modules { get; } = new List<RoutingModule>();

        public ConfigurationCategoryDefinition Category { get; init; }

        /// <summary>
        /// The kind of Request (Lane nodes only, ex. "KeyValues")
        /// </summary>
        public string Route { get; init; }

        /// <summary>
        /// The Service (Service nodes only)
        /// </summary>
        public TrakHoundServiceRouteService Service { get; init; }

        /// <summary>
        /// True if nothing sends Requests to it (a Router no Module or Router Target uses, or a Service no Target sends to)
        /// </summary>
        public bool IsUnused { get; set; }

        /// <summary>
        /// Why the item won't work as configured (ex. a Service that isn't configured)
        /// </summary>
        public string Problem { get; set; }

        /// <summary>
        /// The column it is shown in (left to right)
        /// </summary>
        public int Column => Kind switch
        {
            RoutingMapNodeKind.Modules => 0,
            RoutingMapNodeKind.Router or RoutingMapNodeKind.MissingRouter => 1,
            RoutingMapNodeKind.Route => 2,
            RoutingMapNodeKind.Lane => 3,
            RoutingMapNodeKind.Target => 4,
            _ => 5
        };
    }

    public class RoutingMapEdge
    {
        public string From { get; init; }

        public string To { get; init; }

        public RoutingMapEdgeKind Kind { get; init; }

        public string Key => $"{From}|{To}|{Kind}";
    }

    /// <summary>
    /// The Routers as a graph, left to right: the Modules that use each Router, its Routes, the kinds of Request each Route handles (Lanes),
    /// the Targets that handle each kind (in order) and the Services they send Requests to.
    /// Built from the Routers being edited (saved or not), so it shows where Service calls would go once they are saved
    /// </summary>
    public class RoutingMapGraph
    {
        private readonly Dictionary<string, RoutingMapNode> _nodes = new Dictionary<string, RoutingMapNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, RoutingMapEdge> _edges = new Dictionary<string, RoutingMapEdge>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<RoutingMapEdge>> _outgoing = new Dictionary<string, List<RoutingMapEdge>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<RoutingMapEdge>> _incoming = new Dictionary<string, List<RoutingMapEdge>>(StringComparer.Ordinal);


        public IEnumerable<RoutingMapNode> Nodes => _nodes.Values;

        public IEnumerable<RoutingMapEdge> Edges => _edges.Values;

        /// <summary>
        /// The Router Configurations the graph was built from (for <see cref="TrakHoundServiceRouteResolver.Resolve"/>)
        /// </summary>
        public IReadOnlyList<TrakHoundServiceRouterConfiguration> Routers { get; private set; } = Array.Empty<TrakHoundServiceRouterConfiguration>();

        public IReadOnlyList<TrakHoundServiceRouteService> Services { get; private set; } = Array.Empty<TrakHoundServiceRouteService>();


        public RoutingMapNode GetNode(string key) => key != null ? _nodes.GetValueOrDefault(key) : null;

        public IEnumerable<RoutingMapEdge> GetOutgoing(string key) => key != null ? _outgoing.GetValueOrDefault(key) ?? Enumerable.Empty<RoutingMapEdge>() : Enumerable.Empty<RoutingMapEdge>();

        public IEnumerable<RoutingMapEdge> GetIncoming(string key) => key != null ? _incoming.GetValueOrDefault(key) ?? Enumerable.Empty<RoutingMapEdge>() : Enumerable.Empty<RoutingMapEdge>();

        public static string GetServiceKey(string serviceId) => $"service:{serviceId}";

        public static string GetLaneKey(string routeId, string route) => $"lane:{routeId}:{route}";


        /// <summary>
        /// Builds the graph
        /// </summary>
        /// <param name="model">The Routers being edited</param>
        /// <param name="modules">The Modules that make Service calls (and the Router each uses)</param>
        /// <param name="services">The configured Services (and the Routes they handle, if known)</param>
        public static RoutingMapGraph Build(RoutingModel model, IEnumerable<RoutingModule> modules, IEnumerable<TrakHoundServiceRouteService> services)
        {
            var graph = new RoutingMapGraph();
            if (model == null) return graph;

            var serviceList = services?.Where(o => !string.IsNullOrEmpty(o?.Id)).ToList() ?? new List<TrakHoundServiceRouteService>();
            graph.Services = serviceList;
            graph.Routers = model.ToConfigurations();

            // Services (every configured Service is shown, so the ones nothing sends Requests to stand out)
            foreach (var service in serviceList)
            {
                graph.AddNode(new RoutingMapNode
                {
                    Key = GetServiceKey(service.Id),
                    Kind = RoutingMapNodeKind.Service,
                    Title = !string.IsNullOrEmpty(service.Name) ? service.Name : service.Id,
                    Subtitle = service.Routes == null ? "Kinds unknown (not running)" : service.Routes.Count > 0 ? string.Join(", ", service.Routes) : "Handles no Requests",
                    Service = service
                });
            }

            // Routers, their Routes and Targets (Routes and Targets that aren't connected to a Router aren't saved, so they aren't shown)
            foreach (var router in model.Routers)
            {
                graph.AddNode(new RoutingMapNode
                {
                    Key = router.Id,
                    Kind = RoutingMapNodeKind.Router,
                    Title = !string.IsNullOrWhiteSpace(router.Name) ? router.Name.Trim() : "No Name",
                    Subtitle = router.Description,
                    Item = router
                });

                foreach (var route in router.Routes)
                {
                    graph.AddNode(new RoutingMapNode
                    {
                        Key = route.Id,
                        Kind = RoutingMapNodeKind.Route,
                        Title = route.Patterns.Count > 0 ? string.Join(", ", route.Patterns) : "No Patterns",
                        Item = route
                    });
                    graph.AddEdge(router.Id, route.Id, RoutingMapEdgeKind.Connection);

                    foreach (var target in route.Targets)
                    {
                        graph.AddNode(new RoutingMapNode
                        {
                            Key = target.Id,
                            Kind = RoutingMapNodeKind.Target,
                            Item = target
                        });
                    }
                }
            }

            // What each Target sends Requests to (and, for a Service Target, its Services)
            var targetServices = new Dictionary<string, List<TrakHoundServiceRouteService>>(StringComparer.Ordinal);
            foreach (var target in model.Routers.SelectMany(o => o.Routes).SelectMany(o => o.Targets))
            {
                var node = graph._nodes[target.Id];
                var route = (RouteItem)target.Parent;

                if (target.IsRouter)
                {
                    var targetRouter = model.Get(target.Source) as RouterItem;
                    node.Title = targetRouter != null ? graph._nodes[targetRouter.Id].Title : target.Source ?? "Not Selected";
                    node.Subtitle = "Router";

                    if (targetRouter == null) node.Problem = $"The Router ({target.Source}) doesn't exist";
                    else if (targetRouter == target.GetRouter()) node.Problem = "Sends Requests back to its own Router, so it is skipped";
                    else graph.AddEdge(target.Id, targetRouter.Id, RoutingMapEdgeKind.RouterTarget);
                }
                else
                {
                    var isWildcard = target.Source == TrakHoundServiceRoutes.Wildcard;
                    var source = serviceList.FirstOrDefault(o => o.Id == target.Source);
                    node.Title = isWildcard ? "Every matching Service" : source != null ? graph._nodes[GetServiceKey(source.Id)].Title : target.Source ?? "Not Selected";
                    node.Subtitle = "Service";

                    if (!isWildcard && source == null)
                    {
                        node.Problem = string.IsNullOrEmpty(target.Source) ? "No Service is selected" : $"The Service ({target.Source}) isn't configured";
                        continue;
                    }

                    var selected = TrakHoundServiceRouteResolver.SelectServices(
                        route.Patterns, target.Source, serviceList, o => o.Id, o => o.Id,
                        TrakHoundServiceRouteResolver.IsServicePatternMatch, TrakHoundServiceRouteResolver.IsServiceDenyMatch);

                    targetServices[target.Id] = selected;
                    if (selected.Count == 0) node.Problem = "None of its Services handle the Route's Patterns";
                    foreach (var service in selected) graph.AddEdge(target.Id, GetServiceKey(service.Id), RoutingMapEdgeKind.Service);
                }
            }

            // Lanes : each kind of Request a Route's Patterns match, linked to the Targets that handle it (in the order they are tried).
            // A Router Target is linked to every Lane (its Router decides what it handles)
            foreach (var route in model.Routers.SelectMany(o => o.Routes))
            {
                var kinds = TrakHoundServiceRoutes.Names.Where(o => TrakHoundServiceRouteResolver.IsRouteMatch(route.Patterns, o)).ToList();
                if (kinds.Count == 0) graph._nodes[route.Id].Problem = "Its Patterns match no kind of Request";

                foreach (var kind in kinds)
                {
                    var laneKey = GetLaneKey(route.Id, kind);
                    var handlers = route.Targets.Where(target => target.IsRouter
                        ? graph.GetOutgoing(target.Id).Any()
                        : targetServices.TryGetValue(target.Id, out var selected) && selected.Any(o => Handles(o, kind))).ToList();

                    graph.AddNode(new RoutingMapNode
                    {
                        Key = laneKey,
                        Kind = RoutingMapNodeKind.Lane,
                        Title = kind,
                        Subtitle = handlers.Count == 0 ? "Nothing handles it" : handlers.Count == 1 ? "1 Target" : $"{handlers.Count} Targets, in order",
                        Problem = handlers.Count == 0 ? $"No Target on this Route handles {kind} Requests" : null,
                        Item = route,
                        Route = kind
                    });
                    graph.AddEdge(route.Id, laneKey, RoutingMapEdgeKind.Connection);

                    foreach (var target in handlers) graph.AddEdge(laneKey, target.Id, RoutingMapEdgeKind.Connection);
                }
            }

            // Targets no Lane uses are skipped for every kind of Request
            foreach (var target in graph.Nodes.Where(o => o.Kind == RoutingMapNodeKind.Target && !graph.GetIncoming(o.Key).Any()))
            {
                target.Problem ??= "Handles none of the kinds of Request its Route matches, so it is always skipped";
            }

            // Modules, grouped by the Router they use and their type
            foreach (var module in modules?.Where(o => o != null) ?? Enumerable.Empty<RoutingModule>())
            {
                string toKey;
                var router = model.FindRouter(module.RouterKey);
                if (router != null)
                {
                    toKey = router.Id;
                }
                else if (module.RouterKey != null && serviceList.Any(o => o.Id == module.RouterKey))
                {
                    // A Service's ID sends the calls straight to that Service
                    toKey = GetServiceKey(module.RouterKey);
                }
                else
                {
                    var name = module.RouterKey ?? TrakHoundServiceRouter.Default;
                    toKey = $"missing:{name.ToLowerInvariant()}";
                    if (!graph._nodes.ContainsKey(toKey))
                    {
                        graph.AddNode(new RoutingMapNode
                        {
                            Key = toKey,
                            Kind = RoutingMapNodeKind.MissingRouter,
                            Title = name,
                            Subtitle = "Doesn't exist",
                            Problem = module.RouterKey == null ? "There are no Routers" : $"No Router has the ID or Name \"{name}\""
                        });
                    }
                }

                var groupKey = $"modules:{toKey}:{module.Category?.Key}";
                if (!graph._nodes.TryGetValue(groupKey, out var group))
                {
                    group = new RoutingMapNode
                    {
                        Key = groupKey,
                        Kind = RoutingMapNodeKind.Modules,
                        Title = module.Category?.Title,
                        Category = module.Category
                    };
                    graph.AddNode(group);
                    graph.AddEdge(groupKey, toKey, RoutingMapEdgeKind.Uses);
                }
                group.Modules.Add(module);
            }

            foreach (var group in graph.Nodes.Where(o => o.Kind == RoutingMapNodeKind.Modules))
            {
                group.Modules.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
                group.Subtitle = $"{group.Modules.Count} {(group.Modules.Count == 1 ? group.Category?.SingularTitle : group.Category?.Title)}";
            }

            // Routers and Services that nothing sends Requests to
            foreach (var node in graph.Nodes.Where(o => o.Kind == RoutingMapNodeKind.Router || o.Kind == RoutingMapNodeKind.Service))
            {
                node.IsUnused = !graph.GetIncoming(node.Key).Any();
            }

            return graph;
        }

        private void AddNode(RoutingMapNode node) => _nodes[node.Key] = node;

        private void AddEdge(string from, string to, RoutingMapEdgeKind kind)
        {
            var edge = new RoutingMapEdge { From = from, To = to, Kind = kind };
            if (!_edges.TryAdd(edge.Key, edge)) return;

            if (!_outgoing.TryGetValue(from, out var outgoing)) _outgoing[from] = outgoing = new List<RoutingMapEdge>();
            outgoing.Add(edge);
            if (!_incoming.TryGetValue(to, out var incoming)) _incoming[to] = incoming = new List<RoutingMapEdge>();
            incoming.Add(edge);
        }


        /// <summary>
        /// The nodes and edges a Request for the Route (ex. "KeyValues") can reach: the Lanes for it, their Targets,
        /// and the Services that handle it (or might). Modules, Routers and Routes that match it are included. Null includes everything
        /// </summary>
        public HashSet<string> GetActive(string route)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            if (route == null)
            {
                foreach (var node in Nodes) active.Add(node.Key);
                foreach (var edge in Edges) active.Add(edge.Key);
                return active;
            }

            foreach (var node in Nodes)
            {
                switch (node.Kind)
                {
                    case RoutingMapNodeKind.Modules:
                    case RoutingMapNodeKind.Router:
                    case RoutingMapNodeKind.MissingRouter:
                        active.Add(node.Key);
                        break;

                    case RoutingMapNodeKind.Route:
                        if (TrakHoundServiceRouteResolver.IsRouteMatch(((RouteItem)node.Item).Patterns, route)) active.Add(node.Key);
                        break;

                    case RoutingMapNodeKind.Lane:
                        if (node.Route == route) active.Add(node.Key);
                        break;
                }
            }

            // Targets of the Lanes for this kind, and the Services they send it to
            foreach (var lane in Nodes.Where(o => o.Kind == RoutingMapNodeKind.Lane && active.Contains(o.Key)).ToList())
            {
                foreach (var laneEdge in GetOutgoing(lane.Key))
                {
                    active.Add(laneEdge.Key);
                    active.Add(laneEdge.To);

                    foreach (var edge in GetOutgoing(laneEdge.To))
                    {
                        if (edge.Kind == RoutingMapEdgeKind.RouterTarget || Handles(GetNode(edge.To)?.Service, route))
                        {
                            active.Add(edge.Key);
                            active.Add(edge.To);
                        }
                    }
                }
            }

            // Modules that call a Service directly
            foreach (var edge in Edges.Where(o => o.Kind == RoutingMapEdgeKind.Uses))
            {
                var to = GetNode(edge.To);
                if (to?.Kind != RoutingMapNodeKind.Service || Handles(to.Service, route))
                {
                    active.Add(edge.Key);
                    active.Add(edge.To);
                }
            }

            // Router to Route, and Route to Lane
            foreach (var edge in Edges.Where(o => o.Kind == RoutingMapEdgeKind.Connection && GetNode(o.To)?.Kind != RoutingMapNodeKind.Target))
            {
                if (active.Contains(edge.From) && active.Contains(edge.To)) active.Add(edge.Key);
            }

            return active;
        }

        private static bool Handles(TrakHoundServiceRouteService service, string route) => service != null && (service.Routes == null || service.Routes.Contains(route));

        /// <summary>
        /// The nodes and edges connected to the node: where its Requests go (following Router Targets) and what sends Requests to it
        /// </summary>
        public HashSet<string> GetConnected(string key)
        {
            var connected = new HashSet<string>(StringComparer.Ordinal);
            if (GetNode(key) == null) return connected;

            connected.Add(key);
            Walk(key, true, connected);
            Walk(key, false, connected);
            return connected;
        }

        private void Walk(string start, bool forward, HashSet<string> connected)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { start };
            var queue = new Queue<string>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var key = queue.Dequeue();
                foreach (var edge in forward ? GetOutgoing(key) : GetIncoming(key))
                {
                    connected.Add(edge.Key);
                    var next = forward ? edge.To : edge.From;
                    connected.Add(next);
                    if (visited.Add(next)) queue.Enqueue(next);
                }
            }
        }
    }
}
