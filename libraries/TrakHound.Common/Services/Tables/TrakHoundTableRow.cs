// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// A row of Table data identified by a Key
    /// </summary>
    public struct TrakHoundTableRow
    {
        /// <summary>
        /// The unique Identifier of the row
        /// </summary>
        public string Uuid { get; set; }

        /// <summary>
        /// The Key that identifies the row
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// When the row was created (Unix nanoseconds)
        /// </summary>
        public long Created { get; set; }

        /// <summary>
        /// The values of the row's Cells (in Column order)
        /// </summary>
        public object[] Cells { get; set; }


        /// <summary>
        /// Creates an empty TrakHoundTableRow
        /// </summary>
        public TrakHoundTableRow() { }

        /// <summary>
        /// Creates a TrakHoundTableRow with the specified Key and Cells (Created defaults to now when not greater than zero)
        /// </summary>
        public TrakHoundTableRow(string key, object[] cells, long created = 0)
        {
            Key = key;
            Cells = cells;
            Created = created > 0 ? created : UnixDateTime.Now;
        }
    }
}
