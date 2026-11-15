// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TrakHound.Blazor.Routing;

namespace TrakHound.Instance.Routing.Tests
{
    /// <summary>
    /// App and Admin Pages are matched and their [Parameter] Properties set the way Blazor's Router does it (as in the debug host)
    /// </summary>
    public class PageRouteMatchTests
    {
        private static MatchResult Match(string template, string path) => new Route { Template = template, Handler = typeof(OrderPage) }.Match(path);

        private static Dictionary<string, object> Parameters(string template, string path)
        {
            var matchResult = Match(template, path);
            Assert.True(matchResult.IsMatch);
            return TrakHoundRouteParameters.GetRouteParameters(typeof(OrderPage), matchResult);
        }


        [Theory]
        [InlineData("/orders/{id:int}", "/orders/42", true)]
        [InlineData("/orders/{id:int}", "/orders/abc", false)]
        [InlineData("/orders/{id:guid}", "/orders/1f0c6c5e-8d2e-4f5a-9a51-2f4b1f7f3a10", true)]
        [InlineData("/orders/{id:guid}", "/orders/42", false)]
        [InlineData("/orders/{id:bool}", "/orders/true", true)]
        [InlineData("/orders/{id:decimal}", "/orders/1.5", true)]
        [InlineData("/orders/{id:datetime}", "/orders/2026-11-11", true)]
        [InlineData("/orders/{id:long}", "/orders/1.5", false)]
        [InlineData("/files/{name:nonfile}", "/files/report.pdf", false)]
        [InlineData("/files/{name:nonfile}", "/files/report", true)]
        public void Constraints_Are_Enforced(string template, string path, bool isMatch)
        {
            Assert.Equal(isMatch, Match(template, path).IsMatch);
        }

        [Theory]
        [InlineData("/orders/{id?}", "/orders", true)]
        [InlineData("/orders/{id?}", "/orders/42", true)]
        [InlineData("/orders/{id:int?}", "/orders/abc", false)]
        [InlineData("/orders/{id?}", "/orders/42/lines", false)]
        [InlineData("/files/{*path}", "/files", true)]
        [InlineData("/files/{*path}", "/files/a/b/c.txt", true)]
        [InlineData("/orders", "/orders/42", false)]
        [InlineData("/orders/{id}", "/orders", false)]
        [InlineData("/", "/", true)]
        public void Optional_And_CatchAll_Parameters_Match(string template, string path, bool isMatch)
        {
            Assert.Equal(isMatch, Match(template, path).IsMatch);
        }

        [Fact]
        public void Literal_Segments_Are_Case_Sensitive()
        {
            // As before, so a differently cased Path (ex. "/_ADMIN") never reaches a Page
            Assert.False(Match("/_admin/settings", "/_ADMIN/settings").IsMatch);
        }

        [Fact]
        public void Route_Values_Are_Decoded()
        {
            Assert.Equal("a b", Parameters("/orders/{name}", "/orders/a%20b")["Name"]);
            Assert.Equal("a b/c.txt", Match("/files/{*path}", "/files/a%20b/c.txt").RouteValues["path"]);
        }

        [Fact]
        public void Parameter_Names_Are_Matched_Case_Insensitively()
        {
            Assert.Equal(42, Parameters("/orders/{ID}", "/orders/42")["Id"]);
            Assert.Equal(42, Parameters("/orders/{id}", "/orders/42")["Id"]);
        }

        [Fact]
        public void Values_Are_Converted_To_The_Property_Type()
        {
            var parameters = Parameters("/orders/{id}/{key}/{at}/{status}/{quantity}", "/orders/42/1f0c6c5e-8d2e-4f5a-9a51-2f4b1f7f3a10/2026-11-11/shipped/1.5");

            Assert.Equal(42, parameters["Id"]);
            Assert.Equal(Guid.Parse("1f0c6c5e-8d2e-4f5a-9a51-2f4b1f7f3a10"), parameters["Key"]);
            Assert.Equal(new DateTime(2026, 11, 11), parameters["At"]);
            Assert.Equal(OrderStatus.Shipped, parameters["Status"]);
            Assert.Equal(1.5m, parameters["Quantity"]);
        }

        [Fact]
        public void Missing_Optional_Parameter_Is_Cleared()
        {
            // Null (the default for a value type), so a value from a previous Navigation to the same Page isn't kept
            var parameters = Parameters("/orders/{key?}", "/orders");

            Assert.True(parameters.ContainsKey("Key"));
            Assert.Null(parameters["Key"]);
        }

        [Fact]
        public void Properties_Not_In_The_Template_Are_Not_Set()
        {
            var parameters = Parameters("/orders/{id}", "/orders/42");

            Assert.Equal(new[] { "Id" }, parameters.Keys);
        }

