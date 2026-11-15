// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading;
using TrakHound.Api;

namespace TrakHound.Zones
{
    /// <summary>
    /// The result of checking a call against the Zones of the caller and the target
    /// </summary>
    public readonly struct TrakHoundZoneDecision
    {
        /// <summary>
        /// The call is allowed (or is not governed by a Zone)
        /// </summary>
        public static readonly TrakHoundZoneDecision Allowed = new TrakHoundZoneDecision(true, false, null, null);


        /// <summary>
        /// True if the call can be made. A call that a Zone in Audit Mode would deny is still allowed
        /// </summary>
        public bool IsAllowed { get; }

        /// <summary>
        /// True if a Zone in Audit Mode would have denied the call
        /// </summary>
        public bool IsAudited { get; }

        /// <summary>
        /// The ID of the Zone that denied (or would have denied) the call
        /// </summary>
        public string ZoneId { get; }

        /// <summary>
        /// Why the call was denied. Shown in logs
        /// </summary>
        public string Message { get; }


        public TrakHoundZoneDecision(bool isAllowed, bool isAudited, string zoneId, string message)
        {
            IsAllowed = isAllowed;
            IsAudited = isAudited;
            ZoneId = zoneId;
            Message = message;
        }

        public static TrakHoundZoneDecision Denied(string zoneId, string message) => new TrakHoundZoneDecision(false, false, zoneId, message);

        public static TrakHoundZoneDecision Audited(string zoneId, string message) => new TrakHoundZoneDecision(true, true, zoneId, message);
    }

    /// <summary>
    /// Checks calls made by Modules against their Zones
    /// </summary>
    public interface ITrakHoundZoneAuthorizer
    {
        /// <summary>
        /// Checks whether the caller can access the Resource with the Permission
        /// </summary>
        /// <param name="caller">What is making the call</param>
        /// <param name="targetMemberId">The Member ID of the Module being called (ex. "service:redis"), or null if the Resource is not a Module</param>
        /// <param name="resourceId">The Resource being accessed</param>
        /// <param name="permission">The Permission required</param>
        TrakHoundZoneDecision Authorize(TrakHoundApiRequestSource caller, string targetMemberId, string resourceId, string permission);
    }

    /// <summary>
    /// The Module making the current call. Flows with the async call chain so that code that is not given the caller directly
    /// (ex. a Service Router) can check the call against the caller's Zone.
    /// </summary>
    /// <remarks>
    /// Set by the Client a Module uses for Service calls. When there is no Current context the call is not governed by a Zone.
    /// </remarks>
    public sealed class TrakHoundZoneContext
    {
        private static readonly AsyncLocal<TrakHoundZoneContext> _current = new AsyncLocal<TrakHoundZoneContext>();

        private readonly ITrakHoundZoneAuthorizer _authorizer;


        /// <summary>
        /// The context of the current call (null if the caller is not known)
        /// </summary>
        public static TrakHoundZoneContext Current => _current.Value;

        /// <summary>
        /// What is making the call
        /// </summary>
        public TrakHoundApiRequestSource Caller { get; }


        private TrakHoundZoneContext(ITrakHoundZoneAuthorizer authorizer, TrakHoundApiRequestSource caller)
        {
            _authorizer = authorizer;
            Caller = caller;
        }


        /// <summary>
        /// Sets the Current context. When called from an async method the context applies to the rest of that method (and the calls it makes),
        /// and is restored when the method returns
        /// </summary>
        public static void Enter(ITrakHoundZoneAuthorizer authorizer, TrakHoundApiRequestSource caller)
        {
            _current.Value = authorizer != null && caller != null ? new TrakHoundZoneContext(authorizer, caller) : null;
        }

        /// <summary>
        /// Checks whether the Current caller can access the Resource
        /// </summary>
        public TrakHoundZoneDecision Authorize(string targetMemberId, string resourceId, string permission)
        {
            return _authorizer.Authorize(Caller, targetMemberId, resourceId, permission);
        }
    }
}
