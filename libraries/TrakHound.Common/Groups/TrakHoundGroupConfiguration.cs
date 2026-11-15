// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using YamlDotNet.Serialization;

namespace TrakHound.Groups
{
    /// <summary>
    /// A Group organizes Modules (Apis, Apps, Functions, Workers) and sets defaults for them, such as the Router their Client uses.
    /// A Group never restricts what its Modules can access (that is what Zones are for).
    /// </summary>
    /// <remarks>
    /// Stored as YAML in the "group" Configuration Category. A Module joins a Group by setting its GroupId.
    /// </remarks>
    public class TrakHoundGroupConfiguration : ITrakHoundConfiguration
    {
        /// <summary>
        /// The Configuration Category for Group Configurations
        /// </summary>
        public const string ConfigurationCategory = "group";


        /// <summary>
        /// The unique Identifier of the Group
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// A friendly name for the Group
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A description of the Group
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The Router used by Members that don't set their own RouterId (the Default Router when empty)
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
            return $"{Id}:{Name}:{Description}:{RouterId}".ToSHA256Hash();
        }
    }
}
