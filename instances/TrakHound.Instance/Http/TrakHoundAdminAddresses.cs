// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Net;
using TrakHound.Instances;
using IPNetwork = System.Net.IPNetwork;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// The client addresses the Admin Interface is served to (see TrakHoundInstanceConfiguration.AdminAllowedAddresses). An empty list
    /// allows every address. Loopback is always allowed, so an Admin on the Instance's own computer can't be locked out
    /// </summary>
    public static class TrakHoundAdminAddresses
    {
        private static volatile AllowedState _state; // Null until first used

        // The entries and what was parsed from them are replaced together, so a Request can read them without a lock
        private sealed class AllowedState
        {
            public readonly IEnumerable<string> Entries;
            public readonly IPAddress[] Addresses;
            public readonly IPNetwork[] Networks;

            public AllowedState(IEnumerable<string> entries)
            {
                Entries = entries;

                var addresses = new List<IPAddress>();
                var networks = new List<IPNetwork>();
                if (entries != null)
                {
                    foreach (var entry in entries)
                    {
                        if (TrakHoundTrustedProxies.TryParse(entry, out var address, out var network))
                        {
                            if (address != null) addresses.Add(Normalize(address));
                            else networks.Add(network);
                        }
                    }
                }

                Addresses = addresses.ToArray();
                Networks = networks.ToArray();
            }

            // A list with only invalid entries still restricts (to loopback), rather than silently allowing everyone
            public bool IsRestricted => Entries != null && Entries.Any(o => !string.IsNullOrWhiteSpace(o));
        }


        /// <summary>
        /// Whether the Admin Interface is served to the Request's client address
        /// </summary>
        public static bool IsAllowed(HttpContext context, TrakHoundInstanceConfiguration configuration)
        {
            return IsAllowed(context?.Connection.RemoteIpAddress, configuration?.AdminAllowedAddresses);
        }

        /// <summary>
        /// Whether the Admin Interface is served to a client address (the Request's address, after the Trusted Proxies are applied)
        /// </summary>
        public static bool IsAllowed(IPAddress address, IEnumerable<string> entries) => Check(address, GetState(entries));

        /// <summary>
        /// Whether entries that aren't saved yet (ex. on the Settings page) would allow a client address. Not cached
        /// </summary>
        public static bool WouldAllow(IPAddress address, IEnumerable<string> entries) => Check(address, new AllowedState(entries));

        private static bool Check(IPAddress address, AllowedState state)
        {
            if (!state.IsRestricted) return true;

            if (address == null) return false;
            address = Normalize(address);
            if (IPAddress.IsLoopback(address)) return true;

            foreach (var allowed in state.Addresses)
            {
                if (allowed.Equals(address)) return true;
            }

            foreach (var network in state.Networks)
            {
                if (network.BaseAddress.AddressFamily == address.AddressFamily && network.Contains(address)) return true;
            }

            return false;
        }

        // IPv4 clients on a dual-mode socket appear as ::ffff:10.0.0.5, which wouldn't match an IPv4 entry
        private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        private static AllowedState GetState(IEnumerable<string> entries)
        {
            // Saving the Settings (or reloading the Configuration) replaces the list
            var state = _state;
            if (state != null && ReferenceEquals(entries, state.Entries)) return state;

            state = new AllowedState(entries);
            _state = state;
            return state;
        }
    }
}
