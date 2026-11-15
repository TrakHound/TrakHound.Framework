using System;

namespace TrakHound.Services.Tables.Attributes
{
    /// <summary>
    /// Marks the property's Column as part of the Table's clustered Primary Key (the Column is also NOT NULL)
    /// </summary>
    public class PrimaryKeyAttribute : Attribute
    {
        /// <summary>
        /// The sort order of the Column in the Primary Key (null uses the database default)
        /// </summary>
        public TrakHoundSortOrder? Order { get; set; }


        /// <summary>
        /// Creates a PrimaryKeyAttribute that uses the default sort order
        /// </summary>
        public PrimaryKeyAttribute() { }

        /// <summary>
        /// Creates a PrimaryKeyAttribute with the specified sort order
        /// </summary>
        public PrimaryKeyAttribute(TrakHoundSortOrder order)
        {
            Order = order;
        }
    }
}
