// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Pages.Configuration;
using TrakHound.Instance.Pages.Configuration.Routing;
using TrakHound.Services;

namespace TrakHound.Instance.Routing.Tests
{
    /// <summary>
    /// The Routing Map (Modules → Routers → Routes → Targets → Services), and the warnings about the Routers Modules use
    /// </summary>
    public class RoutingMapGraphTests
    {
        private static readonly ConfigurationCategoryDefinition Workers = ConfigurationCategories.All.First(o => o.Key == "workers");
        private static readonly ConfigurationCategoryDefinition Apis = ConfigurationCategories.All.First(o => o.Key == "api");

        // default : KeyValues => cache, then the archive Router. Blobs => files. archive : * => database
        private static RoutingModel Model() => RoutingModel.FromConfigurations(new[]
        {
            new TrakHoundServiceRouterConfiguration
            {
                Id = "r-default",
                Name = "default",
                Routes = new[]
                {
                    new TrakHoundServiceRouteConfiguration
                    {
                        Id = "route-kv",
                        Patterns = new[] { "KeyValues" },
                        Targets = new[]
                        {
                            new TrakHoundServiceTargetConfiguration { Id = "t-cache", Type = "Service", Source = "cache" },
                            new TrakHoundServiceTargetConfiguration { Id = "t-archive", Type = "Router", Source = "r-archive" }
                        }
                    },
                    new TrakHoundServiceRouteConfiguration
                    {
                        Id = "route-blobs",
                        Patterns = new[] { "Blobs" },
                        Targets = new[] { new TrakHoundServiceTargetConfiguration { Id = "t-files", Type = "Service", Source = "files" } }
                    }
                }
            },
            new TrakHoundServiceRouterConfiguration
            {
                Id = "r-archive",
                Name = "archive",
                Routes = new[]
                {
                    new TrakHoundServiceRouteConfiguration
                    {
                        Id = "route-all",
                        Patterns = new[] { "*" },
                        Targets = new[] { new TrakHoundServiceTargetConfiguration { Id = "t-database", Type = "Service", Source = "database" } }
                    }
                }
            },
            new TrakHoundServiceRouterConfiguration { Id = "r-unused", Name = "unused" }
        });

        private static readonly TrakHoundServiceRouteService[] Services =
        {
            new TrakHoundServiceRouteService { Id = "cache", Name = "Cache", Routes = new[] { "KeyValues" } },
            new TrakHoundServiceRouteService { Id = "files", Name = "Files", Routes = new[] { "Blobs" } },
            new TrakHoundServiceRouteService { Id = "database", Name = "Database", Routes = new[] { "KeyValues", "Tables" } },
            new TrakHoundServiceRouteService { Id = "spare", Name = "Spare", Routes = new[] { "Tables" } }
        };

        private static RoutingModule Module(ConfigurationCategoryDefinition category, string id, string routerId = null, string zoneId = null, string zoneRouterId = null) => new RoutingModule
        {
            Category = category,
            ConfigurationId = id,
            Name = id,
            RouterId = routerId,
            ZoneId = zoneId,
            ZoneRouterId = zoneRouterId
        };


        [Fact]
        public void Modules_Are_Grouped_By_The_Router_They_Use()
        {
            var modules = new[]
            {
                Module(Workers, "collector"),
                Module(Workers, "reporter", "archive"),
                Module(Apis, "status", "r-archive"),
                Module(Workers, "zoned", "default", "test", "archive")
            };

            var graph = RoutingMapGraph.Build(Model(), modules, Services);

            var defaultWorkers = Assert.Single(graph.GetIncoming("r-default"));
            Assert.Equal(new[] { "collector" }, graph.GetNode(defaultWorkers.From).Modules.Select(o => o.ConfigurationId));

            var archiveUsers = graph.GetIncoming("r-archive").Where(o => o.Kind == RoutingMapEdgeKind.Uses).Select(o => graph.GetNode(o.From)).ToList();
            Assert.Equal(new[] { "reporter", "status", "zoned" }, archiveUsers.SelectMany(o => o.Modules).Select(o => o.ConfigurationId).OrderBy(o => o));
        }

        [Fact]
        public void Missing_Routers_And_Direct_Services_Are_Shown()
        {
            var modules = new[] { Module(Workers, "lost", "removed"), Module(Workers, "direct", "spare") };

            var graph = RoutingMapGraph.Build(Model(), modules, Services);

            var missing = Assert.Single(graph.Nodes, o => o.Kind == RoutingMapNodeKind.MissingRouter);
            Assert.Equal("removed", missing.Title);
            Assert.Contains(graph.GetIncoming(RoutingMapGraph.GetServiceKey("spare")), o => o.Kind == RoutingMapEdgeKind.Uses);
        }

        [Fact]
        public void Targets_Link_To_Their_Services_And_Routers()
        {
            var graph = RoutingMapGraph.Build(Model(), Array.Empty<RoutingModule>(), Services);

            Assert.Contains(graph.Edges, o => o.From == "t-cache" && o.To == RoutingMapGraph.GetServiceKey("cache") && o.Kind == RoutingMapEdgeKind.Service);
            Assert.Contains(graph.Edges, o => o.From == "t-archive" && o.To == "r-archive" && o.Kind == RoutingMapEdgeKind.RouterTarget);
        }

