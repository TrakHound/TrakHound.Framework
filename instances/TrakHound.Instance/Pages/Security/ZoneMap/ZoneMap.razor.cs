// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using TrakHound.Blazor.Diagrams;
using TrakHound.Blazor.Diagrams.Core.Geometry;
using TrakHound.Blazor.Diagrams.Core.Models;
using TrakHound.Blazor.Diagrams.Core.Models.Base;
using TrakHound.Blazor.Diagrams.Core.PathGenerators;
using TrakHound.Blazor.Diagrams.Core.Routers;
using TrakHound.Blazor.Diagrams.Options;
using TrakHound.Configurations;
using TrakHound.Zones;

namespace TrakHound.Instance.Pages.Security.ZoneMap
{
    /// <summary>
    /// Shows the Zones as a diagram: Callers on the left, Zones (with their Members) in the middle, and what their Rules allow on the right.
    /// The calls that were denied are drawn as they happen
    /// </summary>
    public partial class ZoneMap
    {
        // Set by the Workspace route ("_admin/workspaces/{WorkspaceId}/..."). ConfigurationsService reads the Workspace from the URL
        [Parameter]
        public string WorkspaceId { get; set; }

        /// <summary>
        /// The ID of the saved node positions (in the "diagram" Configuration Category)
        /// </summary>
        public const string DiagramId = "zone-map";

        private const double GridSize = 15;
        private const double ColumnCallers = 0;
        private const double ColumnZones = 420;
        private const double ColumnTargets = 1000;
        private const double RowGap = 50;

        // Refresh the map at most this often while calls are being denied
        private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(1);

        private readonly object _refreshLock = new object();
        private BlazorDiagram Diagram { get; set; }
        private bool showActivity = true;
        private bool isEmpty;
        private bool layoutSaved;
        private bool fitOnRender = true;
        private int fitAttempts;
        private const int MaxFitAttempts = 30;
        private bool refreshPending;

        private readonly ZoneMapContext context = new ZoneMapContext();
        private ZoneModules modules;
        private List<ZoneModule> unzoned = new List<ZoneModule>();
        private ZoneMapEdge selectedEdge;


        public ZoneMap()
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

            // The map is read only (Zones are edited on their pages)
            options.Constraints.ShouldDeleteNode = _ => ValueTask.FromResult(false);
            options.Constraints.ShouldDeleteLink = _ => ValueTask.FromResult(false);

            Diagram = new BlazorDiagram(options);
            Diagram.RegisterComponent<ZoneMapNodeModel, ZoneMapNodeWidget>();
            Diagram.RegisterComponent<ZoneMapLinkModel, ZoneMapLinkWidget>();
            Diagram.RegisterComponent<ZoneMapLinkLabelModel, ZoneMapLinkLabelWidget>();
        }


        protected override void OnInitialized()
        {
            context.MoveMember = MoveMember;
            context.Changed += ContextChanged;
            Diagram.PointerClick += DiagramPointerClick;

            Build(GetSavedPositions());

            if (zoneManager != null)
            {
                zoneManager.ZonesUpdated += ZonesChanged;
                zoneManager.ActivityRecorded += ActivityRecorded;
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!fitOnRender) return;

            // The canvas is measured after it is first drawn, so zoom to the nodes once it has been
            if (!isEmpty && Diagram.Container != null && Diagram.Nodes.Count > 0)
            {
                fitOnRender = false;
                fitAttempts = 0;
                FitToNodes();
                await InvokeAsync(StateHasChanged);
            }
            else if (isEmpty || ++fitAttempts > MaxFitAttempts)
            {
                fitOnRender = false;
                fitAttempts = 0;
            }
            else
            {
                await Task.Delay(100);
                await InvokeAsync(StateHasChanged);
            }
        }

        public void Dispose()
        {
            context.Changed -= ContextChanged;
            Diagram.PointerClick -= DiagramPointerClick;

            if (zoneManager != null)
            {
                zoneManager.ZonesUpdated -= ZonesChanged;
                zoneManager.ActivityRecorded -= ActivityRecorded;
            }
        }


        #region "Build"

