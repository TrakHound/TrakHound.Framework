using System;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Packages.Api.Models
{
    [Table(TableName, SchemaName)]
    [IgnoreDuplicateKey]
    public class PackageModel
    {
        public const string TableName = "package";
        public const string SchemaName = "trakhound";


        [Column]
        [PrimaryKey]
        public string Organization { get; set; }

        [Column]
        [PrimaryKey]
        public string Uuid { get; set; }

        [Column]
        public string Id { get; set; }

        [Column]
        public int VersionMajor { get; set; }

        [Column]
        public int VersionMinor { get; set; }

        [Column]
        public int VersionPatch { get; set; }

        /// <summary>
        /// The Prerelease Label of the Version (ex. "dev.20261114153000" for 0.3.4-dev.20261114153000). Null for a Stable Version
        /// </summary>
        [Column]
        public string VersionPrerelease { get; set; }

        [Column]
        public string Category { get; set; }

        [Column]
        public DateTime BuildDate { get; set; }

        [Column]
        public string Hash { get; set; }

        [Column]
        public string ContentHash { get; set; }
    }
}
