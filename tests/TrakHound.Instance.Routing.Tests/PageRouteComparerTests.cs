// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Blazor.Routing;

namespace TrakHound.Instance.Routing.Tests
{
    /// <summary>
    /// The order the Instance's pages are matched in : templates without Parameters first, and a literal Segment before a Parameter
    /// </summary>
    public class PageRouteComparerTests
    {
        private static List<string> Sort(params string[] templates) => templates.OrderBy(o => o, new TrakHoundInstancePageRouteManager.RouteComparer()).ToList();


        [Fact]
        public void Literal_Segment_Comes_Before_A_Parameter()
        {
            var sorted = Sort("/_admin/configuration/{CategoryKey}/{ConfigurationId}", "/_admin/configuration/groups/{GroupId}");

            Assert.Equal("/_admin/configuration/groups/{GroupId}", sorted[0]);
        }

        [Fact]
        public void Templates_Without_Parameters_Come_First()
        {
            var sorted = Sort("/_admin/configuration/{CategoryKey}", "/_admin/configuration/groups", "/_admin/configuration/variables");

            Assert.Equal("/_admin/configuration/{CategoryKey}", sorted.Last());
        }

        [Fact]
        public void Order_Does_Not_Depend_On_Input_Order()
        {
            var templates = new[] { "/_admin/configuration/{CategoryKey}/{ConfigurationId}", "/_admin/configuration/groups/{GroupId}", "/_admin/configuration/zones/{ZoneId}" };

            Assert.Equal(Sort(templates), Sort(templates.Reverse().ToArray()));
        }
    }
}
