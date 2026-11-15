// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Zones;

namespace TrakHound.Instance.Pages.Security
{
    public enum ZoneMapNodeKind
    {
        /// <summary>
        /// A Zone and its Members
        /// </summary>
        Zone,

        /// <summary>
        /// Something a Zone's Rules allow (or a call was made to): an Api, Function, Worker, Service, Volume or a pattern (ex. "Every Service")
        /// </summary>
        Target,

        /// <summary>
        /// The Router a Zone sends its Members' Service calls through
        /// </summary>
        Router,

        /// <summary>
        /// A Module (or pattern) outside of the Zone that is allowed to call its Members, or that tried to
        /// </summary>
        Caller
    }

    public enum ZoneMapEdgeKind
    {
        /// <summary>
        /// A Zone's Rules allow its Members to access the Target
        /// </summary>
        Rule,

        /// <summary>
        /// The Caller (or another Zone) is in the Zone's Allowed Callers
        /// </summary>
        AllowedCaller,

        /// <summary>
        /// The Zone sends its Members' Service calls through the Router
        /// </summary>
        Router,

        /// <summary>
        /// Calls that were denied
        /// </summary>
        Denied,

        /// <summary>
        /// Calls that a Zone in Audit Mode would deny
        /// </summary>
        WouldDeny
    }

    public class ZoneMapMember
    {
        public string Pattern { get; set; }

        public string Title { get; set; }

        public string Icon { get; set; }

        /// <summary>
        /// The Modules a pattern matches (null for a single Module)
        /// </summary>
        public string Matches { get; set; }

        /// <summary>
        /// True if no Module matches
        /// </summary>
        public bool IsMissing { get; set; }

        /// <summary>
        /// What the Member's Package does outside of its Client (ex. "File System")
        /// </summary>
        public IReadOnlyList<string> Capabilities { get; set; } = Array.Empty<string>();
    }

    public class ZoneMapNode
    {
        public string Key { get; set; }

        public ZoneMapNodeKind Kind { get; set; }

        public string Title { get; set; }

        public string Subtitle { get; set; }

        public string Icon { get; set; }

        public string Link { get; set; }

        /// <summary>
        /// The Zone (Zone nodes only)
        /// </summary>
        public TrakHoundZoneConfiguration Zone { get; set; }

        public IReadOnlyList<ZoneMapMember> Members { get; set; } = Array.Empty<ZoneMapMember>();
    }

    public class ZoneMapEdge
    {
        public string From { get; set; }

        public string To { get; set; }

        public ZoneMapEdgeKind Kind { get; set; }

        /// <summary>
        /// What the line stands for (ex. "/status · write"), one item per Rule or call
        /// </summary>
        public List<string> Details { get; } = new List<string>();

        /// <summary>
        /// The number of calls (Denied and Would Deny lines)
        /// </summary>
        public long Count { get; set; }

        /// <summary>
        /// The recorded calls the line stands for (Denied and Would Deny lines), so they can be granted
        /// </summary>
        public List<TrakHoundZoneActivity> Activity { get; } = new List<TrakHoundZoneActivity>();
    }

    /// <summary>
    /// The Zones as a graph: Zones (with their Members), what their Rules allow, their Allowed Callers and Routers, and the calls that were denied
    /// </summary>
    public class ZoneMapGraph
    {
        private readonly Dictionary<string, ZoneMapNode> _nodes = new Dictionary<string, ZoneMapNode>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ZoneMapEdge> _edges = new Dictionary<string, ZoneMapEdge>(StringComparer.OrdinalIgnoreCase);


        public IEnumerable<ZoneMapNode> Nodes => _nodes.Values;

        public IEnumerable<ZoneMapEdge> Edges => _edges.Values;


        public ZoneMapNode GetNode(string key) => key != null ? _nodes.GetValueOrDefault(key) : null;

        public static string GetZoneKey(string zoneId) => $"zone:{zoneId}";


