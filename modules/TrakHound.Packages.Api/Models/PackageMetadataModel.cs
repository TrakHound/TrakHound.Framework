using TrakHound.Services.Tables;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Packages.Api.Models
{
    [Table(TableName, SchemaName)]
    [IgnoreDuplicateKey]
    public class PackageMetadataModel
    {
        public const string TableName = "package_metadata";
        public const string SchemaName = "trakhound";

        [Column]
        [PrimaryKey]
        public string Organization { get; set; }

        [Column]
        [PrimaryKey]
        public string PackageUuid { get; set; }

        [Column]
        [PrimaryKey]
        public string Name { get; set; }

        // Values such as a description can be longer than the default of 255
        [Column(Length = TrakHoundTableColumnDefinition.MaxLength, Unicode = true)]
        public string Value { get; set; }
    }
}
