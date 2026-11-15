using System;

namespace TrakHound.Services.Tables.Attributes
{
    /// <summary>
    /// Sets the Table name and/or Schema used by TrakHoundTables for a class
    /// </summary>
    public class TableAttribute : Attribute
    {
        /// <summary>
        /// The name of the Table (the snake_case class name is used when not set)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Schema of the Table (the default Schema is used when not set)
        /// </summary>
        public string Schema { get; set; }


        /// <summary>
        /// Creates a TableAttribute that uses the default name and Schema
        /// </summary>
        public TableAttribute() { }

        /// <summary>
        /// Creates a TableAttribute with the specified Table name
        /// </summary>
        public TableAttribute(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Creates a TableAttribute with the specified Table name and Schema
        /// </summary>
        public TableAttribute(string name, string schema)
        {
            Name = name;
            Schema = schema;
        }
    }
}
