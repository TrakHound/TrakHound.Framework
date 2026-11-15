// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// A Column included in a Table Index
    /// </summary>
    public class TrakHoundTableIndexColumn
    {
        /// <summary>
        /// The name of the Column
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The sort order of the Column in the Index (null uses the database default)
        /// </summary>
        public TrakHoundSortOrder? Order { get; set; }


        /// <summary>
        /// Creates an empty TrakHoundTableIndexColumn
        /// </summary>
        public TrakHoundTableIndexColumn() { }

        /// <summary>
        /// Creates a TrakHoundTableIndexColumn with the specified name and sort order
        /// </summary>
        public TrakHoundTableIndexColumn(string name, TrakHoundSortOrder? order) 
        {
            Name = name;
            Order = order;
        }
    }
}