        /// <summary>
        /// Builds the graph
        /// </summary>
        /// <param name="zones">The Zones</param>
        /// <param name="modules">The Module Configurations (used for names and icons)</param>
        /// <param name="activity">The recorded calls that were denied (or would be)</param>
        /// <param name="getCapabilities">Gets what a Module's Package does outside of its Client (optional)</param>
        public static ZoneMapGraph Build(
            IEnumerable<TrakHoundZoneConfiguration> zones,
            ZoneModules modules,
            IEnumerable<TrakHoundZoneActivity> activity = null,
            Func<ZoneModule, IEnumerable<string>> getCapabilities = null)
        {
            var graph = new ZoneMapGraph();
            var zoneList = zones?.Where(o => !string.IsNullOrEmpty(o?.Id)).ToList() ?? new List<TrakHoundZoneConfiguration>();

            // Zones (first, so that other Zones can be linked to them)
            foreach (var zone in zoneList)
            {
                graph.AddZone(zone, modules, getCapabilities);
            }

            foreach (var zone in zoneList)
            {
                var zoneKey = GetZoneKey(zone.Id);

                // Rules
                foreach (var rule in zone.Rules ?? Enumerable.Empty<TrakHoundZoneRule>())
                {
                    if (string.IsNullOrEmpty(rule?.Resource)) continue;

                    var target = graph.AddTarget(rule.Resource, modules);
                    var permissions = rule.Permissions.IsNullOrEmpty() || rule.Permissions.Contains(TrakHoundZoneResources.Wildcard) ? "any" : string.Join(", ", rule.Permissions);
                    graph.AddEdge(zoneKey, target.Key, ZoneMapEdgeKind.Rule, JoinDetail(GetDetail(rule.Resource), permissions));
                }

                // Allowed Callers
                foreach (var caller in zone.AllowedCallers ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrEmpty(caller)) continue;

                    var callerKey = graph.GetCallerKey(caller, modules);
                    graph.AddEdge(callerKey, zoneKey, ZoneMapEdgeKind.AllowedCaller, null);
                }

                // Router
                if (!string.IsNullOrEmpty(zone.RouterId))
                {
                    var routerKey = $"router:{zone.RouterId}";
                    if (!graph._nodes.ContainsKey(routerKey))
                    {
                        graph._nodes.Add(routerKey, new ZoneMapNode
                        {
                            Key = routerKey,
                            Kind = ZoneMapNodeKind.Router,
                            Title = zone.RouterId,
                            Subtitle = "Service Router",
                            Icon = "fa-regular fa-sharp fa-chart-network",
                            Link = "_admin/configuration/routing"
                        });
                    }

                    graph.AddEdge(zoneKey, routerKey, ZoneMapEdgeKind.Router, "all Service calls");
                }
            }

            // Calls that were denied (or would be)
            foreach (var item in activity ?? Enumerable.Empty<TrakHoundZoneActivity>())
            {
                if (item == null || string.IsNullOrEmpty(item.ZoneId) || !graph._nodes.ContainsKey(GetZoneKey(item.ZoneId))) continue;

                var kind = item.Enforced ? ZoneMapEdgeKind.Denied : ZoneMapEdgeKind.WouldDeny;
                var zoneKey = GetZoneKey(item.ZoneId);

                if (item.Direction == TrakHoundZoneDirection.Outbound)
                {
                    var target = graph.AddTarget(item.ResourceId, modules);
                    graph.AddEdge(zoneKey, target.Key, kind, $"{JoinDetail(GetDetail(item.ResourceId), item.Permission)} ×{item.Count}", item.Count, item);
                }
                else
                {
                    var callerKey = graph.GetCallerKey(item.CallerId, modules);
                    graph.AddEdge(callerKey, zoneKey, kind, $"{item.Permission} ×{item.Count}", item.Count, item);
                }
            }

            return graph;
        }


