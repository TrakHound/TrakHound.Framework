using System;
using System.Collections;
using System.Collections.Generic;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// A list of Parameters passed with a Query
    /// </summary>
    public class TrakHoundTableParameterCollection : IEnumerable<TrakHoundTableParameter>
    {
        private readonly List<TrakHoundTableParameter> _parameters = new List<TrakHoundTableParameter>();


        /// <summary>
        /// The number of Parameters in the collection
        /// </summary>
        public int Count => _parameters.Count;


        /// <summary>
        /// Adds the specified Parameter
        /// </summary>
        public void Add(TrakHoundTableParameter parameter)
        {
            _parameters.Add(parameter);
        }

        /// <summary>
        /// Adds a Parameter with the specified name, Data Type and value
        /// </summary>
        public void Add(string name, TrakHoundTableDataType? dataType, object value)
        {
            var parameter = new TrakHoundTableParameter();
            parameter.Name = name;
            parameter.DataType = dataType;
            parameter.Value = value;

            _parameters.Add(parameter);
        }

        /// <summary>
        /// Adds a Parameter with the specified name, SQL Server data type (ex. varchar(255), bigint) and value
        /// </summary>
        [Obsolete("Use a TrakHoundTableDataType, which works on every database")]
        public void Add(string name, string dataType, object value)
        {
            Add(name, TrakHoundTables.GetSqlServerColumnType(dataType), value);
        }

        /// <summary>
        /// Adds a Parameter whose Data Type and value are derived from the value's runtime type
        /// (DateTime and TimeSpan values are converted to Unix nanoseconds, bool to 1 or 0, and enums to strings)
        /// </summary>
        public void Add<TValue>(string name, TValue value)
        {
            // Use the runtime type so values passed as 'object' still map correctly
            var valueType = value != null ? value.GetType() : typeof(TValue);

            var parameter = new TrakHoundTableParameter();
            parameter.Name = name;
            parameter.DataType = TrakHoundTables.GetColumnType(valueType);
            parameter.Value = TrakHoundTables.GetColumnValue(valueType, value); // Timestamps/Durations -> Unix Nanoseconds

            _parameters.Add(parameter);
        }


        /// <summary>
        /// Returns an enumerator that iterates through the Parameters
        /// </summary>
        public IEnumerator<TrakHoundTableParameter> GetEnumerator() => _parameters.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
