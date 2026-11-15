// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using System.Reflection;
using TrakHound.Instance.Security;

namespace TrakHound.Instance.Http.Tests
{
    /// <summary>
    /// Every Admin Interface Page says which Admin Resource it belongs to, so a Page is never open to an Admin whose Roles don't allow it
    /// </summary>
    public class AdminPageAccessTests
    {
        // Not Admin Interface Pages (see TrakHoundAdminPort.IsAdminPage) : the Error Page and the catch-all for App pages
        private static readonly Type[] _otherPages = new[] { typeof(Pages.InstanceErrorPage), typeof(Pages.AppHost) };


        private static IEnumerable<Type> GetPages()
        {
            return typeof(InstanceApp).Assembly.GetTypes()
                .Where(o => typeof(IComponent).IsAssignableFrom(o) && o.GetCustomAttributes<RouteAttribute>().Any() && !_otherPages.Contains(o));
        }

        private static IReadOnlyDictionary<string, object> Values(params (string Key, object Value)[] values) => values.ToDictionary(o => o.Key, o => o.Value);

        public static IEnumerable<object[]> PageNames => GetPages().Select(o => new object[] { o.FullName });


        [Fact]
        public void Pages_AreFound()
        {
            Assert.True(GetPages().Count() > 50);
        }

        [Theory]
        [MemberData(nameof(PageNames))]
        public void Page_DeclaresItsAdminResource(string pageName)
        {
            var page = GetPages().First(o => o.FullName == pageName);

            var resource = page.GetCustomAttribute<AdminResourceAttribute>();
            var declared = (resource != null ? 1 : 0) + (page.GetCustomAttribute<AdminResourceDynamicAttribute>() != null ? 1 : 0) + (page.GetCustomAttribute<AdminResourcePublicAttribute>() != null ? 1 : 0);
            Assert.True(declared == 1, $"{pageName} must have exactly one of the AdminResource, AdminResourceDynamic or AdminResourcePublic attributes");

            if (resource != null)
            {
                Assert.NotEmpty(resource.ResourceIds);
                Assert.All(resource.ResourceIds, o => Assert.NotNull(TrakHoundAdminSecurity.GetResource(o)));
            }
        }

        [Theory]
        [MemberData(nameof(PageNames))]
        public void DynamicPage_GetsItsResourceFromItsRoutes(string pageName)
        {
            // A Page that checks its own Resource needs a Route value that names it (the Configuration Category), in every Route
            var page = GetPages().First(o => o.FullName == pageName);
            if (page.GetCustomAttribute<AdminResourceDynamicAttribute>() == null) return;

            Assert.All(page.GetCustomAttributes<RouteAttribute>(), o => Assert.Contains("{CategoryKey}", o.Template));
        }

        [Fact]
        public void OnlyTheLoginAndAccountPages_AreOpenToEveryAdmin()
        {
            var open = GetPages().Where(o => o.GetCustomAttribute<AdminResourcePublicAttribute>() != null).Select(o => o.FullName).OrderBy(o => o);

            Assert.Equal(new[] { typeof(Pages.Login.Index).FullName, typeof(Pages.Security.Account).FullName }.OrderBy(o => o), open);
        }

        [Fact]
        public void PageResources_ComeFromTheAttribute()
        {
            Assert.Equal(new[] { TrakHoundAdminSecurity.Packages }, AdminAccess.GetPageResources(typeof(Pages.Packages.Index), Values()));
            Assert.Equal(new[] { TrakHoundAdminSecurity.Admins }, AdminAccess.GetPageResources(typeof(Pages.Security.Admins), null));
            Assert.Null(AdminAccess.GetPageResources(typeof(Pages.Login.Index), null));
        }

        [Theory]
        [InlineData("services", TrakHoundAdminSecurity.ServiceConfigurations)]
        [InlineData("apps", TrakHoundAdminSecurity.AppConfigurations)]
        [InlineData("identity", TrakHoundAdminSecurity.IdentityProviders)]
        public void ConfigurationPages_NeedTheirCategorysResource(string categoryKey, string resourceId)
        {
            Assert.Equal(new[] { resourceId }, AdminAccess.GetPageResources(typeof(Pages.Configuration.List), Values(("CategoryKey", categoryKey))));
            Assert.Equal(new[] { resourceId }, AdminAccess.GetPageResources(typeof(Pages.Configuration.Edit), Values(("CategoryKey", categoryKey), ("ConfigurationId", "a"))));
        }

        [Fact]
        public void AnUnknownConfigurationCategory_IsOnlyForOwners()
        {
            Assert.Equal(new[] { TrakHoundAdminSecurity.Admins }, AdminAccess.GetPageResources(typeof(Pages.Configuration.List), Values(("CategoryKey", "unknown"))));
        }

        [Fact]
        public void WorkspacePages_ArePartOfDeployments()
        {
            // Whichever page it is (ex. a Workspace's Service Configurations), since it edits a Deployment and not the running Instance
            Assert.Equal(new[] { TrakHoundAdminSecurity.Deployments }, AdminAccess.GetPageResources(typeof(Pages.Configuration.List), Values(("WorkspaceId", "site-b"), ("CategoryKey", "services"))));
            Assert.Equal(new[] { TrakHoundAdminSecurity.Deployments }, AdminAccess.GetPageResources(typeof(Pages.Security.Zones), Values(("WorkspaceId", "site-b"))));
        }
    }
}
