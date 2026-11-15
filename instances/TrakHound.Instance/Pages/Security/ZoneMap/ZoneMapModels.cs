// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Blazor.Diagrams.Core.Geometry;
using TrakHound.Blazor.Diagrams.Core.Models;
using TrakHound.Blazor.Diagrams.Core.Models.Base;

namespace TrakHound.Instance.Pages.Security.ZoneMap
{
    /// <summary>
    /// A node on the Zone Map (a Zone, Target, Router or Caller)
    /// </summary>
    public class ZoneMapNodeModel : NodeModel
    {
        public ZoneMapNode Data { get; }

        public ZoneMapNodeModel(ZoneMapNode data, Point position) : base(data.Key, position)
        {
            Data = data;
        }
    }

    /// <summary>
    /// A line on the Zone Map (a Rule, Allowed Caller, Router, or calls that were denied)
    /// </summary>
    public class ZoneMapLinkModel : LinkModel
    {
        public ZoneMapEdge Edge { get; }

        public ZoneMapLinkModel(ZoneMapEdge edge, NodeModel source, NodeModel target) : base($"{edge.From}|{edge.To}|{edge.Kind}", source, target)
        {
            Edge = edge;
            TargetMarker = LinkMarker.Arrow;
            Width = edge.Kind == ZoneMapEdgeKind.Denied || edge.Kind == ZoneMapEdgeKind.WouldDeny ? 2.5 : 2;
            Color = GetColor(edge.Kind);
            SelectedColor = Color;

            // Lines out to Targets start together at the Zone, so their labels are placed near the Target (where the lines are apart)
            var towardZone = target is ZoneMapNodeModel targetNode && targetNode.Data.Kind == ZoneMapNodeKind.Zone;
            Labels.Add(new ZoneMapLinkLabelModel(this, edge, towardZone ? 0.5 : 0.72));
        }

        public static string GetColor(ZoneMapEdgeKind kind)
        {
            switch (kind)
            {
                case ZoneMapEdgeKind.Denied: return "var(--bs-danger, #dc3545)";
                case ZoneMapEdgeKind.WouldDeny: return "var(--bs-warning, #ffc107)";
                case ZoneMapEdgeKind.AllowedCaller: return "var(--bs-success, #198754)";
                case ZoneMapEdgeKind.Router: return "var(--textMild)";
                default: return "var(--accentLight)";
            }
        }
    }

    /// <summary>
    /// The label of a line on the Zone Map (what the Rule allows, or which calls were denied)
    /// </summary>
    public class ZoneMapLinkLabelModel : LinkLabelModel
    {
        public ZoneMapEdge Edge { get; }

        public ZoneMapLinkLabelModel(BaseLinkModel parent, ZoneMapEdge edge, double distance) : base(parent, $"{parent.Id}|label", string.Empty, distance)
        {
            Edge = edge;
        }
    }
}
