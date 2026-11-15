// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components.Endpoints;
using TrakHound.Instances;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// The separate Admin Port (when configured). The Admin Interface and the System API are only served on the Admin Port, and Apps,
    /// Apis, and App Sign In are only served on the HTTP Port. Apps then run in a different browser origin than the Admin Interface,
    /// and the Admin Port can be limited to a management address (ex. 127.0.0.1) or blocked by a firewall.
    /// Health endpoints and the files needed to load pages are served on both.
    /// </summary>
    public static class TrakHoundAdminPort
    {
        private static readonly System.Reflection.Assembly _adminAssembly = typeof(InstanceApp).Assembly;


        /// <summary>
        /// The Admin Port the Instance is listening on (0 when the Admin Interface is served on the HTTP Port).
        /// Set once at startup (changing the setting requires a restart)
        /// </summary>
        public static int Port { get; private set; }

        /// <summary>
        /// The HTTP Port the Instance is listening on (for Apps and Apis)
        /// </summary>
        public static int HttpPort { get; private set; }

        public static bool IsEnabled => Port > 0;


        /// <summary>
        /// Whether a separate Admin Port is configured (it is only used when it is different from the HTTP Port)
        /// </summary>
        public static bool IsConfigured(TrakHoundInstanceConfiguration configuration)
        {
            return configuration != null && configuration.AdminHttpPort > 0 && configuration.AdminHttpPort != configuration.HttpPort;
        }

        internal static void Set(int adminPort, int httpPort)
        {
            Port = adminPort;
            HttpPort = httpPort;
        }


        /// <summary>
        /// Whether the Request was received on the Admin Port. Always true when there is no separate Admin Port
        /// </summary>
        public static bool IsAdminConnection(HttpContext context) => IsAdminConnection(context?.Connection.LocalPort);

        /// <summary>
        /// Whether a connection's local port is the Admin Port. Always true when there is no separate Admin Port
        /// </summary>
        public static bool IsAdminConnection(int? localPort) => !IsEnabled || localPort == Port;

        /// <summary>
        /// Whether a connection's local port is the HTTP Port (Apps and Apis). Always true when there is no separate Admin Port
        /// </summary>
        public static bool IsHttpConnection(int? localPort) => !IsEnabled || localPort != Port;

        /// <summary>
        /// Whether the endpoint is one of the Admin Interface's Pages (ex. /_admin/explorer, /_admin/configuration/settings). The Error Page (/_error) is
        /// not an Admin Page : it is also used for unhandled errors, so it is available when the Admin Interface is disabled. Neither is
        /// the App Host (the catch-all for App pages)
        /// </summary>
        public static bool IsAdminPage(Endpoint endpoint)
        {
            var type = endpoint?.Metadata.GetMetadata<ComponentTypeMetadata>()?.Type;
            return type != null && type.Assembly == _adminAssembly && type != typeof(Pages.InstanceErrorPage) && type != typeof(Pages.AppHost);
        }

        /// <summary>
        /// The Base URI for Apps and Apis when the current page is on the Admin Port (the same host on the HTTP Port).
        /// Returns the Base URI unchanged when there is no separate Admin Port
        /// </summary>
        public static string GetHttpBaseUri(string baseUri)
        {
            if (!IsEnabled || string.IsNullOrEmpty(baseUri)) return baseUri;

            if (Uri.TryCreate(baseUri, UriKind.Absolute, out var uri) && uri.Port == Port)
            {
                var builder = new UriBuilder(uri) { Port = HttpPort };
                return builder.Uri.ToString();
            }

            return baseUri;
        }

        /// <summary>
        /// A link from the Admin Interface to an App, Api, or Sign In route (relative to the Base Path). The link is relative when there
        /// is no separate Admin Port, and an absolute URL on the HTTP Port when there is
        /// </summary>
        public static string GetHttpLink(string baseUri, string path)
        {
            if (!IsEnabled) return path;
            return Url.Combine(GetHttpBaseUri(baseUri), path ?? "");
        }

        /// <summary>
        /// The origin of the Admin Interface for a Request on the HTTP Port (so that Apps can be shown in the Admin Interface's App Preview)
        /// </summary>
        public static string GetAdminOrigin(HttpContext context)
        {
            if (!IsEnabled || context == null) return null;
            return $"{context.Request.Scheme}://{context.Request.Host.Host}:{Port}";
        }
    }
}
