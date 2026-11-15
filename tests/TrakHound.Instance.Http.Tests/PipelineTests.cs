// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using TrakHound.Instances;

namespace TrakHound.Instance.Http.Tests
{
    /// <summary>
    /// The request pipeline's own middleware (see TrakHoundInstanceHost.UseInstancePipeline)
    /// </summary>
    public class PipelineTests
    {
        private static readonly string _root = TrakHoundPackageFilesMiddleware.GetRoot(Path.Combine(Path.GetTempPath(), "trakhound-pipeline-tests"));


        [Theory]
        [InlineData("/_packages/TrakHound.Example.App/1.0.0/app.css")]
        [InlineData("/_packages/TrakHound.Example.App/1.0.0/css/site.css")]
        [InlineData("/_packages/TrakHound.Example.App/1.0.0/../1.0.1/app.css")] // Still within the folder
        [InlineData("/_packages/../appsettings.json")] // The folder's own file, not the Instance's
        public void PackageFile_WithinTheFolder_IsServed(string path)
        {
            var filePath = TrakHoundPackageFilesMiddleware.GetFilePath(_root, new PathString(path));

            Assert.NotNull(filePath);
            Assert.StartsWith(_root, filePath);
        }

        [Theory]
        [InlineData("/_packages/../../appsettings.json")]
        [InlineData("/_packages/../../../appsettings.json")]
        [InlineData("/_packages/../../../config/default/security/admins/a.yaml")]
        [InlineData("/_packages/x/../../../../config/.secretkey")]
        [InlineData("/")]
        [InlineData("")]
        public void PackageFile_OutsideTheFolder_IsRejected(string path)
        {
            Assert.Null(TrakHoundPackageFilesMiddleware.GetFilePath(_root, new PathString(path)));
        }

        [Fact]
        public void PackageFile_WithBackslashes_IsRejected()
        {
            // On Windows "\" is a separator, so these would leave the folder if the server didn't normalize them
            if (!OperatingSystem.IsWindows()) return;

            Assert.Null(TrakHoundPackageFilesMiddleware.GetFilePath(_root, new PathString("/_packages/..\\..\\..\\appsettings.json")));
            Assert.Null(TrakHoundPackageFilesMiddleware.GetFilePath(_root, new PathString("/_packages/x\\..\\..\\..\\..\\config\\.secretkey")));
        }

        [Fact]
        public void PackageFile_OnAnotherDrive_IsRejected()
        {
            if (!OperatingSystem.IsWindows()) return;

            Assert.Null(TrakHoundPackageFilesMiddleware.GetFilePath(_root, new PathString("/C:/Windows/win.ini")));
        }


        [Theory]
        [InlineData(256, 256L * 1024 * 1024)]
        [InlineData(1, 1024L * 1024)]
        [InlineData(0, null)]
        public void RequestBodyLimit_IsFromTheConfiguration(int megabytes, long? expected)
        {
            var configuration = new TrakHoundInstanceConfiguration { MaxRequestBodySizeMb = megabytes };

            Assert.Equal(expected, TrakHoundRequestBodyLimitMiddleware.GetLimit(configuration));
        }

        [Fact]
        public void RequestBodyLimit_DefaultsTo256Megabytes()
        {
            Assert.Equal(TrakHoundInstanceConfiguration.DefaultMaxRequestBodySizeMb, new TrakHoundInstanceConfiguration().MaxRequestBodySizeMb);
            Assert.Equal(256, TrakHoundInstanceConfiguration.DefaultMaxRequestBodySizeMb);
        }

        private class BodySizeFeature : IHttpMaxRequestBodySizeFeature
        {
            public bool IsReadOnly { get; set; }
            public long? MaxRequestBodySize { get; set; }
        }

        private class TestInstance : System.Reflection.DispatchProxy
        {
            public TrakHoundInstanceConfiguration Configuration { get; set; }

            protected override object Invoke(System.Reflection.MethodInfo targetMethod, object[] args)
            {
                return targetMethod.Name == "get_Configuration" ? Configuration : null;
            }

