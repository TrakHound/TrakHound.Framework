// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Blazor.Diagrams.Core.Geometry;
using TrakHound.Blazor.Diagrams.Core.Models;

namespace TrakHound.Instance.Pages.Configuration.Routing
{
    /// <summary>
    /// A node on the Routing Map
    /// </summary>
    public class RoutingMapNodeModel : NodeModel
    {
        public RoutingMapNode Data { get; }

        public RoutingMapNodeModel(RoutingMapNode data, Point position) : base(data.Key, position)
        {
            Data = data;
        }
    }

    /// <summary>
    /// A line on the Routing Map
    /// </summary>
    public class RoutingMapLinkModel : LinkModel
    {
        public RoutingMapEdge Edge { get; }

        public RoutingMapLinkModel(RoutingMapEdge edge, NodeModel source, NodeModel target) : base(edge.Key, source, target)
        {
            Edge = edge;
            TargetMarker = LinkMarker.Arrow;
            Width = 2;
            Color = edge.Kind == RoutingMapEdgeKind.RouterTarget || edge.Kind == RoutingMapEdgeKind.Uses ? "var(--textMild)" : "var(--accentLight)";
            SelectedColor = Color;
        }
    }

    /// <summary>
    /// Shared by the Routing Map and its widgets: which nodes and lines are faded (by the kind of Request shown, and the selected node)
    /// </summary>
    public class RoutingMapContext
    {
        private HashSet<string> _active;
        private HashSet<string> _connected;


        public string SelectedKey { get; private set; }

        /// <summary>
        /// Raised when what is faded changes (so the widgets are drawn again)
        /// </summary>
        public event Action Changed;


        /// <summary>
        /// Sets the nodes and lines a Request can reach (null for all), and those connected to the selected node (null if nothing is selected)
        /// </summary>
        public void Set(HashSet<string> active, string selectedKey, HashSet<string> connected)
        {
            _active = active;
            SelectedKey = selectedKey;
            _connected = connected;
            Changed?.Invoke();
        }

        public bool IsFaded(string key) => key != null && ((_active != null && !_active.Contains(key)) || (_connected != null && !_connected.Contains(key)));
    }
}
