// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using TrakHound.Blazor.Diagrams;
using TrakHound.Blazor.Diagrams.Core.Geometry;
using TrakHound.Blazor.Diagrams.Core.Models.Base;
using TrakHound.Blazor.Diagrams.Core.PathGenerators;
using TrakHound.Blazor.Diagrams.Core.Routers;
using TrakHound.Blazor.Diagrams.Options;
using TrakHound.Services;

namespace TrakHound.Instance.Pages.Configuration.Routing
{
    /// <summary>
    /// Shows the Routers as a read only map, left to right: Modules, Routers, Routes, Targets and Services.
    /// Choosing a kind of Request fades what it can't reach, and selecting a node fades what isn't connected to it
    /// </summary>
    public partial class RoutingMap : IDisposable
    {
        private const double GridSize = 15;
        private const double ColumnWidth = 320;
        private const double RowGap = 25;
        private const double NodeWidth = 240;
        private const int MaxFitAttempts = 30;

        private readonly RoutingMapContext context = new RoutingMapContext();
        private RoutingMapGraph graph = new RoutingMapGraph();
        private List<RoutingModule> modules = new List<RoutingModule>();
        private string kind;
        private string selectedKey;
        private bool fitOnRender = true;
        private bool measured;
        private int fitAttempts;


        private BlazorDiagram Diagram { get; }

        /// <summary>
        /// The Routers being edited
        /// </summary>
        [Parameter]
        public RoutingModel Model { get; set; }

        [Parameter]
        public IEnumerable<RoutingModule> Modules { get; set; }

        [Parameter]
        public IEnumerable<TrakHoundServiceRouteService> Services { get; set; }

        /// <summary>
        /// Called to edit a Router, Route or Target (the Map is closed and the item is selected in the editor)
        /// </summary>
        [Parameter]
        public EventCallback<RoutingItem> EditItem { get; set; }


        public RoutingMap()
        {
            var options = new BlazorDiagramOptions
            {
                GridSize = (int)GridSize,
                Zoom =
                {
                    ScaleFactor = 1.25,
                    Inverse = true,
                    Enabled = true,
                },
                Links =
                {
                    DefaultRouter = new NormalRouter(),
                    DefaultPathGenerator = new SmoothPathGenerator(75),
                    DefaultColor = "var(--accentLight)",
                    DefaultSelectedColor = "var(--accentLight)"
                }
            };

            // The Map is read only (the Routers are edited on the canvas behind it)
            options.Constraints.ShouldDeleteNode = _ => ValueTask.FromResult(false);
            options.Constraints.ShouldDeleteLink = _ => ValueTask.FromResult(false);

            Diagram = new BlazorDiagram(options);
            Diagram.RegisterComponent<RoutingMapNodeModel, RoutingMapNodeWidget>();
            Diagram.RegisterComponent<RoutingMapLinkModel, RoutingMapLinkWidget>();
        }


        protected override void OnInitialized()
        {
            modules = Modules?.ToList() ?? new List<RoutingModule>();
            graph = RoutingMapGraph.Build(Model, modules, Services);
            Build();

            Diagram.SelectionChanged += SelectionChanged;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!fitOnRender) return;

            // The canvas and nodes are measured after they are first drawn: lay the nodes out again with their sizes, then zoom to them
            if (Diagram.Container != null && Diagram.Nodes.Count > 0 && Diagram.Nodes.All(o => o.Size != null))
            {
                fitOnRender = false;
                measured = true;
                foreach (var position in GetLayout())
                {
                    Diagram.Nodes.FirstOrDefault(o => o.Id == position.Key)?.SetPosition(position.Value.X, position.Value.Y);
                }
                FitToNodes();
                await InvokeAsync(StateHasChanged);
            }
            else if (Diagram.Nodes.Count == 0 || ++fitAttempts > MaxFitAttempts)
            {
                fitOnRender = false;
                FitToNodes();
            }
            else
            {
                await Task.Delay(100);
                await InvokeAsync(StateHasChanged);
            }
        }

        public void Dispose()
        {
            Diagram.SelectionChanged -= SelectionChanged;
        }


        #region "Build"