        [Fact]
        public void Routes_Are_Split_Into_A_Lane_For_Each_Kind_They_Handle()
        {
            var graph = RoutingMapGraph.Build(Model(), Array.Empty<RoutingModule>(), Services);

            var lanes = graph.GetOutgoing("route-all").Select(o => graph.GetNode(o.To)).ToList();
            Assert.Equal(TrakHoundServiceRoutes.Names, lanes.Select(o => o.Route));
            Assert.Equal(new[] { "KeyValues" }, graph.GetOutgoing("route-kv").Select(o => graph.GetNode(o.To).Route));
        }

        [Fact]
        public void Lanes_Only_Link_The_Targets_That_Handle_Their_Kind()
        {
            var graph = RoutingMapGraph.Build(Model(), Array.Empty<RoutingModule>(), Services);

            // database handles KeyValues and Tables
            Assert.Single(graph.GetOutgoing(RoutingMapGraph.GetLaneKey("route-all", "Tables")), o => o.To == "t-database");
            Assert.Single(graph.GetOutgoing(RoutingMapGraph.GetLaneKey("route-all", "KeyValues")), o => o.To == "t-database");

            var blobs = graph.GetNode(RoutingMapGraph.GetLaneKey("route-all", "Blobs"));
            Assert.Empty(graph.GetOutgoing(blobs.Key));
            Assert.NotNull(blobs.Problem);

            // A Router Target is linked to every Lane of its Route
            Assert.Single(graph.GetOutgoing(RoutingMapGraph.GetLaneKey("route-kv", "KeyValues")), o => o.To == "t-archive");
        }

        [Fact]
        public void Unused_Routers_And_Services_Are_Marked()
        {
            var graph = RoutingMapGraph.Build(Model(), new[] { Module(Workers, "collector") }, Services);

            Assert.True(graph.GetNode("r-unused").IsUnused);
            Assert.False(graph.GetNode("r-archive").IsUnused);
            Assert.True(graph.GetNode(RoutingMapGraph.GetServiceKey("spare")).IsUnused);
            Assert.False(graph.GetNode(RoutingMapGraph.GetServiceKey("database")).IsUnused);
        }

        [Fact]
        public void A_Kind_Of_Request_Only_Reaches_Matching_Routes_And_Services()
        {
            var graph = RoutingMapGraph.Build(Model(), Array.Empty<RoutingModule>(), Services);

            var active = graph.GetActive("Tables");

            Assert.DoesNotContain("route-kv", active);
            Assert.DoesNotContain("t-cache", active);
            Assert.Contains("route-all", active);
            Assert.Contains(RoutingMapGraph.GetLaneKey("route-all", "Tables"), active);
            Assert.DoesNotContain(RoutingMapGraph.GetLaneKey("route-all", "KeyValues"), active);
            Assert.Contains(RoutingMapGraph.GetServiceKey("database"), active);
            Assert.DoesNotContain(RoutingMapGraph.GetServiceKey("files"), active);
        }

        [Fact]
        public void Selecting_A_Module_Follows_Its_Requests_Through_Router_Targets()
        {
            var graph = RoutingMapGraph.Build(Model(), new[] { Module(Workers, "collector") }, Services);
            var group = graph.Nodes.Single(o => o.Kind == RoutingMapNodeKind.Modules);

            var connected = graph.GetConnected(group.Key);

            Assert.Contains("r-archive", connected);
            Assert.Contains(RoutingMapGraph.GetServiceKey("database"), connected);
            Assert.DoesNotContain("r-unused", connected);
            Assert.DoesNotContain(RoutingMapGraph.GetServiceKey("spare"), connected);
        }


        #region "Module Warnings"

        // The issues only found when the Modules are checked
        private static List<RoutingIssue> Validate(RoutingModel model, params RoutingModule[] modules)
        {
            bool ServiceExists(string id) => Services.Any(o => o.Id == id);
            var withoutModules = model.Validate(ServiceExists).Select(o => $"{o.ItemId}|{o.Message}").ToHashSet();
            return model.Validate(ServiceExists, modules).Where(o => !withoutModules.Contains($"{o.ItemId}|{o.Message}")).ToList();
        }

        [Fact]
        public void Modules_Using_Existing_Routers_Have_No_Warnings()
        {
            Assert.Empty(Validate(Model(), Module(Workers, "collector"), Module(Workers, "reporter", "ARCHIVE"), Module(Workers, "direct", "spare")));
        }

        [Fact]
        public void Renaming_A_Router_Modules_Use_By_Name_Is_A_Warning()
        {
            var model = Model();
            ((RouterItem)model.Get("r-archive")).Name = "history";

            var issue = Assert.Single(Validate(model, Module(Workers, "reporter", "archive")));
            Assert.Equal("r-archive", issue.ItemId);

            // Modules that use it by ID still find it
            Assert.Empty(Validate(model, Module(Workers, "reporter", "r-archive")));
        }

        [Fact]
        public void Deleting_A_Router_Modules_Use_Is_A_Warning()
        {
            var model = Model();
            model.Remove(model.Get("r-unused"));

            var issue = Assert.Single(Validate(model, Module(Workers, "reporter", "unused")));
            Assert.Null(issue.ItemId);
            Assert.Equal(RoutingIssueSeverity.Warning, issue.Severity);
        }

        [Fact]
        public void Renaming_The_Default_Router_Is_A_Warning()
        {
            var model = Model();
            ((RouterItem)model.Get("r-default")).Name = "main";

            Assert.Single(Validate(model, Module(Workers, "collector")));
        }

        #endregion
    }
}
