// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;

namespace TrakHound.Security
{
    /// <summary>
    /// Checks whether an Identity can access a Resource
    /// </summary>
    public interface ITrakHoundAuthorizer
    {
        /// <summary>
        /// Checks whether the Identity (null for an anonymous Request) can access the Resource, optionally requiring a specific Permission
        /// </summary>
        TrakHoundAuthorizationResult Authorize(TrakHoundIdentity identity, string resourceId, string permission = null);
    }

    /// <summary>
    /// The Identity of the Request currently being processed. Flows with the async call chain, so that code called while handling a
    /// Request (ex. Service calls from an API Controller) can be authorized for the Identity that made the Request.
    /// </summary>
    /// <remarks>
    /// When there is no Current context the code is not running on behalf of a Request (ex. a Worker or Function) and is trusted.
    /// </remarks>
    public sealed class TrakHoundSecurityContext
    {
        private static readonly AsyncLocal<TrakHoundSecurityContext> _current = new AsyncLocal<TrakHoundSecurityContext>();

        // A context usually lasts one Request, but one can flow into longer work (ex. a stream or a Blazor circuit started by the Request),
        // so remembered results expire quickly (Profile changes still apply promptly) and are limited in number
        private static readonly long _authorizationLifetime = TimeSpan.FromSeconds(5).Ticks;
        private const int _authorizationLimit = 256;

        private readonly ITrakHoundAuthorizer _authorizer;

        // Created on the first Authorize : a context is created for every Request, and most don't Authorize through it
        private Dictionary<(string ResourceId, string Permission), (TrakHoundAuthorizationResult Result, long Expires)> _authorizations;


        /// <summary>
        /// The context of the Request currently being processed (null if not processing a Request)
        /// </summary>
        public static TrakHoundSecurityContext Current => _current.Value;

        /// <summary>
        /// The Identity that made the Request (null for an anonymous Request)
        /// </summary>
        public TrakHoundIdentity Identity { get; }


        private TrakHoundSecurityContext(ITrakHoundAuthorizer authorizer, TrakHoundIdentity identity)
        {
            _authorizer = authorizer;
            Identity = identity;
        }


        /// <summary>
        /// Sets the Current context for the rest of the async call chain. Dispose to restore the previous context
        /// </summary>
        public static IDisposable Begin(ITrakHoundAuthorizer authorizer, TrakHoundIdentity identity)
        {
            var previous = _current.Value;
            _current.Value = authorizer != null ? new TrakHoundSecurityContext(authorizer, identity) : null;
            return new Scope(previous);
        }

        /// <summary>
        /// Checks whether the Request's Identity can access the Resource
        /// </summary>
        public TrakHoundAuthorizationResult Authorize(string resourceId, string permission = null)
        {
            // An expired Identity is always checked again (so a remembered Allowed can't outlive it)
            if (Identity != null && Identity.IsExpired) return _authorizer.Authorize(Identity, resourceId, permission);

            // The same Resource is usually checked many times in one Request (ex. once per Service call), so the result is remembered
            var key = (resourceId, permission);
            var now = DateTime.UtcNow.Ticks;

            var authorizations = Volatile.Read(ref _authorizations);
            if (authorizations != null)
            {
                lock (authorizations)
                {
                    if (authorizations.TryGetValue(key, out var entry) && entry.Expires > now) return entry.Result;
                }
            }

            var result = _authorizer.Authorize(Identity, resourceId, permission);

            if (authorizations == null)
            {
                var created = new Dictionary<(string ResourceId, string Permission), (TrakHoundAuthorizationResult Result, long Expires)>();
                authorizations = Interlocked.CompareExchange(ref _authorizations, created, null) ?? created;
            }

            lock (authorizations)
            {
                if (authorizations.Count >= _authorizationLimit) authorizations.Clear();
                authorizations[key] = (result, now + _authorizationLifetime);
            }

            return result;
        }


        private sealed class Scope : IDisposable
        {
            private readonly TrakHoundSecurityContext _previous;

            public Scope(TrakHoundSecurityContext previous) => _previous = previous;

            public void Dispose() => _current.Value = _previous;
        }
    }
}
