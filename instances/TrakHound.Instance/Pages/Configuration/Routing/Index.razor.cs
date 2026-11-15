// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;
using TrakHound.Blazor.Diagrams;
using TrakHound.Blazor.Diagrams.Core.Anchors;
using TrakHound.Blazor.Diagrams.Core.Geometry;
using TrakHound.Blazor.Diagrams.Core.Models;
using TrakHound.Blazor.Diagrams.Core.Models.Base;
using TrakHound.Blazor.Diagrams.Core.PathGenerators;
using TrakHound.Blazor.Diagrams.Core.Routers;
using TrakHound.Blazor.Diagrams.Options;
using TrakHound.Configurations;
using TrakHound.Services;

namespace TrakHound.Instance.Pages.Configuration.Routing
{
    /// <summary>
    /// Edits the Routers as a diagram. The Routers are read into a RoutingModel, which is what is edited and saved (the diagram only shows it)
    /// </summary>
    public partial class Index : IDisposable
    {
        // Set by the Workspace route ("_admin/workspaces/{WorkspaceId}/..."). ConfigurationsService reads the Workspace from the URL
        [Parameter]
        public string WorkspaceId { get; set; }

        /// <summary>
        /// The ID of the saved node positions (in the "diagram" Configuration Category)
        /// </summary>
        public const string RoutingDiagramId = "routing-diagram";

        private const double NodeWidth = 260;
        private const double NodeHeight = 80;
        private const int MaxFitAttempts = 30;

        // When first shown, the diagram is not zoomed out further than this (so the nodes can be read)
        private const double InitialMinZoom = 0.6;

        private readonly RoutingEditorContext context = new RoutingEditorContext();

        // The links drawn for a connection in the Model (Link => Child Item ID). Other links are being drawn by the user
        private readonly Dictionary<BaseLinkModel, string> connectionLinks = new Dictionary<BaseLinkModel, string>();

        private RoutingModel model = new RoutingModel();
        private Dictionary<string, ITrakHoundServiceConfiguration> services = new Dictionary<string, ITrakHoundServiceConfiguration>(StringComparer.Ordinal);

        // The Modules that make Service calls (and the Router each names)
        private List<RoutingModule> modules = new List<RoutingModule>();

        // The Services shown on the Map (null while it is closed)
        private List<TrakHoundServiceRouteService> mapServices;
        private bool mapLoading;

        // The Hash of each Router when it was loaded (to tell if it was changed by something else before it is saved)
        private Dictionary<string, string> loadedHashes = new Dictionary<string, string>(StringComparer.Ordinal);

        private List<RoutingIssue> issues = new List<RoutingIssue>();
        private readonly List<string> saveErrors = new List<string>();
        private RoutingItem selected;
        private string savedSnapshot;
        private string savedLayoutSnapshot;
        private bool building;
        private bool saving;
        private bool saved;
        private bool fitOnRender;
        private int fitAttempts;
        private string search;
        private PaletteEntry dragging;
        private string leaveTarget;
        private bool leaveConfirmed;
        private bool inspectorHidden;


        private BlazorDiagram Diagram { get; }

        private ITrakHoundConfigurationProfile Profile => configurationsService.ConfigurationProfile;

        private bool IsDirty => !leaveConfirmed && savedSnapshot != null && (model.GetSnapshot() != savedSnapshot || GetLayoutSnapshot() != savedLayoutSnapshot);

        private int ErrorCount => issues.Count(o => o.Severity == RoutingIssueSeverity.Error);

        private int WarningCount => issues.Count(o => o.Severity == RoutingIssueSeverity.Warning);


        public Index()
        {
            var options = new BlazorDiagramOptions
            {
                AllowMultiSelection = true,
                GridSize = (int)RoutingLayout.GridSize,
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
                    EnableSnapping = true,
                    RequireTarget = true,
                    DefaultColor = "var(--accentLight)",
                    DefaultSelectedColor = "var(--accentDark)"
                }
            };

            Diagram = new BlazorDiagram(options);
            Diagram.RegisterComponent<RoutingNode, RoutingNodeWidget>();
        }


