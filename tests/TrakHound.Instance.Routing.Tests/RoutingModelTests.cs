// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Instance.Pages.Configuration.Routing;
using TrakHound.Services;

namespace TrakHound.Instance.Routing.Tests
{
    public class RoutingModelTests
    {
        // default : KeyValues => database (falls back on NotFound and Empty), then archive. * => cache
        private static TrakHoundServiceRouterConfiguration DefaultRouter() => new TrakHoundServiceRouterConfiguration
        {
            Id = "router-1",
            Name = "default",
            Description = "The main Router",
            Routes = new[]
            {
                new TrakHoundServiceRouteConfiguration
                {
                    Id = "route-1",
                    Patterns = new[] { "KeyValues" },
                    Filters = new[] { "objects" },
                    Targets = new[]
                    {
                        new TrakHoundServiceTargetConfiguration { Id = "target-1", Type = "Service", Source = "database", FallbackOn = new List<string> { "NotFound", "Empty" } },
                        new TrakHoundServiceTargetConfiguration { Id = "target-2", Type = "Service", Source = "archive" }
                    }
                },
                new TrakHoundServiceRouteConfiguration
                {
                    Id = "route-2",
                    Patterns = new[] { "*" },
                    Targets = new[]
                    {
                        new TrakHoundServiceTargetConfiguration { Id = "target-3", Type = "Service", Source = "cache" }
                    }
                }
            }
        };

        private static TrakHoundServiceRouterConfiguration Router(string id, string name, params (string Type, string Source)[] targets) => new TrakHoundServiceRouterConfiguration
        {
            Id = id,
            Name = name,
            Routes = new[]
            {
                new TrakHoundServiceRouteConfiguration
                {
                    Id = $"{id}-route",
                    Patterns = new[] { "*" },
                    Targets = targets.Select((o, i) => new TrakHoundServiceTargetConfiguration { Id = $"{id}-target-{i}", Type = o.Type, Source = o.Source }).ToArray()
                }
            }
        };

        private static string Json(object value) => JsonSerializer.Serialize(value);

        private static readonly HashSet<string> _services = new HashSet<string> { "database", "archive", "cache" };


        #region "Configurations"

        [Fact]
        public void Configurations_Are_Read_And_Written_Without_Changes()
        {
            var configuration = DefaultRouter();

            var model = RoutingModel.FromConfigurations(new[] { configuration });
            var saved = Assert.Single(model.ToConfigurations());

            Assert.Equal(Json(configuration), Json(saved));
        }

        [Fact]
        public void Every_Item_Is_Read()
        {
            var model = RoutingModel.FromConfigurations(new[] { DefaultRouter() });

            Assert.Single(model.Routers);
            Assert.Equal(2, model.Items.OfType<RouteItem>().Count());
            Assert.Equal(3, model.Items.OfType<TargetItem>().Count());
            Assert.Empty(model.Unconnected);

            var route = (RouteItem)model.Get("route-1");
            Assert.Equal(new[] { "target-1", "target-2" }, route.Targets.Select(o => o.Id));

            var database = (TargetItem)model.Get("target-1");
            Assert.Equal(new[] { "NotFound", "Empty" }, database.FallbackOn);
            Assert.Null(((TargetItem)model.Get("target-2")).FallbackOn);
        }

        [Fact]
        public void Targets_Without_Fallback_Results_Use_The_Default()
        {
            var model = RoutingModel.FromConfigurations(new[] { DefaultRouter() });
            var archive = (TargetItem)model.Get("target-2");

            Assert.Equal(TrakHoundServiceFallback.Default.Select(o => o.ToString()), archive.EffectiveFallbackOn);
            Assert.Null(model.ToConfigurations().Single().Routes.First().Targets.Last().FallbackOn);
        }

        [Fact]
        public void Targets_That_Never_Fall_Back_Are_Saved_With_An_Empty_List()
        {
            var model = RoutingModel.FromConfigurations(new[] { DefaultRouter() });
            ((TargetItem)model.Get("target-2")).FallbackOn = new List<string>();

            var saved = model.ToConfigurations().Single().Routes.First().Targets.Last();
            Assert.NotNull(saved.FallbackOn);
            Assert.Empty(saved.FallbackOn);
        }

        [Fact]
        public void Targets_Can_Be_Moved_Earlier_Or_Later()
        {
            var model = RoutingModel.FromConfigurations(new[] { DefaultRouter() });
            var route = (RouteItem)model.Get("route-1");
            var archive = model.Get("target-2");

            Assert.True(model.Move(archive, -1));
            Assert.Equal(new[] { "target-2", "target-1" }, route.Targets.Select(o => o.Id));
            Assert.Equal(new[] { "target-2", "target-1" }, model.ToConfigurations().Single().Routes.First().Targets.Select(o => o.Id));

            Assert.False(model.Move(archive, -1));
            Assert.False(model.Move(model.Get("router-1"), 1));
        }

