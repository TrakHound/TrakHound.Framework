using System;

namespace TrakHound.Services.Tables.Attributes
{
    /// <summary>
    /// Adds a NonClustered Index to the Table of a class. The Index is created by GenerateCreate (for new and existing Tables) if it doesn't exist.
    /// Column names are used as written (ex. "instance_id") and can end with " asc" or " desc" to set the sort order
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class TableIndexAttribute : Attribute
    {
        /// <summary>
        /// The name of the Index (unique within the Table)
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The key Columns of the Index (in order)
        /// </summary>
        public string[] Columns { get; }

        /// <summary>
        /// The Columns stored in the Index without being part of its key (so queries that read them don't need a lookup)
        /// </summary>
        public string[] Include { get; set; }

        /// <summary>
        /// True if no two rows can have the same values in the key Columns
        /// </summary>
        public bool IsUnique { get; set; }


        /// <summary>
        /// Creates a TableIndexAttribute with the specified Index name and key Columns
        /// </summary>
        public TableIndexAttribute(string name, params string[] columns)
        {
            Name = name;
            Columns = columns;
        }
    }
}