        protected override void OnInitialized()
        {
            context.GetTargetName = GetTargetName;
            context.GetTargetService = target => target.IsService && target.Source != null ? services.GetValueOrDefault(target.Source) : null;
            context.GetModules = GetModules;

            Diagram.Links.Added += LinkAdded;
            Diagram.Links.Removed += LinkRemoved;
            Diagram.Nodes.Removed += NodeRemoved;
            Diagram.SelectionChanged += SelectionChanged;

            Load();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!fitOnRender) return;

            // The canvas is measured after it is first drawn, so zoom to the nodes once it has been
            if (Diagram.Container != null && Diagram.Nodes.Count > 0)
            {
                fitOnRender = false;
                fitAttempts = 0;
                FitToNodes(InitialMinZoom);
                await InvokeAsync(StateHasChanged);
            }
            else if (Diagram.Nodes.Count == 0 || ++fitAttempts > MaxFitAttempts)
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
            Diagram.Links.Added -= LinkAdded;
            Diagram.Links.Removed -= LinkRemoved;
            Diagram.Nodes.Removed -= NodeRemoved;
            Diagram.SelectionChanged -= SelectionChanged;
        }


        #region "Load"

        private void Load()
        {
            saveErrors.Clear();
            selected = null;
            leaveConfirmed = false;

            var routerConfigurations = Profile?.Get<TrakHoundServiceRouterConfiguration>(TrakHoundServiceRouterConfiguration.ConfigurationCategory)?.ToList() ?? new List<TrakHoundServiceRouterConfiguration>();
            loadedHashes = routerConfigurations.Where(o => !string.IsNullOrEmpty(o.Id)).GroupBy(o => o.Id).ToDictionary(o => o.Key, o => o.First().Hash, StringComparer.Ordinal);

            var serviceConfigurations = Profile?.Get<ITrakHoundServiceConfiguration>(TrakHoundServiceConfiguration.ConfigurationCategory) ?? Enumerable.Empty<ITrakHoundServiceConfiguration>();
            services = serviceConfigurations.Where(o => !string.IsNullOrEmpty(o.Id)).GroupBy(o => o.Id).ToDictionary(o => o.Key, o => o.First(), StringComparer.Ordinal);

            modules = RoutingModules.Load(Profile);

            model = RoutingModel.FromConfigurations(routerConfigurations);

            Build(GetPositions(GetSavedPositions()));
            Validate();

            savedSnapshot = model.GetSnapshot();
            savedLayoutSnapshot = GetLayoutSnapshot();
            fitOnRender = true;
        }

        private Dictionary<string, Point> GetSavedPositions()
        {
            var layout = Profile?.Get<TrakHoundDiagramConfiguration>(TrakHoundDiagramConfiguration.ConfigurationCategory, RoutingDiagramId)
                ?? Profile?.Read<TrakHoundDiagramConfiguration>(TrakHoundDiagramConfiguration.ConfigurationCategory, RoutingDiagramId);

            // Earlier versions saved the Routing Diagram with a different ID: use the Diagram that has the most of the items
            if (layout == null && Profile != null)
            {
                var ids = model.Items.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
                layout = Profile.Read<TrakHoundDiagramConfiguration>(TrakHoundDiagramConfiguration.ConfigurationCategory)?
                    .Where(o => o.Id != TrakHound.Instance.Pages.Security.ZoneMap.ZoneMap.DiagramId && o.Nodes != null)
                    .Select(o => (Layout: o, Count: o.Nodes.Count(n => n.Id != null && ids.Contains(n.Id))))
                    .Where(o => o.Count > 0)
                    .OrderByDescending(o => o.Count)
                    .Select(o => o.Layout)
                    .FirstOrDefault();
            }

            var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
            foreach (var node in layout?.Nodes ?? Enumerable.Empty<TrakHoundDiagramNodeConfiguration>())
            {
                if (!string.IsNullOrEmpty(node.Id)) positions[node.Id] = new Point(node.X, node.Y);
            }
            return positions;
        }

        /// <summary>
        /// The position of every item: where it was saved, or (for items that weren't saved on the diagram) next to the item it is connected to
        /// </summary>
        private Dictionary<string, Point> GetPositions(Dictionary<string, Point> saved)
        {
            var positions = new Dictionary<string, Point>(StringComparer.Ordinal);
            var items = GetTreeOrder().ToList();

            // Nothing was saved (or very little was): arrange everything
            if (saved == null || items.Count(o => saved.ContainsKey(o.Id)) * 2 < items.Count)
            {
                foreach (var position in RoutingLayout.Arrange(model)) positions[position.Key] = new Point(position.Value.X, position.Value.Y);
                return positions;
            }

            foreach (var item in items.Where(o => saved.ContainsKey(o.Id))) positions[item.Id] = saved[item.Id];

            var bottom = positions.Count > 0 ? positions.Values.Max(o => o.Y) : 0;
            foreach (var item in items.Where(o => !saved.ContainsKey(o.Id)))
            {
                if (item.Parent != null && positions.TryGetValue(item.Parent.Id, out var parent))
                {
                    var siblings = item.Parent.Children.Where(o => o != item && positions.ContainsKey(o.Id)).Select(o => (positions[o.Id].X, positions[o.Id].Y));
                    var position = RoutingLayout.GetChildPosition((parent.X, parent.Y), siblings);
                    positions[item.Id] = new Point(position.X, position.Y);
                }
                else
                {
                    bottom = RoutingLayout.Snap(bottom + RoutingLayout.RowHeight);
                    positions[item.Id] = new Point((int)item.Kind * RoutingLayout.ColumnWidth, bottom);
                }
            }

            return positions;
        }

        // Parents before their children
        private IEnumerable<RoutingItem> GetTreeOrder()
        {
            var roots = model.Routers.Cast<RoutingItem>().Concat(model.Unconnected);
            var queue = new Queue<RoutingItem>(roots);
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                yield return item;
                foreach (var child in item.Children) queue.Enqueue(child);
            }
        }

        #endregion

        #region "Diagram"

        /// <summary>
        /// Draws every item in the Model, and a link for each connection
        /// </summary>
        private void Build(Dictionary<string, Point> positions)
        {
            building = true;
            try
            {
                Diagram.Batch(() =>
                {
                    connectionLinks.Clear();
                    Diagram.Links.Clear();
                    Diagram.Nodes.Clear();

                    var nodes = new Dictionary<string, RoutingNode>(StringComparer.Ordinal);
                    foreach (var item in model.Items)
                    {
                        var position = positions?.GetValueOrDefault(item.Id) ?? new Point(0, 0);
                        var node = new RoutingNode(item, position, model.CanConnect);
                        nodes[item.Id] = node;
                        Diagram.Nodes.Add(node);
                    }

                    foreach (var item in model.Items.Where(o => o.Parent != null))
                    {
                        AddConnectionLink(nodes[item.Parent.Id], nodes[item.Id]);
                    }
                });
            }
            finally
            {
                building = false;
            }
        }

        private RoutingNode GetNode(RoutingItem item) => item != null ? Diagram.Nodes.OfType<RoutingNode>().FirstOrDefault(o => o.Item == item) : null;

        private void AddConnectionLink(RoutingNode parent, RoutingNode child)
        {
            var link = new LinkModel(new SinglePortAnchor(parent.OutputPort), new SinglePortAnchor(child.InputPort));
            connectionLinks[link] = child.Item.Id;
            Diagram.Links.Add(link);
        }

        private void LinkAdded(BaseLinkModel link)
        {
            // A link being drawn by the user (it is connected once it is dropped on a port)
            if (!building && !connectionLinks.ContainsKey(link))
            {
                link.TargetAttached += LinkAttached;
            }
        }

        private void LinkAttached(BaseLinkModel link)
        {
            link.TargetAttached -= LinkAttached;

            var source = link.Source?.Model as RoutingPort;
            var target = link.Target?.Model as RoutingPort;

            // The drawn link is replaced by one from the Parent's Output to the Child's Input (it can be drawn from either end)
            Diagram.Links.Remove(link);

            if (source != null && target != null && source.Direction != target.Direction)
            {
                var (parentPort, childPort) = RoutingPort.GetConnection(source, target);
                if (model.Connect(parentPort.Node.Item, childPort.Node.Item))
                {
                    AddConnectionLink(parentPort.Node, childPort.Node);
                    Changed();
                }
            }
        }

        private void LinkRemoved(BaseLinkModel link)
        {
            if (building) return;

            if (connectionLinks.Remove(link, out var childId))
            {
                model.Disconnect(model.Get(childId));
                Changed();
            }
        }

        private void NodeRemoved(NodeModel node)
        {
            if (building || node is not RoutingNode routingNode) return;

            // The node's links have already been removed (which disconnected it)
            model.Remove(routingNode.Item);
            if (selected == routingNode.Item) selected = null;
            Changed();
        }

        private void SelectionChanged(SelectableModel selectable)
        {
            var next = Diagram.GetSelectedModels().OfType<RoutingNode>().Select(o => o.Item).FirstOrDefault();
            if (next != selected)
            {
                selected = next;
                _ = InvokeAsync(StateHasChanged);
            }
        }

        /// <summary>
        /// Called after an item (or a connection) is changed: checks the items again and draws the nodes again
        /// </summary>
        private void Changed()
        {
            saved = false;
            Validate();
            context.NotifyChanged();
            _ = InvokeAsync(StateHasChanged);
        }

        private void Validate()
        {
            issues = model.Validate(id => services.ContainsKey(id), modules)
                .OrderBy(o => o.Severity)
                .ThenBy(o => model.Get(o.ItemId)?.Kind)
                .ToList();
            context.SetIssues(issues);
        }

        private string GetTargetName(TargetItem target)
        {
            if (target == null || string.IsNullOrEmpty(target.Source)) return null;

            if (target.IsRouter)
            {
                var router = model.Get(target.Source) as RouterItem;
                return router != null ? (!string.IsNullOrWhiteSpace(router.Name) ? router.Name : "No Name") : null;
            }

            var service = services.GetValueOrDefault(target.Source);
            return service != null ? (!string.IsNullOrEmpty(service.Name) ? service.Name : service.Id) : null;
        }

        /// <summary>
        /// The Modules whose Service calls go through the Router (as it is now, saved or not)
        /// </summary>
        private List<RoutingModule> GetModules(RouterItem router)
        {
            return router != null ? modules.Where(o => model.FindRouter(o.RouterKey) == router).ToList() : new List<RoutingModule>();
        }

        private string GetLayoutSnapshot()
        {
            return string.Join('|', Diagram.Nodes.OrderBy(o => o.Id, StringComparer.Ordinal).Select(o => $"{o.Id}:{Math.Round(o.Position.X)}:{Math.Round(o.Position.Y)}"));
        }

        #endregion

        #region "Add"

        private enum PaletteEntryType
        {
            Router,
            Route,
            Service,
            RouterTarget
        }

        private class PaletteEntry
        {
            public PaletteEntryType Type { get; init; }
            public string Source { get; init; }
        }

        private RoutingItem CreateItem(PaletteEntry entry)
        {
            switch (entry.Type)
            {
                case PaletteEntryType.Router:
                    return new RouterItem { Name = model.GetUniqueRouterName() };

                case PaletteEntryType.Route:
                    return new RouteItem { Patterns = new List<string> { "*" } };
                case PaletteEntryType.Service:
                    return new TargetItem { Type = TargetItem.ServiceType, Source = entry.Source };

                case PaletteEntryType.RouterTarget:
                    return new TargetItem { Type = TargetItem.RouterType, Source = entry.Source };

                default:
                    return null;
            }
        }

        /// <summary>
        /// Adds the item, connected to the selected item if it can be (ex. a Service Target added while a Route is selected), otherwise in the middle of the canvas
        /// </summary>
        private void Add(PaletteEntry entry)
        {
            var item = CreateItem(entry);
            if (item == null) return;

            model.Add(item);

            var parent = selected != null && model.CanConnect(selected, item) ? selected : null;
            var parentNode = GetNode(parent);

            Point position;
            if (parentNode != null)
            {
                var siblings = parent.Children.Select(GetNode).Where(o => o != null).Select(o => (o.Position.X, o.Position.Y));
                var childPosition = RoutingLayout.GetChildPosition((parentNode.Position.X, parentNode.Position.Y), siblings);
                position = GetFreePosition(new Point(childPosition.X, childPosition.Y));
            }
            else
            {
                position = GetCenterPosition();
            }

            var node = AddNode(item, position);
            if (parentNode != null)
            {
                model.Connect(parent, item);
                AddConnectionLink(parentNode, node);
            }

            Select(item, false);
            Changed();
        }

        private void StartDrag(PaletteEntry entry) => dragging = entry;

        // Items dragged onto the canvas are placed where they are dropped (and aren't connected)
        private void Drop(DragEventArgs args)
        {
            var entry = dragging;
            dragging = null;

            var item = entry != null ? CreateItem(entry) : null;
            if (item == null || Diagram.Container == null) return;

            model.Add(item);

            var point = Diagram.GetRelativeMousePoint(args.ClientX, args.ClientY);
            AddNode(item, new Point(RoutingLayout.Snap(point.X - NodeWidth / 2), RoutingLayout.Snap(point.Y - NodeHeight / 2)));

            Select(item, false);
            Changed();
        }

        private RoutingNode AddNode(RoutingItem item, Point position)
        {
            var node = new RoutingNode(item, position, model.CanConnect);
            Diagram.Nodes.Add(node);
            return node;
        }

        /// <summary>
        /// Left of the center of the canvas (so it isn't under the Inspector), moved down until it doesn't cover another node
        /// </summary>
        private Point GetCenterPosition()
        {
            var container = Diagram.Container;
            if (container == null) return new Point(0, 0);

            var center = Diagram.GetRelativeMousePoint(container.Left + container.Width * 0.3, container.Top + container.Height / 2);
            return GetFreePosition(new Point(RoutingLayout.Snap(center.X - NodeWidth / 2), RoutingLayout.Snap(center.Y - NodeHeight / 2)));
        }

        // Moves the position down until a new node there wouldn't cover another node
        private Point GetFreePosition(Point position)
        {
            var x = position.X;
            var y = position.Y;

            bool Covers(NodeModel node) =>
                x < node.Position.X + (node.Size?.Width ?? NodeWidth) && node.Position.X < x + NodeWidth &&
                y < node.Position.Y + (node.Size?.Height ?? NodeHeight) && node.Position.Y < y + NodeHeight;

            while (Diagram.Nodes.Any(Covers)) y = RoutingLayout.Snap(y + RoutingLayout.RowHeight);

            return new Point(x, y);
        }

        #endregion

        #region "Selection"

        private void Select(RoutingItem item, bool center)
        {
            var node = GetNode(item);
            if (node == null) return;

            Diagram.SelectModel(node, true);
            selected = item;

            // Left of center, so the node isn't under the Inspector (which is shown over the canvas on narrower screens)
            if (center && Diagram.Container != null)
            {
                var width = node.Size?.Width ?? NodeWidth;
                var height = node.Size?.Height ?? NodeHeight;
                Diagram.SetPan(
                    Diagram.Container.Width * 0.3 - (node.Position.X + width / 2) * Diagram.Zoom,
                    Diagram.Container.Height / 2 - (node.Position.Y + height / 2) * Diagram.Zoom);
            }
        }

        private void SelectIssue(RoutingIssue issue) => Select(model.Get(issue.ItemId), true);

        private void ClearSelection()
        {
            Diagram.UnselectAll();
            selected = null;
        }

        private void DeleteSelected()
        {
            // Removing the nodes removes their items from the Model (see NodeRemoved)
            var nodes = Diagram.GetSelectedModels().OfType<RoutingNode>().ToList();
            var links = Diagram.GetSelectedModels().OfType<BaseLinkModel>().ToList();

            Diagram.Batch(() =>
            {
                Diagram.Links.Remove(links);
                Diagram.Nodes.Remove(nodes);
            });

            selected = null;
        }

        private void Disconnect(RoutingItem item)
        {
            var link = connectionLinks.FirstOrDefault(o => o.Value == item.Id).Key;
            if (link != null) Diagram.Links.Remove(link);
        }

        #endregion

        #region "Edit"

        // Called by the Inspector when a value is changed
        private void ItemChanged() => Changed();

        private void SetTargetType(TargetItem target, string type)
        {
            if (string.Equals(target.Type, type, StringComparison.OrdinalIgnoreCase)) return;

            target.Type = type;
            target.Source = null;
            Changed();
        }

        // Changing a result gives the Target its own list (starting from the default)
        private void ToggleFallback(TargetItem target, string resultType, bool enabled)
        {
            var fallbackOn = target.EffectiveFallbackOn.ToList();
            fallbackOn.RemoveAll(o => string.Equals(o, resultType, StringComparison.OrdinalIgnoreCase));
            if (enabled) fallbackOn.Add(resultType);

            // Kept in the order of the options (so the same results always read the same). Others (ex. a Status Code) go last
            var options = TrakHoundServiceFallback.Options.Select(o => o.ToString()).ToList();
            int GetOrder(string value) => options.IndexOf(value) is var i && i >= 0 ? i : int.MaxValue;
            target.FallbackOn = fallbackOn.OrderBy(GetOrder).ToList();
            Changed();
        }

        private void ResetFallback(TargetItem target)
        {
            target.FallbackOn = null;
            Changed();
        }

        /// <summary>
        /// Moves the Target earlier or later in the order its Route tries its Targets in. The nodes swap places, so the diagram reads in the same order
        /// </summary>
        private void MoveTarget(TargetItem target, int offset)
        {
            var siblings = target.Parent?.Children;
            if (siblings == null) return;

            var index = siblings.ToList().IndexOf(target);
            var other = index + offset >= 0 && index + offset < siblings.Count ? siblings[index + offset] : null;

            if (other != null && model.Move(target, offset))
            {
                var node = GetNode(target);
                var otherNode = GetNode(other);
                if (node != null && otherNode != null)
                {
                    var position = node.Position;
                    node.SetPosition(otherNode.Position.X, otherNode.Position.Y);
                    otherNode.SetPosition(position.X, position.Y);
                }

                Changed();
            }
        }

        #endregion

        #region "Save"

        private async Task Save()
        {
            if (saving) return;

            saveErrors.Clear();
            Validate();

            if (ErrorCount > 0)
            {
                saveErrors.Add($"Fix the {ErrorCount} error{(ErrorCount != 1 ? "s" : "")} before saving");
                var first = issues.First(o => o.Severity == RoutingIssueSeverity.Error);
                SelectIssue(first);
                return;
            }

            if (Profile == null)
            {
                saveErrors.Add("The Configuration Profile isn't available");
                return;
            }

            // Routers changed (or removed) by something else since they were loaded would be overwritten
            var configurations = model.ToConfigurations();
            foreach (var loaded in loadedHashes)
            {
                var current = Profile.Get<TrakHoundServiceRouterConfiguration>(TrakHoundServiceRouterConfiguration.ConfigurationCategory, loaded.Key);
                if (current != null && current.Hash != loaded.Value)
                {
                    var name = !string.IsNullOrEmpty(current.Name) ? current.Name : current.Id;
                    saveErrors.Add($"The \"{name}\" Router was changed outside of this page. Use Discard to load the changes (yours will be lost)");
                }
            }
            if (saveErrors.Count > 0) return;

            saving = true;
            await InvokeAsync(StateHasChanged);

            var profile = Profile as TrakHoundConfigurationProfile;
            void ErrorReceived(object sender, string message) => saveErrors.Add(message);
            if (profile != null) profile.ErrorReceived += ErrorReceived;

            try
            {
                // Only the Routers that were loaded here (and have been removed) are removed. Routers added by something else are kept
                var ids = configurations.Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var removedId in loadedHashes.Keys.Where(o => !ids.Contains(o)).ToList())
                {
                    configurationsService.Remove(TrakHoundServiceRouterConfiguration.ConfigurationCategory, removedId);
                }

                // Unchanged Routers aren't written again
                foreach (var configuration in configurations)
                {
                    configurationsService.Save(configuration);
                }

                var layout = new TrakHoundDiagramConfiguration();
                layout.Id = RoutingDiagramId;
                layout.Nodes = Diagram.Nodes.Select(o => new TrakHoundDiagramNodeConfiguration { Id = o.Id, X = o.Position.X, Y = o.Position.Y }).ToList();
                configurationsService.Save(layout, TrakHound.Instance.Security.TrakHoundAdminSecurity.RouterConfigurations);

                loadedHashes = configurations.ToDictionary(o => o.Id, o => o.Hash, StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                saveErrors.Add($"Error saving the Routers : {ex.Message}");
            }
            finally
            {
                if (profile != null) profile.ErrorReceived -= ErrorReceived;
            }

            if (saveErrors.Count == 0)
            {
                savedSnapshot = model.GetSnapshot();
                savedLayoutSnapshot = GetLayoutSnapshot();
                saved = true;
            }

            saving = false;
            await InvokeAsync(StateHasChanged);
        }

        private void Discard()
        {
            Load();
            Changed();
        }

        #endregion

        #region "Toolbar"

        private void AutoArrange()
        {
            var positions = RoutingLayout.Arrange(model).ToDictionary(o => o.Key, o => new Point(o.Value.X, o.Value.Y));
            var selectedItem = selected;

            Build(positions);
            Select(selectedItem, false);
            fitOnRender = true;
        }

        private void FitToNodes() => FitToNodes(0.1);

        /// <summary>
        /// Zooms and pans so every node is shown (between the Minimum Zoom and 100%). If they don't fit at the Minimum Zoom, the left of the diagram (the Routers) is shown.
        /// Nodes that haven't been measured yet use their estimated size
        /// </summary>
        private void FitToNodes(double minZoom)
        {
            var container = Diagram.Container;
            if (container == null || container.Width <= 0 || container.Height <= 0 || Diagram.Nodes.Count == 0) return;

            const double margin = 40;
            var minX = Diagram.Nodes.Min(o => o.Position.X);
            var minY = Diagram.Nodes.Min(o => o.Position.Y);
            var maxX = Diagram.Nodes.Max(o => o.Position.X + (o.Size?.Width ?? NodeWidth));
            var maxY = Diagram.Nodes.Max(o => o.Position.Y + (o.Size?.Height ?? NodeHeight));

            var fitX = container.Width / (maxX - minX + 2 * margin);
            var fitY = container.Height / (maxY - minY + 2 * margin);
            var zoom = Math.Clamp(Math.Min(fitX, fitY), minZoom, 1);
            Diagram.SetZoom(zoom);

            // Center the nodes in the canvas (or start from the left / top if they don't fit)
            var offsetX = zoom <= fitX ? (container.Width - (maxX - minX) * zoom) / 2 : margin;
            var offsetY = zoom <= fitY ? (container.Height - (maxY - minY) * zoom) / 2 : margin;
            Diagram.SetPan(offsetX - minX * zoom, offsetY - minY * zoom);
        }

        #endregion

        #region "Map"

        /// <summary>
        /// Opens the Map. The Services' Routes are read from the running Services (a Service that isn't running is shown with unknown Routes)
        /// </summary>
        private async Task OpenMap()
        {
            if (mapLoading) return;
            mapLoading = true;

            Dictionary<string, IEnumerable<string>> routes = null;
            try
            {
                var client = configurationsService.Instance?.ClientProvider?.GetClient();
                var drivers = client?.Services?.Drivers != null ? await client.Services.Drivers.GetDrivers() : null;
                routes = drivers?.Where(o => !string.IsNullOrEmpty(o?.ConfigurationId)).GroupBy(o => o.ConfigurationId).ToDictionary(o => o.Key, o => o.First().Routes, StringComparer.Ordinal);
            }
            catch { }

            mapServices = services.Values
                .OrderBy(o => !string.IsNullOrEmpty(o.Name) ? o.Name : o.Id, StringComparer.OrdinalIgnoreCase)
                .Select(o => new TrakHoundServiceRouteService
                {
                    Id = o.Id,
                    Name = o.Name,
                    Routes = routes != null && routes.TryGetValue(o.Id, out var serviceRoutes) ? serviceRoutes?.Distinct().ToList() ?? new List<string>() : null
                })
                .ToList();

            mapLoading = false;
        }

        private void CloseMap() => mapServices = null;

        // Called by the Map to edit an item : the Map is closed and the item is selected
        private void EditFromMap(RoutingItem item)
        {
            mapServices = null;
            inspectorHidden = false;
            Select(item, true);
        }

        #endregion

        #region "Navigation"

        private void BeforeNavigation(LocationChangingContext args)
        {
            if (!IsDirty) return;

            leaveTarget = args.TargetLocation;
            args.PreventNavigation();
        }

        private void ConfirmLeave()
        {
            var target = leaveTarget;
            leaveTarget = null;
            leaveConfirmed = true;
            navigationManager.NavigateTo(target);
        }

        #endregion
    }
}
