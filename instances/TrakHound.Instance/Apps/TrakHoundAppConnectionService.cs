// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR;
using TrakHound.Apps;
using TrakHound.Instances;

namespace TrakHound.Instance.Apps
{
    public class TrakHoundAppConnectionService : IDisposable
    {
        private readonly string _id = Guid.NewGuid().ToString();
        private readonly TrakHoundAppConnectionManager _appConnectionManager;
        private readonly ITrakHoundInstance _instance;

        public string Id => _id;

        public TrakHoundAppConnection Connection { get; set; }

        /// <summary>
        /// Whether this is the Prerender of a Page (the HTTP Request before the Blazor Circuit connects, which uses its own Connection Service)
        /// </summary>
        public bool IsPrerendering { get; }

        /// <summary>
        /// The local Port the Client connected to (used to separate the Admin Port from the HTTP Port)
        /// </summary>
        public int? LocalPort { get; }

        /// <summary>
        /// The Client's address (after the Trusted Proxies are applied). Used to limit the Admin Interface to its Allowed Addresses
        /// </summary>
        public IPAddress RemoteAddress { get; }


        public TrakHoundAppConnectionService(TrakHoundAppConnectionManager appConnectionManager, IHttpContextAccessor httpContextAccessor, ITrakHoundInstance instance)
        {
            _appConnectionManager = appConnectionManager;
            _instance = instance;

            // The Circuit's HttpContext is the Blazor Hub's Request. IHttpContextAccessor only has it when the Circuit uses WebSockets,
            // so the Hub Filter's is used first (a Circuit created without either isn't recorded)
            var hubHttpContext = TrakHoundAppHubFilter.HttpContext;
            var httpContext = hubHttpContext ?? httpContextAccessor.HttpContext;
            if (httpContext != null)
            {
                IsPrerendering = hubHttpContext == null && httpContext.GetEndpoint()?.Metadata.GetMetadata<HubMetadata>() == null;

                LocalPort = httpContext.Connection.LocalPort;
                RemoteAddress = httpContext.Connection.RemoteIpAddress;

                var connectionId = httpContext.Connection.Id;
                var clientAddress = GetAddress(httpContext.Connection.RemoteIpAddress);
                var agent = httpContext.Request.Headers.UserAgent.FirstOrDefault();

                var appConnection = new TrakHoundAppConnection();
                appConnection.ConnectionId = connectionId;
                appConnection.Code = CreateCode(_id);
                appConnection.ClientAddress = clientAddress;
                appConnection.ClientAgent = agent;
                appConnection.Created = DateTime.UtcNow;
                Connection = appConnection;

                appConnectionManager.AddConnection(_id, appConnection);

                // Look up the Hostname in the background (a Reverse DNS lookup can take seconds when there is no entry, which would delay the Page)
                if (!string.IsNullOrEmpty(clientAddress))
                {
                    _ = Task.Run(async () =>
                    {
                        var hostname = await GetHostNameAsync(clientAddress);
                        if (!string.IsNullOrEmpty(hostname)) _appConnectionManager.SetHostname(_id, hostname);
                    });
                }
            }
        }

        public void Dispose()
        {
            _appConnectionManager.RemoveConnection(_id);

            // End the Connection's App Metrics (its open Page View)
            (_instance?.AppProvider as TrakHound.Blazor.Apps.TrakHoundAppProvider)?.Metrics.ConnectionClosed(_id);
        }

        // Hostnames by IP Address (including addresses with no entry), so each page view (Prerender + Circuit) doesn't run its own Reverse DNS lookup.
        // Lookups in progress are shared, and the cache is capped since every Client Address adds an entry
        private static readonly TimeSpan _hostnameTtl = TimeSpan.FromMinutes(10);
        private const int MaxHostnames = 1000;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, HostnameEntry> _hostnames = new System.Collections.Concurrent.ConcurrentDictionary<string, HostnameEntry>(); // IP Address => Hostname Lookup

        private sealed class HostnameEntry
        {
            public Lazy<Task<string>> Lookup { get; init; } // Lazy so that only the entry that is kept starts a lookup

            public DateTime Expires { get; init; }
        }

        public static Task<string> GetHostNameAsync(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress)) return Task.FromResult<string>(null);

            var now = DateTime.UtcNow;
            if (_hostnames.TryGetValue(ipAddress, out var entry) && entry.Expires > now) return entry.Lookup.Value;

            if (_hostnames.Count >= MaxHostnames)
            {
                // Remove expired entries first, and everything if that isn't enough (a simple bound rather than LRU)
                foreach (var existing in _hostnames)
                {
                    if (existing.Value.Expires <= now) _hostnames.TryRemove(existing);
                }
                if (_hostnames.Count >= MaxHostnames) _hostnames.Clear();
            }

            var newEntry = new HostnameEntry { Lookup = new Lazy<Task<string>>(() => LookupHostNameAsync(ipAddress)), Expires = now + _hostnameTtl };

            // Another connection may have started a lookup for the same address at the same time, so use whichever was added
            entry = _hostnames.AddOrUpdate(ipAddress, newEntry, (key, current) => current.Expires > now ? current : newEntry);
            return entry.Lookup.Value;
        }

        private static async Task<string> LookupHostNameAsync(string ipAddress)
        {
            try
            {
                var entry = await Dns.GetHostEntryAsync(ipAddress);
                if (entry != null)
                {
                    return entry.HostName;
                }
            }
            catch (SocketException)
            {
                // Not every address has a Reverse DNS entry
            }
            catch (ArgumentException) { }

            return null;
        }

        // Letters and digits that can't be confused with each other on a screen (no 0/O, 1/I/L, 2/Z, 5/S, 8/B)
        private const string CodeCharacters = "34679ACDEFGHJKMNPQRTUVWXY";

        /// <summary>
        /// A short code for the connection (4 characters from the Connection ID, so it stays the same for the life of the connection)
        /// </summary>
        private static string CreateCode(string id)
        {
            var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id ?? Guid.NewGuid().ToString()));
            var code = new char[4];
            for (var i = 0; i < code.Length; i++) code[i] = CodeCharacters[hash[i] % CodeCharacters.Length];
            return new string(code);
        }

        /// <summary>
        /// The Client's Address (IPv4 when an IPv4 address is mapped to IPv6). The Remote IP Address doesn't include the Port
        /// </summary>
        private static string GetAddress(IPAddress address)
        {
            if (address == null) return null;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return address.ToString();
        }
    }
}