        [Theory]
        [InlineData("/sections", "/sections")]
        [InlineData("/sections/{id:int}", "/sections/42")]
        public void A_Parameter_Only_In_Another_Route_Of_The_Page_Is_Cleared(string template, string path)
        {
            // The Page is kept when navigating between its Routes, so the Section of the last Route would otherwise stay
            var matchResult = new Route { Template = template, Handler = typeof(SectionPage) }.Match(path);
            Assert.True(matchResult.IsMatch);

            var parameters = TrakHoundRouteParameters.GetRouteParameters(typeof(SectionPage), matchResult);

            Assert.True(parameters.ContainsKey("Section"));
            Assert.Null(parameters["Section"]);

            // Not in any Route
            Assert.False(parameters.ContainsKey("Title"));
        }

        [Fact]
        public void Query_Values_Are_Decoded_Converted_And_Grouped()
        {
            var parameters = TrakHoundRouteParameters.GetQueryParameters(typeof(OrderPage), "?search=a%20b%3Dc&page=3&tags=x&TAGS=y&only=1");

            Assert.Equal("a b=c", parameters["Search"]);
            Assert.Equal(3, parameters["Page"]);
            Assert.Equal(new[] { "x", "y" }, parameters["Tags"]);

            // A Property without [Parameter] only accepts Blazor's cascading value
            Assert.False(parameters.ContainsKey("Only"));
        }

        [Theory]
        [InlineData("http://localhost/orders/42?search=a%20b&page=3", "42 [a b] 3")]
        [InlineData("http://localhost/orders/42", "42 [] ")]
        public async Task Blazor_Supplies_Query_Parameters_With_The_Route_Parameters(string location, string expected)
        {
            // The Router passes only the Route Parameters, and Blazor's own provider (registered by AddRazorComponents)
            // supplies [SupplyParameterFromQuery] from the location, with or without [Parameter]
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<NavigationManager>(new TestNavigationManager(location))
                .AddSupplyValueFromQueryProvider()
                .BuildServiceProvider();

            await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

            var path = new Uri(location).AbsolutePath;
            var parameters = TrakHoundRouteParameters.GetRouteParameters(typeof(QueryPage), new Route { Template = "/orders/{id:int}", Handler = typeof(QueryPage) }.Match(path));

            var html = await renderer.Dispatcher.InvokeAsync(async () =>
                (await renderer.RenderComponentAsync<QueryPage>(ParameterView.FromDictionary(parameters))).ToHtmlString());

            Assert.Equal(expected, html);
        }

        [Fact]
        public void Constrained_Templates_Are_Matched_Before_Unconstrained()
        {
            var sorted = new[] { "/orders/{*path}", "/orders/{name}", "/orders/{id?}", "/orders/{id:int}", "/orders/new" }
                .OrderBy(o => o, new TrakHoundRouteTemplate.Comparer())
                .ToList();

            Assert.Equal(new[] { "/orders/new", "/orders/{id:int}", "/orders/{name}", "/orders/{id?}", "/orders/{*path}" }, sorted);
        }


        public enum OrderStatus { Open, Shipped }

        private class TestNavigationManager : NavigationManager
        {
            public TestNavigationManager(string uri) => Initialize("http://localhost/", uri);
        }

        public class QueryPage : ComponentBase
        {
            [Parameter] public int Id { get; set; }
            [SupplyParameterFromQuery] public string Search { get; set; }
            [Parameter, SupplyParameterFromQuery] public int? Page { get; set; }

            protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, $"{Id} [{Search}] {Page}");
        }

        [Route("/sections")]
        [Route("/sections/{id:int}")]
        [Route("/sections/by-name/{Section?}")]
        public class SectionPage : ComponentBase
        {
            [Parameter] public int Id { get; set; }
            [Parameter] public string Section { get; set; }
            [Parameter] public string Title { get; set; }
        }

        public class OrderPage : ComponentBase
        {
            [Parameter] public int Id { get; set; }
            [Parameter] public Guid? Key { get; set; }
            [Parameter] public DateTime At { get; set; }
            [Parameter] public OrderStatus Status { get; set; }
            [Parameter] public decimal Quantity { get; set; }
            [Parameter] public string Name { get; set; }

            [Parameter, SupplyParameterFromQuery] public string Search { get; set; }
            [Parameter, SupplyParameterFromQuery] public int? Page { get; set; }
            [Parameter, SupplyParameterFromQuery] public string[] Tags { get; set; }
            [SupplyParameterFromQuery] public string Only { get; set; }
        }
    }
}
