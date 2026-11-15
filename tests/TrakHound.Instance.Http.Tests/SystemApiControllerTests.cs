// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using System.Reflection;
using TrakHound.Instance.Security;

namespace TrakHound.Instance.Http.Tests
{
    /// <summary>
    /// Every System API Controller says how it is authorized (a System Resource, or per Service operation), and changes are never made by a GET
    /// (which a browser sends cross-site with the Session Cookie)
    /// </summary>
    public class SystemApiControllerTests
    {
        // Controllers that are not part of the System API (Sign In, Sign Out, the Admin Interface Session, and Downloads, which are only
        // available at a random, temporary key)
        private static readonly string[] _nonSystemControllers = new[]
        {
            "AdminSessionController",
            "DownloadController",
            "IdentityCallbackController",
            "IdentityRevokationController"
        };


        private static IEnumerable<Type> GetControllers()
        {
            return typeof(TrakHoundAuthenticationMiddleware).Assembly.GetTypes()
                .Where(o => typeof(ControllerBase).IsAssignableFrom(o) && !o.IsAbstract && o.GetCustomAttribute<NonControllerAttribute>() == null);
        }

        private static IEnumerable<MethodInfo> GetActions(Type controller)
        {
            return controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(o => !o.IsSpecialName && o.DeclaringType != typeof(ControllerBase) && o.DeclaringType != typeof(object) && o.GetCustomAttribute<NonActionAttribute>() == null)
                .Where(o => o.DeclaringType.Assembly == controller.Assembly);
        }


        public static IEnumerable<object[]> SystemControllers => GetControllers().Where(o => !_nonSystemControllers.Contains(o.Name)).Select(o => new object[] { o.Name });

        [Fact]
        public void SystemControllers_AreFound()
        {
            Assert.NotEmpty(SystemControllers);
            Assert.All(_nonSystemControllers, name => Assert.Contains(GetControllers(), o => o.Name == name));
        }

        [Theory]
        [MemberData(nameof(SystemControllers))]
        public void SystemController_DeclaresHowItIsAuthorized(string controllerName)
        {
            var controller = GetControllers().First(o => o.Name == controllerName);
            if (controller.GetCustomAttribute<TrakHoundSystemResourceAttribute>() != null) return;
            if (controller.GetCustomAttribute<TrakHoundServiceOperationsAttribute>() != null) return;

            var unclassified = GetActions(controller).Where(o => o.GetCustomAttribute<TrakHoundSystemResourceAttribute>() == null).Select(o => o.Name).ToList();
            Assert.True(unclassified.Count == 0, $"{controllerName} Actions without a System Resource : {string.Join(", ", unclassified)}");
        }

        public static IEnumerable<object[]> AllControllers => GetControllers().Select(o => new object[] { o.Name });

        [Theory]
        [MemberData(nameof(AllControllers))]
        public void Controller_ActionsHaveAttributeRoutes(string controllerName)
        {
            // Conventional routes (MapControllerRoute) are matched after the App Host's catch-all page ("/{**path}"), so they
            // never reach their Controller (every Request would be a 404)
            var controller = GetControllers().First(o => o.Name == controllerName);
            var controllerRoute = controller.GetCustomAttributes().OfType<IRouteTemplateProvider>().Any(o => o.Template != null);

            var unrouted = GetActions(controller)
                .Where(o => !controllerRoute && !o.GetCustomAttributes().OfType<IRouteTemplateProvider>().Any(a => a.Template != null))
                .Select(o => o.Name)
                .ToList();

            Assert.True(unrouted.Count == 0, $"{controllerName} Actions without an attribute route : {string.Join(", ", unrouted)}");
        }

        [Theory]
        [MemberData(nameof(SystemControllers))]
        public void SystemController_WriteActionsAreNotGet(string controllerName)
        {
            var controller = GetControllers().First(o => o.Name == controllerName);
            foreach (var action in GetActions(controller).Where(o => o.GetCustomAttribute<TrakHoundSystemWriteAttribute>() != null))
            {
                var methods = action.GetCustomAttributes().OfType<IActionHttpMethodProvider>().SelectMany(o => o.HttpMethods).ToList();
                Assert.True(methods.Count > 0, $"{controllerName}.{action.Name} changes the Instance but accepts any HTTP Method (including GET)");
                Assert.DoesNotContain("GET", methods, StringComparer.OrdinalIgnoreCase);
            }
        }


        private static HttpContext CreateContext(string path, params object[] metadata)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), "test"));
            return context;
        }

        [Fact]
        public void ControllerWithoutResource_IsUnclassified()
        {
            Assert.True(TrakHoundAuthenticationMiddleware.IsUnclassifiedSystemEndpoint(CreateContext("/_logs/loggers", new ControllerActionDescriptor())));
        }

        [Fact]
        public void ControllerWithResource_IsClassified()
        {
            Assert.False(TrakHoundAuthenticationMiddleware.IsUnclassifiedSystemEndpoint(CreateContext("/_logs/loggers", new ControllerActionDescriptor(), new TrakHoundSystemResourceAttribute(TrakHoundSystemSecurity.Logs))));
            Assert.False(TrakHoundAuthenticationMiddleware.IsUnclassifiedSystemEndpoint(CreateContext("/_services/keyvalues", new ControllerActionDescriptor(), new TrakHoundServiceOperationsAttribute())));
        }

        [Fact]
        public void EndpointOutsideSystemApi_IsNotChecked()
        {
            Assert.False(TrakHoundAuthenticationMiddleware.IsUnclassifiedSystemEndpoint(CreateContext("/_download/abc", new ControllerActionDescriptor())));
        }

        [Fact]
        public void EndpointThatIsNotAController_IsNotChecked()
        {
            // ex. the MCP Server, which declares its own System Resource
            Assert.False(TrakHoundAuthenticationMiddleware.IsUnclassifiedSystemEndpoint(CreateContext("/_mcp")));
        }
    }
}