        private void Build(IReadOnlyDictionary<string, Point> positions)
        {
            var zones = zoneManager?.GetZones()?.ToList() ?? new List<TrakHoundZoneConfiguration>();
            isEmpty = zones.Count == 0;

            modules = new ZoneModules(configurationsService.ConfigurationProfile);
            var scanner = zoneManager?.Capabilities;
            var activity = showActivity ? zoneManager?.GetActivity() : null;

            // Modules that can be dragged onto a Zone
            unzoned = modules.All.Where(o => zoneManager?.GetMemberZone(o.MemberId) == null).ToList();

            var graph = ZoneMapGraph.Build(zones, modules, activity, module => scanner?.Scan(module.PackageId, module.PackageVersion)?.Select(o => o.Title) ?? Enumerable.Empty<string>());
            var layout = GetLayout(graph);

            // Keep the open Activity panel up to date (it is closed if its line is gone)
            if (selectedEdge != null)
            {
                selectedEdge = graph.Edges.FirstOrDefault(o => o.From == selectedEdge.From && o.To == selectedEdge.To && o.Kind == selectedEdge.Kind);
            }

            Diagram.Batch(() =>
            {
                Diagram.Links.Clear();
                Diagram.Nodes.Clear();

                var nodes = new Dictionary<string, ZoneMapNodeModel>(StringComparer.OrdinalIgnoreCase);
                foreach (var node in graph.Nodes)
                {
                    var position = positions?.GetValueOrDefault(node.Key) ?? layout.GetValueOrDefault(node.Key) ?? new Point(0, 0);
                    var model = new ZoneMapNodeModel(node, position);
                    nodes[node.Key] = model;
                    Diagram.Nodes.Add(model);
                }

                foreach (var edge in graph.Edges)
                {
                    var source = nodes.GetValueOrDefault(edge.From);
                    var target = nodes.GetValueOrDefault(edge.To);
                    if (source != null && target != null && source != target)
                    {
                        Diagram.Links.Add(new ZoneMapLinkModel(edge, source, target));
                    }
                }
            });
        }

        /// <summary>
        /// Places the nodes in columns: Callers, then Zones, then Targets and Routers. Each column is centered on the tallest one
        /// </summary>
        private static Dictionary<string, Point> GetLayout(ZoneMapGraph graph)
        {
            var columns = new[]
            {
                (X: ColumnCallers, Nodes: graph.Nodes.Where(o => o.Kind == ZoneMapNodeKind.Caller).OrderBy(o => o.Title, StringComparer.OrdinalIgnoreCase).ToList()),
                (X: ColumnZones, Nodes: graph.Nodes.Where(o => o.Kind == ZoneMapNodeKind.Zone).OrderBy(o => o.Zone?.Id, StringComparer.OrdinalIgnoreCase).ToList()),
                (X: ColumnTargets, Nodes: graph.Nodes.Where(o => o.Kind == ZoneMapNodeKind.Target || o.Kind == ZoneMapNodeKind.Router).OrderBy(o => o.Kind).ThenBy(o => o.Subtitle).ThenBy(o => o.Title, StringComparer.OrdinalIgnoreCase).ToList())
            };

            var heights = columns.Select(c => c.Nodes.Sum(EstimateHeight) + Math.Max(0, c.Nodes.Count - 1) * RowGap).ToList();
            var tallest = heights.Count > 0 ? heights.Max() : 0;

            var positions = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Length; i++)
            {
                var y = Snap((tallest - heights[i]) / 2);
                foreach (var node in columns[i].Nodes)
                {
                    positions[node.Key] = new Point(columns[i].X, y);
                    y = Snap(y + EstimateHeight(node) + RowGap);
                }
            }