            public static ITrakHoundInstance Create(TrakHoundInstanceConfiguration configuration)
            {
                var instance = System.Reflection.DispatchProxy.Create<ITrakHoundInstance, TestInstance>();
                ((TestInstance)(object)instance).Configuration = configuration;
                return instance;
            }
        }

        [Theory]
        [InlineData(10, 10L * 1024 * 1024)]
        [InlineData(0, null)]
        public async Task RequestBodyLimit_IsAppliedToTheRequest(int megabytes, long? expected)
        {
            var instance = TestInstance.Create(new TrakHoundInstanceConfiguration { MaxRequestBodySizeMb = megabytes });
            var middleware = new TrakHoundRequestBodyLimitMiddleware(_ => Task.CompletedTask, instance);

            var feature = new BodySizeFeature { MaxRequestBodySize = 30_000_000 };
            var context = new DefaultHttpContext();
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

            await middleware.Invoke(context);

            Assert.Equal(expected, feature.MaxRequestBodySize);
        }

        [Fact]
        public async Task RequestBodyLimit_ReadOnlyFeature_IsNotChanged()
        {
            // ex. the body was already read (the limit can't change after that)
            var instance = TestInstance.Create(new TrakHoundInstanceConfiguration { MaxRequestBodySizeMb = 10 });
            var middleware = new TrakHoundRequestBodyLimitMiddleware(_ => Task.CompletedTask, instance);

            var feature = new BodySizeFeature { IsReadOnly = true, MaxRequestBodySize = 5 };
            var context = new DefaultHttpContext();
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

            await middleware.Invoke(context);

            Assert.Equal(5, feature.MaxRequestBodySize);
        }


        [Fact]
        public async Task ResponseHeaders_AreAdded()
        {
            var middleware = new TrakHoundResponseHeadersMiddleware(_ => Task.CompletedTask);
            var context = new DefaultHttpContext();
            context.Request.Path = "/orders";

            await middleware.Invoke(context);

            Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
            Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"]);
            Assert.Equal("frame-ancestors 'self'", context.Response.Headers.ContentSecurityPolicy);
        }

        [Fact]
        public async Task ResponseHeaders_StaticFiles_HaveNoFramePolicy()
        {
            var middleware = new TrakHoundResponseHeadersMiddleware(_ => Task.CompletedTask);
            var context = new DefaultHttpContext();
            context.Request.Path = "/_framework/blazor.web.js";

            await middleware.Invoke(context);

            Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
            Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
        }


        [Theory]
        [InlineData("/_blazor", true)]
        [InlineData("/_Blazor/negotiate", true)]
        [InlineData("/_blazorx", false)]
        public void IsBlazorRoute(string path, bool expected)
        {
            Assert.Equal(expected, TrakHoundInstanceRoutes.IsBlazorRoute(new PathString(path)));
        }

        [Theory]
        [InlineData("/_workers/list", true)]
        [InlineData("/_WORKERS", true)]
        [InlineData("/_workersx", false)]
        public void IsWorkersRoute(string path, bool expected)
        {
            Assert.Equal(expected, TrakHoundInstanceRoutes.IsWorkersRoute(new PathString(path)));
        }

        [Theory]
        [InlineData("/favicon.ico", true)]
        [InlineData("/FAVICON.ICO", true)]
        [InlineData("/favicon.ico.bak", false)]
        public void IsFaviconRoute(string path, bool expected)
        {
            Assert.Equal(expected, TrakHoundInstanceRoutes.IsFaviconRoute(new PathString(path)));
        }

        [Fact]
        public void Favicon_IsAValidRoute()
        {
            // Otherwise TrakHoundRouteValidationMiddleware returns 404 before the static file is served
            var instance = TestInstance.Create(new TrakHoundInstanceConfiguration());
            var context = new DefaultHttpContext();
            context.Request.Path = "/favicon.ico";

            Assert.True(TrakHoundInstanceRequests.IsValidRoute(instance, context));
        }
    }
}
