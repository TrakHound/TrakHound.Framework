// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using System.Reflection;

namespace TrakHound.Instance.Http.Tests
{
    /// <summary>
    /// Every Admin interface page links to its docs page on the TrakHound website (the help icon in its area's header)
    /// </summary>
    public class AdminHelpLinkTests
    {
        // The sign in page has no area header (and so no help link)
        private const string _loginPage = "_admin/login";


        // Every Admin interface route, with its parameters filled in (ex. "_admin/configuration/zones/{ZoneId}" => "_admin/configuration/zones/x")
        public static IEnumerable<object[]> AdminRoutes => typeof(InstanceApp).Assembly.GetTypes()
            .Where(o => typeof(IComponent).IsAssignableFrom(o))
            .SelectMany(o => o.GetCustomAttributes<RouteAttribute>())
            .Select(o => o.Template.Trim('/'))
            .Where(o => o == "_admin" || o.StartsWith("_admin/", StringComparison.OrdinalIgnoreCase))
            .Select(o => string.Join('/', o.Split('/').Select(s => s.StartsWith('{') ? "x" : s)))
            .Where(o => o != _loginPage)
            .Distinct()
            .Select(o => new object[] { o });


        [Theory]
        [MemberData(nameof(AdminRoutes))]
        public void AdminPage_HasHelpLink(string path)
        {
            var link = TrakHoundWebsite.GetAdminHelpLink(path);

            Assert.NotNull(link);
            Assert.StartsWith(TrakHoundWebsite.BaseUrl + "/", link);
        }

        [Theory]
        [InlineData("_admin", "instances")]
        [InlineData("_admin/explorer/abc", "instances")]
        [InlineData("_admin/explorer/abc/functions/f1/runs/r1", "functions")]
        [InlineData("_admin/explorer/abc/diagnostics/mcp", "mcp-server")]
        [InlineData("_admin/explorer/abc/diagnostics/logs/view", "instances#logs")]
        [InlineData("_admin/configuration/zones/z1", "zones")]
        [InlineData("_admin/workspaces/w1/configuration/variables", "volumes-and-variables#variables")]
        [InlineData("_admin/configuration/api", "apis")]
        [InlineData("_admin/packages/sources?x=1", "self-hosting-packages")]
        [InlineData("_admin/security/trusted-publishers", "packages#verification")]
        [InlineData("/_admin/deployments/backups", "configuration#deployments")]
        public void AdminPage_LinksToItsTopic(string path, string topic)
        {
            Assert.Equal($"{TrakHoundWebsite.FrameworkDocs}/{topic}", TrakHoundWebsite.GetAdminHelpLink(path));
        }

        [Fact]
        public void LicensesPage_LinksToLicensingGuide()
        {
            Assert.Equal(TrakHoundWebsite.LicensingGuide, TrakHoundWebsite.GetAdminHelpLink("_admin/licenses/assignments"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("dashboard")]
        [InlineData("_admin/login")]
        public void OtherPages_HaveNoHelpLink(string path)
        {
            Assert.Null(TrakHoundWebsite.GetAdminHelpLink(path));
        }

        [Fact]
        public void HelpLinks_UseKnownTopics()
        {
            // The website's page names are listed once (Topics.All), so a renamed page is fixed in one place
            foreach (var route in AdminRoutes.Select(o => (string)o[0]))
            {
                var link = TrakHoundWebsite.GetAdminHelpLink(route);
                if (link == TrakHoundWebsite.LicensingGuide) continue;

                var topic = link.Substring(TrakHoundWebsite.FrameworkDocs.Length + 1).Split('#')[0];
                Assert.Contains(topic, TrakHoundWebsite.Topics.All);
            }
        }
    }
}
