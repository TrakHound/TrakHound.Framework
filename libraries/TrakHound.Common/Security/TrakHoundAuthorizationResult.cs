// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Security
{
    /// <summary>
    /// The Status of an Authorization check
    /// </summary>
    public enum TrakHoundAuthorizationStatus
    {
        /// <summary>
        /// Access to the Resource is allowed
        /// </summary>
        Allowed,

        /// <summary>
        /// The Resource requires an Identity and none (or an expired one) was given
        /// </summary>
        AuthenticationRequired,

        /// <summary>
        /// The Identity does not have access to the Resource
        /// </summary>
        Forbidden
    }

    /// <summary>
    /// The result of checking whether an Identity can access a Resource
    /// </summary>
    public readonly struct TrakHoundAuthorizationResult
    {
        private static readonly string[] _anyPermission = new string[] { "*" };


        /// <summary>
        /// The Status of the Authorization check
        /// </summary>
        public TrakHoundAuthorizationStatus Status { get; }

        /// <summary>
        /// The Permissions the Identity has for the Resource ("*" for any Permission)
        /// </summary>
        public IReadOnlyList<string> Permissions { get; }

        /// <summary>
        /// When Authentication is required, the IDs of the Identity Providers that can sign in to the Resource (ordered by Profile Priority)
        /// </summary>
        public IReadOnlyList<string> ProviderIds { get; }

        /// <summary>
        /// A message describing the result. Shown in logs, never to the client
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// A message that is safe to send to the client with a denial (ex. which setting refuses every caller), or null to send none.
        /// It must be the same for every Identity and Resource, so it never shows how the Instance's access is configured
        /// </summary>
        public string ClientMessage { get; }


        /// <summary>
        /// True if access is allowed
        /// </summary>
        public bool IsAllowed => Status == TrakHoundAuthorizationStatus.Allowed;

        /// <summary>
        /// True if access is allowed only because no Profile covers the Resource (it has not been protected, or deliberately made public)
        /// </summary>
        public bool IsUncovered { get; }


        private TrakHoundAuthorizationResult(TrakHoundAuthorizationStatus status, IReadOnlyList<string> permissions, IReadOnlyList<string> providerIds, string message, bool uncovered = false, string clientMessage = null)
        {
            Status = status;
            Permissions = permissions ?? Array.Empty<string>();
            ProviderIds = providerIds ?? Array.Empty<string>();
            Message = message;
            IsUncovered = uncovered;
            ClientMessage = clientMessage;
        }


        /// <summary>
        /// Access is allowed with the specified Permissions
        /// </summary>
        public static TrakHoundAuthorizationResult Allowed(IReadOnlyList<string> permissions, string message = null) => new TrakHoundAuthorizationResult(TrakHoundAuthorizationStatus.Allowed, permissions, null, message);

        /// <summary>
        /// Access is allowed without restriction (the Resource is not protected)
        /// </summary>
        public static TrakHoundAuthorizationResult Unrestricted(string message = null) => new TrakHoundAuthorizationResult(TrakHoundAuthorizationStatus.Allowed, _anyPermission, null, message);

        /// <summary>
        /// Access is allowed because no Profile covers the Resource
        /// </summary>
        public static TrakHoundAuthorizationResult Uncovered(string message = null) => new TrakHoundAuthorizationResult(TrakHoundAuthorizationStatus.Allowed, _anyPermission, null, message, true);

        /// <summary>
        /// An Identity is required from one of the specified Identity Providers
        /// </summary>
        public static TrakHoundAuthorizationResult AuthenticationRequired(IReadOnlyList<string> providerIds, string message = null) => new TrakHoundAuthorizationResult(TrakHoundAuthorizationStatus.AuthenticationRequired, null, providerIds, message);

        /// <summary>
        /// The Identity does not have access
        /// </summary>
        /// <param name="message">Why, for the logs (never sent to the client)</param>
        /// <param name="clientMessage">A message that is safe to send to the client (see ClientMessage), or null to send none</param>
        public static TrakHoundAuthorizationResult Forbidden(string message = null, string clientMessage = null) => new TrakHoundAuthorizationResult(TrakHoundAuthorizationStatus.Forbidden, null, null, message, false, clientMessage);
    }
}
