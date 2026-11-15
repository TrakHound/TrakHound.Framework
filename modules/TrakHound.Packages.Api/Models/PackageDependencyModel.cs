using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Packages.Api.Models
{
    [Table(TableName, SchemaName)]
    [IgnoreDuplicateKey]
    public class PackageDependencyModel
    {
        public const string TableName = "package_dependency";
        public const string SchemaName = "trakhound";

        [Column]
        [PrimaryKey]
        public string Organization { get; set; }

        [Column]
        [PrimaryKey]
        public string PackageUuid { get; set; }

        [Column]
        [PrimaryKey]
        public string DependencyId { get; set; }

        /// <summary>
        /// An exact Version (1.0.0) or a wildcard (1.*)
        /// </summary>
        [Column]
        public string Version { get; set; }

        /// <summary>
        /// The lowest Version that satisfies the Dependency (null when Version alone decides)
        /// </summary>
        [Column]
        public string MinimumVersion { get; set; }
    }
}
