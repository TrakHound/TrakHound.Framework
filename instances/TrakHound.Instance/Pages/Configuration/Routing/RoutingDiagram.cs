// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Blazor.Diagrams.Core.Geometry;
using TrakHound.Blazor.Diagrams.Core.Models;
using TrakHound.Blazor.Diagrams.Core.Models.Base;

namespace TrakHound.Instance.Pages.Configuration.Routing
{
    /// <summary>
    /// A Router, Route or Target on the diagram
    /// </summary>
    public class RoutingNode : NodeModel
    {
        public RoutingItem Item { get; }

        public RoutingPort InputPort { get; }

        public RoutingPort OutputPort { get; }


        public RoutingNode(RoutingItem item, Point position, Func<RoutingItem, RoutingItem, bool> canConnect) : base(item.Id, position)
        {
            Item = item;

            // Routers can't be connected to anything (other Routers are sent Requests by a Router Target)
            if (item.Kind != RoutingItemKind.Router) InputPort = (RoutingPort)AddPort(new RoutingPort(this, RoutingPortDirection.Input, canConnect));
            // Targets are the last items (a Route tries its Targets in order)
            if (item.Kind != RoutingItemKind.Target) OutputPort = (RoutingPort)AddPort(new RoutingPort(this, RoutingPortDirection.Output, canConnect));
        }
    }

    public enum RoutingPortDirection
    {
        Input,
        Output
    }

    /// <summary>
    /// Connects an item (Output, on the right) to the items it sends Requests to (Input, on the left). Only connections the Routing allows can be made
    /// </summary>
    public class RoutingPort : PortModel
    {
        private readonly Func<RoutingItem, RoutingItem, bool> _canConnect;


        public RoutingPortDirection Direction { get; }

        public RoutingNode Node => (RoutingNode)Parent;


        public RoutingPort(RoutingNode parent, RoutingPortDirection direction, Func<RoutingItem, RoutingItem, bool> canConnect)
            : base($"{parent.Id}:{direction.ToString().ToLowerInvariant()}", parent, direction == RoutingPortDirection.Input ? PortAlignment.Left : PortAlignment.Right)
        {
            Direction = direction;
            _canConnect = canConnect;
        }


        public override bool CanAttachTo(ILinkable other)
        {
            if (other is not RoutingPort port || port.Parent == Parent || port.Direction == Direction) return false;

            // A link can be drawn from either end
            var (parent, child) = GetConnection(this, port);
            return _canConnect == null || _canConnect(parent.Node.Item, child.Node.Item);
        }

        /// <summary>
        /// The Output (parent) and Input (child) ports of a link between the two ports
        /// </summary>
        public static (RoutingPort Parent, RoutingPort Child) GetConnection(RoutingPort a, RoutingPort b)
        {
            return a.Direction == RoutingPortDirection.Output ? (a, b) : (b, a);
        }
    }
}