        private void Build()
        {
            var positions = GetLayout();

            Diagram.Batch(() =>
            {
                Diagram.Links.Clear();
                Diagram.Nodes.Clear();

                var nodes = new Dictionary<string, RoutingMapNodeModel>(StringComparer.Ordinal);
                foreach (var node in graph.Nodes)
                {
                    var model = new RoutingMapNodeModel(node, positions.GetValueOrDefault(node.Key) ?? new Point(node.Column * ColumnWidth, 0));
                    nodes[node.Key] = model;
                    Diagram.Nodes.Add(model);
                }

                foreach (var edge in graph.Edges)
                {
                    var source = nodes.GetValueOrDefault(edge.From);
                    var target = nodes.GetValueOrDefault(edge.To);
                    if (source != null && target != null && source != target)
                    {
                        Diagram.Links.Add(new RoutingMapLinkModel(edge, source, target));
                    }
                }
            });
        }

        /// <summary>
        /// Routers, Routes and Lanes are placed as a tree (each item centered on its children). Targets are placed next to their Lanes, Modules next to
        /// the Router they use, and Services next to the Targets that send to them, moved down where they would overlap
        /// </summary>
        private Dictionary<string, Point> GetLayout()
        {
            var y = new Dictionary<string, double>(StringComparer.Ordinal);
            var row = 0.0;

            // Routers, their Routes and the Routes' Lanes (a tree)
            double Place(RoutingMapNode node)
            {
                double nodeY;
                var children = graph.GetOutgoing(node.Key)
                    .Where(o => o.Kind == RoutingMapEdgeKind.Connection)
                    .Select(o => graph.GetNode(o.To))
                    .Where(o => o != null && (o.Kind == RoutingMapNodeKind.Route || o.Kind == RoutingMapNodeKind.Lane))
                    .ToList();

                if (children.Count == 0)
                {
                    nodeY = row;
                    row += GetHeight(node) + RowGap;
                }
                else
                {
                    var childYs = children.Select(Place).ToList();
                    nodeY = (childYs.First() + childYs.Last()) / 2;
                }

                y[node.Key] = nodeY;
                return nodeY;
            }

            foreach (var router in graph.Nodes.Where(o => o.Kind == RoutingMapNodeKind.Router).OrderBy(o => o.Title, StringComparer.OrdinalIgnoreCase))
            {
                Place(router);
            }

            foreach (var missing in graph.Nodes.Where(o => o.Kind == RoutingMapNodeKind.MissingRouter).OrderBy(o => o.Title, StringComparer.OrdinalIgnoreCase))
            {
                y[missing.Key] = row;
                row += GetHeight(missing) + RowGap;
            }

            // Targets : next to the Lanes that use them (or their Route, if none do)
            Stack(graph.Nodes.Where(o => o.Kind == RoutingMapNodeKind.Target)
                .Select(o =>
                {
                    var from = graph.GetIncoming(o.Key).Select(e => y.TryGetValue(e.From, out var fromY) ? fromY : double.NaN).Where(v => !double.IsNaN(v)).ToList();
                    var desired = from.Count > 0 ? from.Average() : y.GetValueOrDefault(o.Item.Parent?.Id, double.MaxValue);

                    // Targets next to the same Lanes are kept in the order they are tried
                    var order = o.Item.Parent?.Children.ToList().IndexOf(o.Item) ?? 0;
                    return (Node: o, Y: desired == double.MaxValue ? desired : desired + order * 0.001);
                }), y);

            // Modules : next to the Router (or Service) they use
            Stack(graph.Nodes.Where(o => o.Kind == RoutingMapNodeKind.Modules)
                .Select(o => (Node: o, Y: y.GetValueOrDefault(graph.GetOutgoing(o.Key).First().To, double.MaxValue))), y);

            // Services : next to the Targets (or Modules) that send to them (the ones nothing sends to go last)
            Stack(graph.Nodes.Where(o => o.Kind == RoutingMapNodeKind.Service)
                .Select(o =>
                {
                    var from = graph.GetIncoming(o.Key).Select(e => y.TryGetValue(e.From, out var fromY) ? fromY : double.NaN).Where(v => !double.IsNaN(v)).ToList();
                    return (Node: o, Y: from.Count > 0 ? from.Average() : double.MaxValue);
                }), y);

            return graph.Nodes.Where(o => y.ContainsKey(o.Key)).ToDictionary(o => o.Key, o => new Point(o.Column * ColumnWidth, Snap(y[o.Key])), StringComparer.Ordinal);
        }