        [Fact]
        public void Duplicate_Ids_Are_Replaced()
        {
            // The same Target ID under two Routes (saved by earlier versions of the page)
            var configuration = Router("a", "default", ("Service", "database"), ("Service", "cache"));
            configuration.Routes.First().Targets.Last().Id = configuration.Routes.First().Targets.First().Id;

            var model = RoutingModel.FromConfigurations(new[] { configuration });

            var targets = model.Items.OfType<TargetItem>().ToList();
            Assert.Equal(2, targets.Count);
            Assert.Equal(2, targets.Select(o => o.Id).Distinct().Count());
        }

        [Fact]
        public void Items_That_Are_Not_Connected_Are_Not_Saved()
        {
            var model = RoutingModel.FromConfigurations(new[] { DefaultRouter() });

            model.Disconnect(model.Get("target-1"));

            var saved = Assert.Single(model.ToConfigurations());
            var route = saved.Routes.Single(o => o.Id == "route-1");
            Assert.Equal(new[] { "target-2" }, route.Targets.Select(o => o.Id));
            Assert.Contains(model.Get("target-1"), model.Unconnected);
        }

        [Fact]
        public void Empty_Values_Are_Not_Saved()
        {
            var model = new RoutingModel();
            var router = model.Add(new RouterItem("r") { Name = "default", Description = "" });
            var route = model.Add(new RouteItem("route") { Patterns = new List<string> { " * ", "", "  " } });
            model.Connect(router, route);

            var saved = Assert.Single(model.ToConfigurations());
            Assert.Null(saved.Description);
            Assert.Equal(new[] { "*" }, saved.Routes.Single().Patterns);
            Assert.Null(saved.Routes.Single().Filters);
        }

        #endregion

        #region "Connections"

        [Theory]
        [InlineData(RoutingItemKind.Router, RoutingItemKind.Route, true)]
        [InlineData(RoutingItemKind.Route, RoutingItemKind.Target, true)]
        [InlineData(RoutingItemKind.Router, RoutingItemKind.Target, false)]
        [InlineData(RoutingItemKind.Router, RoutingItemKind.Router, false)]
        [InlineData(RoutingItemKind.Route, RoutingItemKind.Route, false)]
        [InlineData(RoutingItemKind.Target, RoutingItemKind.Target, false)]
        [InlineData(RoutingItemKind.Target, RoutingItemKind.Route, false)]
        [InlineData(RoutingItemKind.Route, RoutingItemKind.Router, false)]
        public void Only_Allowed_Kinds_Can_Be_Connected(RoutingItemKind parent, RoutingItemKind child, bool expected)
        {
            Assert.Equal(expected, RoutingModel.IsAllowed(parent, child));
        }

        [Fact]
        public void An_Item_Can_Only_Have_One_Parent()
        {
            var model = new RoutingModel();
            var a = model.Add(new RouterItem());
            var b = model.Add(new RouterItem());
            var route = model.Add(new RouteItem());

            Assert.True(model.Connect(a, route));
            Assert.False(model.CanConnect(b, route));

            model.Disconnect(route);
            Assert.True(model.Connect(b, route));
            Assert.Empty(a.Children);
            Assert.Same(b, route.Parent);
        }

        [Fact]
        public void Removing_An_Item_Disconnects_Its_Children()
        {
            var model = RoutingModel.FromConfigurations(new[] { DefaultRouter() });
            var route = model.Get("route-1");

            model.Remove(route);

            Assert.Null(model.Get("route-1"));
            Assert.Null(model.Get("target-1").Parent);
            Assert.Contains(model.Get("target-1"), model.Unconnected);
            Assert.Single(model.ToConfigurations().Single().Routes);
        }

        #endregion

        #region "Validation"

        private static List<RoutingIssue> Validate(RoutingModel model, string itemId) => model.Validate(_services.Contains).Where(o => o.ItemId == itemId).ToList();

        [Fact]
        public void A_Valid_Router_Has_No_Issues()
        {
            var model = RoutingModel.FromConfigurations(new[] { DefaultRouter() });

            Assert.Empty(model.Validate(_services.Contains));
        }

        [Fact]
        public void Router_Names_Must_Be_Unique()
        {
            var model = RoutingModel.FromConfigurations(new[] { Router("a", "default", ("Service", "cache")), Router("b", "Default ", ("Service", "cache")) });

            var issues = model.Validate(_services.Contains);
            Assert.Contains(issues, o => o.ItemId == "a" && o.Severity == RoutingIssueSeverity.Error);
            Assert.Contains(issues, o => o.ItemId == "b" && o.Severity == RoutingIssueSeverity.Error);
        }

        [Fact]
        public void Routers_Need_A_Name()
        {
            var model = RoutingModel.FromConfigurations(new[] { Router("a", " ", ("Service", "cache")) });

            Assert.Contains(Validate(model, "a"), o => o.Severity == RoutingIssueSeverity.Error);
        }

