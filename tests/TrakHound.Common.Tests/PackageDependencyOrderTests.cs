// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Packages;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// The order a group of Packages is built and published in (ex. by 'th packages build'), so each one follows the Packages it depends on
    /// </summary>
    public class PackageDependencyOrderTests
    {
        private record Item(string Id, params string[] Dependencies);

        private static List<string> Sort(out List<Item> cyclic, params Item[] items)
        {
            return TrakHoundPackageDependencyOrder.Sort(items, o => o.Id, o => o.Dependencies, out cyclic).Select(o => o.Id).ToList();
        }


        [Fact]
        public void Keeps_the_listed_order_without_dependencies()
        {
            var sorted = Sort(out var cyclic, new Item("C"), new Item("A"), new Item("B"));

            Assert.Equal(new[] { "C", "A", "B" }, sorted);
            Assert.Empty(cyclic);
        }

        [Fact]
        public void Places_a_package_after_its_dependency()
        {
            var sorted = Sort(out var cyclic, new Item("App", "Api"), new Item("Other"), new Item("Api"));

            Assert.Equal(new[] { "Other", "Api", "App" }, sorted);
            Assert.Empty(cyclic);
        }

        [Fact]
        public void Follows_a_chain_of_dependencies()
        {
            var sorted = Sort(out _, new Item("App", "Api"), new Item("Api", "Service"), new Item("Service"));

            Assert.Equal(new[] { "Service", "Api", "App" }, sorted);
        }

        [Fact]
        public void Ignores_dependencies_outside_the_group()
        {
            var sorted = Sort(out var cyclic, new Item("App", "Published.Elsewhere"), new Item("Api"));

            Assert.Equal(new[] { "App", "Api" }, sorted);
            Assert.Empty(cyclic);
        }

        [Fact]
        public void Matches_ids_without_regard_to_case()
        {
            var sorted = Sort(out _, new Item("App", "trakhound.api"), new Item("TrakHound.Api"));

            Assert.Equal(new[] { "TrakHound.Api", "App" }, sorted);
        }

        [Fact]
        public void Reports_a_cycle_and_still_returns_every_item()
        {
            var sorted = Sort(out var cyclic, new Item("A", "B"), new Item("B", "A"), new Item("C", "A"));

            Assert.Equal(new[] { "A", "B", "C" }, sorted);
            Assert.Equal("A", Assert.Single(cyclic).Id);
        }

        [Fact]
        public void Ignores_a_package_that_depends_on_itself()
        {
            var sorted = Sort(out var cyclic, new Item("A", "A"), new Item("B"));

            Assert.Equal(new[] { "A", "B" }, sorted);
            Assert.Empty(cyclic);
        }

        [Fact]
        public void Keeps_items_without_an_id()
        {
            var sorted = Sort(out var cyclic, new Item(null), new Item("App", "Api"), new Item("Api"));

            Assert.Equal(new[] { null, "Api", "App" }, sorted);
            Assert.Empty(cyclic);
        }
    }
}