        // Places the nodes in order of their Y, at their Y or just below the previous node (nodes without a Y go last)
        private void Stack(IEnumerable<(RoutingMapNode Node, double Y)> nodes, Dictionary<string, double> y)
        {
            var next = 0.0;
            foreach (var (node, desired) in nodes.OrderBy(o => o.Y).ThenBy(o => o.Node.Title, StringComparer.OrdinalIgnoreCase).ToList())
            {
                var value = desired == double.MaxValue ? next : Math.Max(desired, next);
                y[node.Key] = value;
                next = value + GetHeight(node) + RowGap;
            }
        }

        // The height of a node : measured once it has been drawn
        private double GetHeight(RoutingMapNode node)
        {
            var size = measured && node != null ? Diagram.Nodes.FirstOrDefault(o => o.Id == node.Key)?.Size : null;
            return size?.Height ?? EstimateHeight(node);
        }

        // The height of a node before it is drawn (a Modules node grows with its Modules)
        private static double EstimateHeight(RoutingMapNode node)
        {
            if (node?.Kind == RoutingMapNodeKind.Modules) return 46 + Math.Min(node.Modules.Count, 6) * 18;
            if (node?.Kind == RoutingMapNodeKind.Lane) return 50;
            return node?.Item is TargetItem target && target.FallbackOn != null ? 86 : 68;
        }

        private static double Snap(double value) => Math.Round(value / GridSize) * GridSize;

        #endregion

        #region "Selection"

        private void SelectionChanged(SelectableModel model)
        {
            var key = Diagram.GetSelectedModels().OfType<RoutingMapNodeModel>().Select(o => o.Data.Key).FirstOrDefault();
            if (key != selectedKey)
            {
                selectedKey = key;
                Refresh();
            }
        }

        private void Select(string key)
        {
            var node = Diagram.Nodes.OfType<RoutingMapNodeModel>().FirstOrDefault(o => o.Data.Key == key);
            if (node == null) return;

            // Raises SelectionChanged
            Diagram.SelectModel(node, true);
        }

        private void SetKind(string value)
        {
            kind = value;
            Refresh();
        }

        private void Refresh()
        {
            context.Set(kind != null ? graph.GetActive(kind) : null, selectedKey, selectedKey != null ? graph.GetConnected(selectedKey) : null);
            _ = InvokeAsync(StateHasChanged);
        }

        /// <summary>
        /// What needs looking at: Routers that don't exist, and Routers and Services nothing uses
        /// </summary>
        private IEnumerable<(string Title, List<RoutingMapNode> Nodes)> GetAttention()
        {
            var missing = graph.Nodes.Where(o => o.Kind == RoutingMapNodeKind.MissingRouter).ToList();
            if (missing.Count > 0) yield return ("Missing Routers", missing);

            var routers = graph.Nodes.Where(o => o.Kind == RoutingMapNodeKind.Router && o.IsUnused).ToList();
            if (routers.Count > 0) yield return ("Routers nothing uses", routers);

            var services = graph.Nodes.Where(o => o.Kind == RoutingMapNodeKind.Service && o.IsUnused).ToList();
            if (services.Count > 0) yield return ("Services nothing sends Requests to", services);
        }

        #endregion

        #region "Toolbar"

        /// <summary>
        /// Zooms and pans so every node is shown (never zooming in past 100%). Nodes that haven't been measured yet use their estimated size
        /// </summary>
        private void FitToNodes()
        {
            var container = Diagram.Container;
            if (container == null || container.Width <= 0 || container.Height <= 0 || Diagram.Nodes.Count == 0) return;

            const double margin = 40;
            var nodes = Diagram.Nodes.OfType<RoutingMapNodeModel>().ToList();
            var minX = nodes.Min(o => o.Position.X);
            var minY = nodes.Min(o => o.Position.Y);
            var maxX = nodes.Max(o => o.Position.X + (o.Size?.Width ?? NodeWidth));
            var maxY = nodes.Max(o => o.Position.Y + (o.Size?.Height ?? EstimateHeight(o.Data)));

            var zoom = Math.Clamp(Math.Min(container.Width / (maxX - minX + 2 * margin), container.Height / (maxY - minY + 2 * margin)), 0.2, 1);
            Diagram.SetZoom(zoom);

            var offsetX = Math.Max(margin, (container.Width - (maxX - minX) * zoom) / 2);
            var offsetY = Math.Max(margin, (container.Height - (maxY - minY) * zoom) / 2);
            Diagram.SetPan(offsetX - minX * zoom, offsetY - minY * zoom);
        }

        #endregion
    }
}
