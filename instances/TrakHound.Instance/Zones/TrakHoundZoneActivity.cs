// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Zones
{
    /// <summary>
    /// Which Zone rule a call was checked against
    /// </summary>
    public enum TrakHoundZoneDirection
    {
        /// <summary>
        /// What the Members of the caller's Zone can access (the Zone's Rules)
        /// </summary>
        Outbound,

        /// <summary>
        /// Which Modules can call the Members of the target's Zone (the Zone's AllowedCallers)
        /// </summary>
        Inbound
    }

    /// <summary>
    /// The result of checking a call against the Zones of the caller and the target
    /// </summary>
    public readonly struct TrakHoundZoneEvaluation
    {
        /// <summary>
        /// Neither the caller nor the target is in a Zone
        /// </summary>
        public static readonly TrakHoundZoneEvaluation NotGoverned = new TrakHoundZoneEvaluation(null, null, TrakHoundZoneDecision.Allowed);


        /// <summary>
        /// The Zone that decided the call (null if the call is not governed by a Zone)
        /// </summary>
        public TrakHoundZoneConfiguration Zone { get; }

        /// <summary>
        /// The rule that denied the call (null if the call is allowed)
        /// </summary>
        public TrakHoundZoneDirection? Direction { get; }

        public TrakHoundZoneDecision Decision { get; }


        private TrakHoundZoneEvaluation(TrakHoundZoneConfiguration zone, TrakHoundZoneDirection? direction, TrakHoundZoneDecision decision)
        {
            Zone = zone;
            Direction = direction;
            Decision = decision;
        }

        public static TrakHoundZoneEvaluation Allow(TrakHoundZoneConfiguration zone) => new TrakHoundZoneEvaluation(zone, null, TrakHoundZoneDecision.Allowed);

        /// <summary>
        /// The call is not allowed by the Zone. A Zone in Audit Mode still allows it (and it is recorded as "Would Deny")
        /// </summary>
        public static TrakHoundZoneEvaluation Deny(TrakHoundZoneConfiguration zone, TrakHoundZoneDirection direction, string message)
        {
            var decision = zone.Mode == TrakHoundZoneMode.Audit ? TrakHoundZoneDecision.Audited(zone.Id, message) : TrakHoundZoneDecision.Denied(zone.Id, message);
            return new TrakHoundZoneEvaluation(zone, direction, decision);
        }
    }

    /// <summary>
    /// A call that a Zone denied (or would deny in Audit Mode). Repeated calls are counted on the same item
    /// </summary>
    public class TrakHoundZoneActivity
    {
        public string Key { get; set; }

        public string ZoneId { get; set; }

        public TrakHoundZoneDirection Direction { get; set; }

        /// <summary>
        /// The Member ID of the Module that made the call
        /// </summary>
        public string CallerId { get; set; }

        public string CallerName { get; set; }

        /// <summary>
        /// The Member ID of the Module that was called (null if the Resource is not a Module, ex. a Volume)
        /// </summary>
        public string TargetId { get; set; }

        public string ResourceId { get; set; }

        public string Permission { get; set; }

        /// <summary>
        /// True if the call was denied, false if it was only recorded (Audit Mode)
        /// </summary>
        public bool Enforced { get; set; }

        public string Message { get; set; }

        public long Count { get; set; }

        public DateTime FirstSeen { get; set; }

        public DateTime LastSeen { get; set; }

        internal DateTime LastNotified { get; set; }


        public TrakHoundZoneActivity Clone() => (TrakHoundZoneActivity)MemberwiseClone();
    }
}