        private void AddZone(TrakHoundZoneConfiguration zone, ZoneModules modules, Func<ZoneModule, IEnumerable<string>> getCapabilities)
        {
            var members = new List<ZoneMapMember>();
            foreach (var pattern in zone.Members ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrEmpty(pattern)) continue;

                var matched = modules?.Match(pattern).ToList() ?? new List<ZoneModule>();
                var isPattern = pattern.EndsWith(TrakHoundZoneResources.Wildcard);
                var isGroup = !isPattern && IsGroupReference(pattern);

                members.Add(new ZoneMapMember
                {
                    Pattern = pattern,
                    Title = isPattern ? pattern : isGroup ? modules?.Describe(pattern) ?? pattern : matched.FirstOrDefault()?.DisplayName ?? pattern,
                    Icon = ZoneModules.GetTypeIcon(pattern.Split(':')[0]),
                    Matches = (isPattern || isGroup) && matched.Count > 0 ? string.Join(", ", matched.Select(o => o.DisplayName)) : null,
                    IsMissing = matched.Count == 0,
                    Capabilities = getCapabilities != null ? matched.SelectMany(getCapabilities).Distinct().ToList() : Array.Empty<string>()
                });
            }

            var key = GetZoneKey(zone.Id);
            _nodes[key] = new ZoneMapNode
            {
                Key = key,
                Kind = ZoneMapNodeKind.Zone,
                Title = !string.IsNullOrEmpty(zone.Name) ? zone.Name : zone.Id,
                Subtitle = zone.Id,
                Icon = "fa-solid fa-sharp fa-object-group",
                Link = $"_admin/configuration/zones/{Uri.EscapeDataString(zone.Id)}",
                Zone = zone,
                Members = members
            };
        }

        /// <summary>
        /// Adds the Target that a Resource (or pattern) belongs to. Resources of the same Module share a Target (ex. every route of an Api)
        /// </summary>
        private ZoneMapNode AddTarget(string resource, ZoneModules modules)
        {
            var key = GetTargetKey(resource);
            if (_nodes.TryGetValue(key, out var existing)) return existing;

            var parts = key.Split(':', 2);
            var module = parts.Length > 1 ? modules?.Get(key) : null;

            string title;
            string subtitle;
            if (module != null)
            {
                title = module.DisplayName;
                subtitle = module.TypeTitle;
            }
            else if (parts[0] == TrakHoundZoneResources.VolumePrefix && parts.Length > 1)
            {
                title = parts[1];
                subtitle = "Volume";
            }
            else if (parts[0] == TrakHoundZoneResources.ManagePrefix)
            {
                title = "Management";
                subtitle = "Reload, Start / Stop, Service Configurations";
            }
            else
            {
                title = modules?.DescribeResource(key) ?? key;
                subtitle = parts.Length > 1 && parts[1] != TrakHoundZoneResources.Wildcard ? $"{ZoneModules.GetTypeTitle(parts[0])} (not found)" : "Pattern";
            }

            var node = new ZoneMapNode
            {
                Key = key,
                Kind = ZoneMapNodeKind.Target,
                Title = title,
                Subtitle = subtitle,
                Icon = ZoneModules.GetTypeIcon(parts[0])
            };

            _nodes.Add(key, node);
            return node;
        }

        /// <summary>
        /// Gets the node for a Caller. Another Zone ("zone:{id}") and Members of a Zone are linked from the Zone itself
        /// </summary>
        private string GetCallerKey(string caller, ZoneModules modules)
        {
            var parts = caller.Split(':', 2);
            if (parts[0] == TrakHoundZoneResources.ZonePrefix && parts.Length > 1 && _nodes.ContainsKey(GetZoneKey(parts[1])))
            {
                return GetZoneKey(parts[1]);
            }

            if (!caller.EndsWith(TrakHoundZoneResources.Wildcard))
            {
                var memberZone = _nodes.Values.FirstOrDefault(o => o.Kind == ZoneMapNodeKind.Zone && o.Members.Any(m => TrakHoundZoneResources.IsPatternMatch(m.Pattern, caller)));
                if (memberZone != null) return memberZone.Key;
            }

            var key = $"caller:{caller}";
            if (!_nodes.ContainsKey(key))
            {
                var module = modules?.Get(caller);
                _nodes.Add(key, new ZoneMapNode
                {
                    Key = key,
                    Kind = ZoneMapNodeKind.Caller,
                    Title = module?.DisplayName ?? (caller == TrakHoundZoneResources.Wildcard ? "Every Module" : IsGroupReference(caller) ? modules?.Describe(caller) ?? caller : caller),
                    Subtitle = module?.TypeTitle ?? (caller.EndsWith(TrakHoundZoneResources.Wildcard) ? "Pattern" : IsGroupReference(caller) ? "Group" : "Module"),
                    Icon = ZoneModules.GetTypeIcon(parts[0])
                });
            }

            return key;
        }

        private static bool IsGroupReference(string value) => value.StartsWith($"{TrakHoundZoneResources.GroupPrefix}:", StringComparison.OrdinalIgnoreCase);

        private void AddEdge(string from, string to, ZoneMapEdgeKind kind, string detail, long count = 0, TrakHoundZoneActivity activity = null)
        {
            var key = $"{from}|{to}|{kind}";
            if (!_edges.TryGetValue(key, out var edge))
            {
                edge = new ZoneMapEdge { From = from, To = to, Kind = kind };
                _edges.Add(key, edge);
            }

            if (!string.IsNullOrEmpty(detail) && !edge.Details.Contains(detail)) edge.Details.Add(detail);
            edge.Count += count;
            if (activity != null) edge.Activity.Add(activity);
        }


        /// <summary>
        /// The Target a Resource belongs to: the Module (or Volume) it is in, or the pattern itself (ex. "service:*")
        /// </summary>
        public static string GetTargetKey(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return TrakHoundZoneResources.Wildcard;

            var parts = resource.Split(':', 3);
            switch (parts[0])
            {
                case TrakHoundZoneResources.ApiPrefix:
                case TrakHoundZoneResources.AppPrefix:
                case TrakHoundZoneResources.FunctionPrefix:
                case TrakHoundZoneResources.WorkerPrefix:
                case TrakHoundZoneResources.ServicePrefix:
                case TrakHoundZoneResources.VolumePrefix:
                    if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]) && !parts[1].Contains(TrakHoundZoneResources.Wildcard)) return $"{parts[0]}:{parts[1]}";
                    return resource;

                case TrakHoundZoneResources.ManagePrefix:
                    return TrakHoundZoneResources.ManagePrefix;
            }

            return resource;
        }

        /// <summary>
        /// The part of a Resource below its Target (ex. "/status" for "api:my-api:/status"), "all" when it covers all of it,
        /// or null when the Target has no parts (a Function or Worker)
        /// </summary>
        public static string GetDetail(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return "all";

            var parts = resource.Split(':', 3);
            if (parts[0] == TrakHoundZoneResources.ManagePrefix) return parts.Length > 1 ? resource.Substring(TrakHoundZoneResources.ManagePrefix.Length + 1) : "all";
            if (parts.Length > 2 && parts[2] != TrakHoundZoneResources.Wildcard) return parts[2].Replace(":", " · ");

            var isWhole = parts[0] == TrakHoundZoneResources.FunctionPrefix || parts[0] == TrakHoundZoneResources.WorkerPrefix || parts[0] == TrakHoundZoneResources.AppPrefix;
            if (isWhole && parts.Length == 2 && !parts[1].Contains(TrakHoundZoneResources.Wildcard)) return null;

            return "all";
        }

        private static string JoinDetail(string detail, string permission)
        {
            return string.Join(" · ", new[] { detail, permission }.Where(o => !string.IsNullOrEmpty(o)));
        }
    }
}
