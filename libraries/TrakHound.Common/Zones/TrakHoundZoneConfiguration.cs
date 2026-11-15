// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using TrakHound.Configurations;
using YamlDotNet.Serialization;

namespace TrakHound.Zones
{
    /// <summary>
    /// How a Zone handles calls that its Rules do not allow
    /// </summary>
    public enum TrakHoundZoneMode
    {
        /// <summary>
        /// Calls that are not allowed are denied
        /// </summary>
        Enforce,

        /// <summary>
        /// Calls that are not allowed are recorded (as "Would Deny") but still made. Used to learn what a Module needs before Enforcing
        /// </summary>
        Audit
    }

    /// <summary>
    /// Allows the Members of a Zone to access the Resources that match a pattern
    /// </summary>
    public class TrakHoundZoneRule
    {
        /// <summary>
        /// The Resource ID pattern. Matches exactly, or by prefix when it ends with a Wildcard (ex. "api:entities:*")
        /// </summary>
        public string Resource { get; set; }

        /// <summary>
        /// The Permissions allowed ("read", "write", "run"). Empty (or "*") allows any Permission
        /// </summary>
        public IEnumerable<string> Permissions { get; set; }

        /// <summary>
        /// A description of why the Rule is needed
        /// </summary>
        public string Description { get; set; }
    }

    /// <summary>
    /// A Zone limits what a set of Modules (Apis, Apps, Functions, Workers) can access through their Client, and optionally which
    /// Modules can call them. Zones guard against a Module accidentally reaching something it shouldn't (ex. a Module in development,
    /// or one written with an AI coding tool). They are not a security boundary against a Module that is deliberately hostile.
    /// </summary>
    /// <remarks>
    /// Stored as YAML in the "zone" Configuration Category. A Module that is not a Member of any Zone is not restricted.
    /// </remarks>
    public class TrakHoundZoneConfiguration : ITrakHoundConfiguration
    {
        /// <summary>
        /// The Configuration Category for Zone Configurations
        /// </summary>
        public const string ConfigurationCategory = "zone";


        /// <summary>
        /// The unique Identifier of the Zone
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// A friendly name for the Zone
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A description of the Zone
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Whether calls that are not allowed are denied (Enforce) or only recorded (Audit)
        /// </summary>
        public TrakHoundZoneMode Mode { get; set; }

        /// <summary>
        /// The Modules in the Zone, as Member IDs (ex. "function:my-function") or patterns (ex. "worker:dev-*")
        /// </summary>
        public IEnumerable<string> Members { get; set; }

        /// <summary>
        /// Members can call each other without a Rule (default true)
        /// </summary>
        public bool AllowSameZone { get; set; } = true;

        /// <summary>
        /// What the Members can access. Anything not matched by a Rule is denied
        /// </summary>
        public IEnumerable<TrakHoundZoneRule> Rules { get; set; }

        /// <summary>
        /// The Modules (Member ID patterns, or "zone:{zoneId}") that can call the Members of this Zone. Empty allows any Module
        /// </summary>
        public IEnumerable<string> AllowedCallers { get; set; }

        /// <summary>
        /// When set, every Service call made by a Member goes through this Router (ex. a Router to test data), whatever Router the Member asks for
        /// </summary>
        public string RouterId { get; set; }


        /// <inheritdoc/>
        [YamlIgnore]
        public string Category => ConfigurationCategory;

        /// <inheritdoc/>
        [YamlIgnore]
        public string Path { get; set; }

        /// <summary>
        /// A SHA256 hash of the Configuration's properties (used to detect when it has changed)
        /// </summary>
        [YamlIgnore]
        public string Hash => GenerateHash();


        private string GenerateHash()
        {
            return $"{Id}:{Name}:{Description}:{Mode}:{Members?.ToJson()}:{AllowSameZone}:{Rules?.ToJson()}:{AllowedCallers?.ToJson()}:{RouterId}".ToSHA256Hash();
        }
    }
}
