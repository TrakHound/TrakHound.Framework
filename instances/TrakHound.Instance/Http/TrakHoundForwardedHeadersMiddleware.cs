// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using System.Net;
using TrakHound.Logging;
using IPNetwork = System.Net.IPNetwork;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// Reads the Trusted Proxies (see TrakHoundInstanceConfiguration.TrustedProxies)
    /// </summary>
    public static class TrakHoundTrustedProxies
    {
        /// <summary>
        /// Parses an IP Address (ex. 127.0.0.1) or Network (ex. 10.0.0.0/8)
        /// </summary>
        public static bool TryParse(string entry, out IPAddress address, out IPNetwork network)
        {
            address = null;
            network = default;

            var value = entry?.Trim();
            if (string.IsNullOrEmpty(value)) return false;

            if (value.Contains('/')) return IPNetwork.TryParse(value, out network);
            return IPAddress.TryParse(value, out address);
        }

        /// <summary>
        /// The entries that are not an IP Address or Network
        /// </summary>
        public static IEnumerable<string> GetInvalid(IEnumerable<string> entries)
        {
            return entries?.Where(o => !string.IsNullOrWhiteSpace(o) && !TryParse(o, out _, out _)).ToList() ?? new List<string>();
        }

        /// <summary>
        /// Creates the Forwarded Headers Options that trust only the entries (null when there are none, so the headers are ignored)
        /// </summary>
        public static ForwardedHeadersOptions CreateOptions(IEnumerable<string> entries)
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,

                // Read X-Forwarded-For from the right until an address that isn't a Trusted Proxy, so chained proxies work and
                // an address a client adds to the header itself is never used
                ForwardLimit = null
            };

            // The defaults trust loopback, which would let any client spoof its address through a proxy on the same machine
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (TryParse(entry, out var address, out var network))
                    {
                        if (address != null) options.KnownProxies.Add(address);
                        else options.KnownIPNetworks.Add(network);
                    }
                }
            }

            return options.KnownProxies.Count > 0 || options.KnownIPNetworks.Count > 0 ? options : null;
        }
    }


    /// <summary>
    /// Sets the client's address and scheme from the X-Forwarded-For and X-Forwarded-Proto headers when the Request comes from a Trusted Proxy.
    /// The Configuration is read on every Request, so saved changes apply immediately
    /// </summary>
    public class TrakHoundForwardedHeadersMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Func<IEnumerable<string>> _getTrustedProxies;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ITrakHoundLogger _logger = new TrakHoundLogger<TrakHoundForwardedHeadersMiddleware>();
        private readonly object _lock = new object();
        private volatile MiddlewareState _state; // Null until the first Request

        // The entries and the middleware created from them are replaced together, so a Request can read them without the lock
        private sealed class MiddlewareState
        {
            public readonly IEnumerable<string> Entries;
            public readonly ForwardedHeadersMiddleware Middleware;

            public MiddlewareState(IEnumerable<string> entries, ForwardedHeadersMiddleware middleware)
            {
                Entries = entries;
                Middleware = middleware;
            }
        }


        public TrakHoundForwardedHeadersMiddleware(RequestDelegate next, Func<IEnumerable<string>> getTrustedProxies, ILoggerFactory loggerFactory)
        {
            _next = next;
            _getTrustedProxies = getTrustedProxies;
            _loggerFactory = loggerFactory;
        }


        public Task Invoke(HttpContext context)
        {
            var middleware = GetMiddleware();
            return middleware != null ? middleware.Invoke(context) : _next(context);
        }

        private ForwardedHeadersMiddleware GetMiddleware()
        {
            var entries = _getTrustedProxies?.Invoke();

            // Saving the Settings (or reloading the Configuration) replaces the list
            var state = _state;
            if (state != null && ReferenceEquals(entries, state.Entries)) return state.Middleware;

            lock (_lock)
            {
                state = _state;
                if (state == null || !ReferenceEquals(entries, state.Entries))
                {
                    foreach (var invalid in TrakHoundTrustedProxies.GetInvalid(entries))
                    {
                        _logger.LogWarning($"Trusted Proxy Ignored : '{invalid}' is not an IP Address or Network");
                    }

                    var options = TrakHoundTrustedProxies.CreateOptions(entries);
                    var middleware = options != null ? new ForwardedHeadersMiddleware(_next, _loggerFactory, Options.Create(options)) : null;

                    state = new MiddlewareState(entries, middleware);
                    _state = state;
                }

                return state.Middleware;
            }
        }
    }
}