            return positions;
        }

        // The height of a node before it is drawn (Zones grow with their Members)
        private static double EstimateHeight(ZoneMapNode node)
        {
            if (node.Kind == ZoneMapNodeKind.Zone)
            {
                var footer = node.Zone != null && !node.Zone.AllowSameZone ? 28 : 0;
                return 62 + Math.Max(1, node.Members.Count) * 24 + 12 + footer;
            }

            return 60;
        }

        private static double Snap(double value) => Math.Round(value / GridSize) * GridSize;

        private Dictionary<string, Point> GetCurrentPositions()
        {
            return Diagram.Nodes.ToDictionary(o => o.Id, o => o.Position, StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region "Saved Layout"

        private ITrakHoundConfigurationProfile Profile => configurationsService.ConfigurationProfile;

        // The Zone Manager subscribed to (kept, since the URL has already changed when the page is disposed)
        private TrakHoundZoneManager _zoneManager;
        private TrakHoundZoneManager zoneManager => _zoneManager ??= configurationsService.Target.ZoneManager;

        private TrakHoundDiagramConfiguration GetSavedLayout()
        {
            // The Diagram Category is only loaded by the pages that use it
            var layout = Profile?.Get<TrakHoundDiagramConfiguration>(TrakHoundDiagramConfiguration.ConfigurationCategory, DiagramId);
            if (layout == null && Profile != null)
            {
                layout = Profile.Read<TrakHoundDiagramConfiguration>(TrakHoundDiagramConfiguration.ConfigurationCategory, DiagramId);
            }

            return layout;
        }

        private Dictionary<string, Point> GetSavedPositions()
        {
            var nodes = GetSavedLayout()?.Nodes;
            if (nodes.IsNullOrEmpty()) return null;

            return nodes.Where(o => !string.IsNullOrEmpty(o.Id)).GroupBy(o => o.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(o => o.Key, o => new Point(o.First().X, o.First().Y), StringComparer.OrdinalIgnoreCase);
        }

        private async Task SaveLayout()
        {
            var layout = new TrakHoundDiagramConfiguration();
            layout.Id = DiagramId;
            layout.Nodes = Diagram.Nodes.Select(o => new TrakHoundDiagramNodeConfiguration { Id = o.Id, X = o.Position.X, Y = o.Position.Y }).ToList();

            if (Profile != null) configurationsService.Save(layout, TrakHound.Instance.Security.TrakHoundAdminSecurity.Zones);

            layoutSaved = true;
            await Task.Delay(2000);
            layoutSaved = false;
            await InvokeAsync(StateHasChanged);
        }

        #endregion

        #region "Members"

        private void ContextChanged() => _ = InvokeAsync(StateHasChanged);

        private async Task MoveMember(string member, string fromZoneId, string toZoneId)
        {
            // The Zone Manager raises ZonesUpdated, which rebuilds the map
            zoneManager?.MoveMember(member, fromZoneId, toZoneId);
            await InvokeAsync(StateHasChanged);
        }

        private async Task DropOutOfZones()
        {
            if (context.Drag?.FromZoneId != null) await context.Drop(null);
        }

        // Releasing anywhere other than a Zone (or Not in a Zone) cancels the drag
        private void EndDrag()
        {
            if (context.Drag != null) context.EndDrag();
        }

        #endregion

        #region "Activity"

        private void DiagramPointerClick(Model model, TrakHound.Blazor.Diagrams.Core.Events.PointerEventArgs args)
        {
            var edge = (model as ZoneMapLinkModel)?.Edge;
            var isActivity = edge != null && (edge.Kind == ZoneMapEdgeKind.Denied || edge.Kind == ZoneMapEdgeKind.WouldDeny);

            // Clicking a red or amber line opens its calls. Clicking anywhere else closes them
            var next = isActivity ? edge : (model == null ? null : selectedEdge);
            if (next != selectedEdge)
            {
                selectedEdge = next;
                _ = InvokeAsync(StateHasChanged);
            }
        }

        private void Grant(TrakHoundZoneActivity activity, string pattern)
        {
            zoneManager?.Grant(activity, pattern);
            Build(GetCurrentPositions());
        }

        private void Dismiss(TrakHoundZoneActivity activity)
        {
            zoneManager?.ClearActivity(activity.Key);
            Build(GetCurrentPositions());
        }

        #endregion

        #region "Toolbar"

        private void Reload()
        {
            Build(GetCurrentPositions());
        }

        private void AutoArrange()
        {
            Build(null);
            fitOnRender = true;
        }

        private void ZoomToFit() => FitToNodes();

        /// <summary>
        /// Zooms and pans so every node is shown (never zooming in past 100%). Nodes that haven't been measured yet use their estimated size
        /// </summary>
        private void FitToNodes()
        {
            var container = Diagram.Container;
            if (container == null || container.Width <= 0 || container.Height <= 0 || Diagram.Nodes.Count == 0) return;

            const double margin = 40;
            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            foreach (var node in Diagram.Nodes.OfType<ZoneMapNodeModel>())
            {
                var width = node.Size?.Width ?? (node.Data.Kind == ZoneMapNodeKind.Zone ? 290 : 240);
                var height = node.Size?.Height ?? EstimateHeight(node.Data);

                minX = Math.Min(minX, node.Position.X);
                minY = Math.Min(minY, node.Position.Y);
                maxX = Math.Max(maxX, node.Position.X + width);
                maxY = Math.Max(maxY, node.Position.Y + height);
            }

            if (minX == double.MaxValue) return;

            var boundsWidth = maxX - minX + 2 * margin;
            var boundsHeight = maxY - minY + 2 * margin;
            var zoom = Math.Min(1, Math.Min(container.Width / boundsWidth, container.Height / boundsHeight));

            Diagram.SetZoom(zoom);

            // Center the nodes in the canvas
            var offsetX = (container.Width - (maxX - minX) * zoom) / 2;
            var offsetY = (container.Height - (maxY - minY) * zoom) / 2;
            Diagram.SetPan(offsetX - minX * zoom, offsetY - minY * zoom);
        }

        private void ShowActivityChanged(Microsoft.AspNetCore.Components.ChangeEventArgs args)
        {
            showActivity = args.Value is bool value && value;
            Build(GetCurrentPositions());
        }

        #endregion

        #region "Live Updates"

        private void ZonesChanged(object sender, EventArgs args)
        {
            _ = InvokeAsync(() =>
            {
                var wasEmpty = isEmpty;
                Build(GetCurrentPositions());
                if (wasEmpty) fitOnRender = true;
                StateHasChanged();
            });
        }

        private void ActivityRecorded(object sender, TrakHoundZoneActivity activity)
        {
            if (!showActivity) return;

            // Refresh once for a burst of calls
            lock (_refreshLock)
            {
                if (refreshPending) return;
                refreshPending = true;
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(_refreshInterval);
                lock (_refreshLock) refreshPending = false;

                await InvokeAsync(() =>
                {
                    Build(GetCurrentPositions());
                    StateHasChanged();
                });
            });
        }

        #endregion
    }
}
