// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Data
{
    /// <summary>
    /// A Column in a TrakHoundDataSet
    /// </summary>
    public class TrakHoundDataColumn
    {
        /// <summary>
        /// The zero-based position of the Column in the DataSet (0 is the Primary Key Column)
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// The unique Identifier of the Column, used to look up Cell values
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The display Name of the Column
        /// </summary>
        public string Name { get; set; }


        /// <summary>
        /// Creates an empty Column
        /// </summary>
        public TrakHoundDataColumn() { }

        /// <summary>
        /// Creates a Column with the specified ID and optional Name
        /// </summary>
        public TrakHoundDataColumn(string id, string name = null)
        {
            Id = id;
            Name = name;
        }

        /// <summary>
        /// Creates a Column at the specified Index with the specified ID and optional Name
        /// </summary>
        public TrakHoundDataColumn(int index, string id, string name = null)
        {
            Index = index;
            Id = id;
            Name = name;
        }
    }
}