        [Fact]
        public void Routes_Need_A_Pattern()
        {
            var model = RoutingModel.FromConfigurations(new[] { DefaultRouter() });
            ((RouteItem)model.Get("route-2")).Patterns.Clear();

            Assert.Contains(Validate(model, "route-2"), o => o.Severity == RoutingIssueSeverity.Error);
        }

        [Theory]
        [InlineData("*")]
        [InlineData("KeyValues")]
        [InlineData("!Tables")]
        [InlineData("Message.*")]
        [InlineData("^(Blobs|Tables)$")]
        public void Patterns_That_Match_A_Route_Have_No_Warning(string pattern)
        {
            Assert.Null(RoutingModel.GetPatternWarning(pattern));
        }

        [Theory]
        [InlineData("keyvalues")]
        [InlineData("KeyValue$")]
        [InlineData("!Table$")]
        [InlineData("[Blobs")]
        public void Patterns_That_Match_No_Route_Are_Warnings(string pattern)
        {
            var model = RoutingModel.FromConfigurations(new[] { DefaultRouter() });
            ((RouteItem)model.Get("route-1")).Patterns.Add(pattern);

            var issue = Assert.Single(Validate(model, "route-1"));
            Assert.Equal(RoutingIssueSeverity.Warning, issue.Severity);
        }

        [Fact]
        public void Missing_Services_Are_Warnings()
        {
            var model = RoutingModel.FromConfigurations(new[] { Router("a", "default", ("Service", "not-configured")) });

            var issue = Assert.Single(Validate(model, "a-target-0"));
            Assert.Equal(RoutingIssueSeverity.Warning, issue.Severity);
        }

        [Fact]
        public void Router_Targets_Must_Exist()
        {
            var model = RoutingModel.FromConfigurations(new[] { Router("a", "default", ("Router", "deleted")) });

            Assert.Contains(Validate(model, "a-target-0"), o => o.Severity == RoutingIssueSeverity.Error);
        }

        [Fact]
        public void Router_Targets_Can_Not_Loop()
        {
            // a => b => a
            var model = RoutingModel.FromConfigurations(new[] { Router("a", "default", ("Router", "b")), Router("b", "other", ("Router", "a")) });

            Assert.Contains(Validate(model, "a-target-0"), o => o.Severity == RoutingIssueSeverity.Error);
            Assert.Contains(Validate(model, "b-target-0"), o => o.Severity == RoutingIssueSeverity.Error);
        }

        [Fact]
        public void Router_Targets_Can_Not_Target_Their_Own_Router()
        {
            var model = RoutingModel.FromConfigurations(new[] { Router("a", "default", ("Router", "a")) });

            Assert.Contains(Validate(model, "a-target-0"), o => o.Severity == RoutingIssueSeverity.Error);
        }

        [Fact]
        public void Router_Targets_Without_A_Loop_Are_Valid()
        {
            // a => b => cache
            var model = RoutingModel.FromConfigurations(new[] { Router("a", "default", ("Router", "b")), Router("b", "other", ("Service", "cache")) });

            Assert.Empty(model.Validate(_services.Contains));
        }

        [Fact]
        public void Items_That_Are_Not_Connected_Are_Warnings()
        {
            var model = new RoutingModel();
            var route = model.Add(new RouteItem { Patterns = new List<string> { "*" } });

            Assert.Contains(Validate(model, route.Id), o => o.Severity == RoutingIssueSeverity.Warning && o.Message.Contains("Not connected"));
        }

        #endregion

        #region "Names and Snapshots"

        [Fact]
        public void New_Routers_Get_A_Unique_Name()
        {
            var model = new RoutingModel();
            Assert.Equal("default", model.GetUniqueRouterName());

            model.Add(new RouterItem { Name = "Default" });
            Assert.Equal("router", model.GetUniqueRouterName());

            model.Add(new RouterItem { Name = "router" });
            model.Add(new RouterItem { Name = "router-2" });
            Assert.Equal("router-3", model.GetUniqueRouterName());
        }

        [Fact]
        public void Snapshots_Change_When_Items_Change()
        {
            var model = RoutingModel.FromConfigurations(new[] { DefaultRouter() });
            var snapshot = model.GetSnapshot();

            Assert.Equal(snapshot, RoutingModel.FromConfigurations(new[] { DefaultRouter() }).GetSnapshot());

            ((RouterItem)model.Get("router-1")).Name = "renamed";
            Assert.NotEqual(snapshot, model.GetSnapshot());

            ((RouterItem)model.Get("router-1")).Name = "default";
            Assert.Equal(snapshot, model.GetSnapshot());

            model.Disconnect(model.Get("target-3"));
            Assert.NotEqual(snapshot, model.GetSnapshot());
        }

        #endregion
    }
}
