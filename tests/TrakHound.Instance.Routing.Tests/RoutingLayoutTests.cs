// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Pages.Configuration.Routing;

namespace TrakHound.Instance.Routing.Tests
{
    public class RoutingLayoutTests
    {
        [Fact]
        public void Items_Are_Placed_In_Columns_By_Depth()
        {
            var model = new RoutingModel();
            var router = model.Add(new RouterItem { Name = "default" });
            var route = model.Add(new RouteItem());
            var target = model.Add(new TargetItem());
            var fallbackTarget = model.Add(new TargetItem());
            model.Connect(router, route);
            model.Connect(route, target);
            model.Connect(route, fallbackTarget);

            var positions = RoutingLayout.Arrange(model);

            Assert.Equal(0, positions[router.Id].X);
            Assert.Equal(RoutingLayout.Snap(RoutingLayout.ColumnWidth), positions[route.Id].X);
            Assert.Equal(RoutingLayout.Snap(2 * RoutingLayout.ColumnWidth), positions[target.Id].X);
            Assert.Equal(RoutingLayout.Snap(2 * RoutingLayout.ColumnWidth), positions[fallbackTarget.Id].X);

            // A Route's Targets are placed top to bottom in the order they are tried
            Assert.True(positions[target.Id].Y < positions[fallbackTarget.Id].Y);
        }

        [Fact]
        public void Parents_Are_Centered_On_Their_Children()
        {
            var model = new RoutingModel();
            var router = model.Add(new RouterItem { Name = "default" });
            var route = model.Add(new RouteItem());
            var targets = Enumerable.Range(0, 3).Select(_ => model.Add(new TargetItem())).ToList();
            model.Connect(router, route);
            foreach (var target in targets) model.Connect(route, target);

            var positions = RoutingLayout.Arrange(model);

            Assert.Equal(positions[targets[1].Id].Y, positions[route.Id].Y);
            Assert.Equal(positions[route.Id].Y, positions[router.Id].Y);
            Assert.True(positions[targets[0].Id].Y < positions[targets[1].Id].Y);
            Assert.True(positions[targets[1].Id].Y < positions[targets[2].Id].Y);
        }

        [Fact]
        public void Items_Do_Not_Overlap()
        {
            var model = new RoutingModel();
            foreach (var name in new[] { "a", "b" })
            {
                var router = model.Add(new RouterItem { Name = name });
                for (var i = 0; i < 2; i++)
                {
                    var route = model.Add(new RouteItem());
                    model.Connect(router, route);
                    for (var j = 0; j < 2; j++) model.Connect(route, model.Add(new TargetItem()));
                }
            }
            model.Add(new TargetItem());

            var positions = RoutingLayout.Arrange(model);

            Assert.Equal(model.Items.Count(), positions.Count);
            Assert.Equal(positions.Count, positions.Values.Distinct().Count());
        }

        [Fact]
        public void Items_That_Are_Not_Connected_Are_Placed_Below()
        {
            var model = new RoutingModel();
            var router = model.Add(new RouterItem { Name = "default" });
            var route = model.Add(new RouteItem());
            model.Connect(router, route);
            var unconnected = model.Add(new TargetItem());

            var positions = RoutingLayout.Arrange(model);

            Assert.True(positions[unconnected.Id].Y > positions[route.Id].Y);
            Assert.Equal(RoutingLayout.Snap(2 * RoutingLayout.ColumnWidth), positions[unconnected.Id].X);
        }

        [Fact]
        public void New_Children_Are_Placed_Below_Their_Siblings()
        {
            var parent = (X: 0.0, Y: 0.0);

            Assert.Equal((RoutingLayout.Snap(RoutingLayout.ColumnWidth), 0.0), RoutingLayout.GetChildPosition(parent, Enumerable.Empty<(double, double)>()));

            var below = RoutingLayout.GetChildPosition(parent, new[] { (330.0, 0.0), (330.0, 105.0) });
            Assert.Equal(RoutingLayout.Snap(210), below.Y);
        }
    }
}
