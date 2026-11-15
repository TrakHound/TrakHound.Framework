using System;

namespace TrakHound.Services.Tables.Attributes
{
    /// <summary>
    /// Overrides the Column name, Length and/or Unicode setting generated for a property by TrakHoundTables
    /// </summary>
    public class ColumnAttribute : Attribute
    {
        /// <summary>
        /// The name of the Column (the snake_case property name is used when not set)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The length of a String Column (TrakHoundTableColumnDefinition.MaxLength for no limit, 255 when not set)
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// True if a String Column must keep any Unicode character
        /// </summary>
        public bool Unicode { get; set; }

        /// <summary>
        /// The SQL Server type definition of the Column (ex. varchar(max)). Only the obsolete TrakHoundTables methods that generate SQL use it
        /// </summary>
        [Obsolete("Use Length and Unicode, which work on every database. Table Requests ignore this")]
        public string Definition { get; set; }


        /// <summary>
        /// Creates a ColumnAttribute that uses the default name
        /// </summary>
        public ColumnAttribute() { }

        /// <summary>
        /// Creates a ColumnAttribute with the specified Column name
        /// </summary>
        public ColumnAttribute(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Creates a ColumnAttribute with the specified Column name and SQL Server definition
        /// </summary>
        [Obsolete("Use Length and Unicode, which work on every database. Table Requests ignore the definition")]
        public ColumnAttribute(string name, string definition)
        {
            Name = name;
#pragma warning disable CS0618
            Definition = definition;
#pragma warning restore CS0618
        }
    }
}
