namespace TrakHound.Services.Tables
{
    /// <summary>
    /// A condition comparing a Column to a Value
    /// </summary>
    public struct TrakHoundTableQuery
    {
        /// <summary>
        /// The name of the Column to compare
        /// </summary>
        public string Column { get; set; }

        /// <summary>
        /// The operator used to compare the Column to the Value
        /// </summary>
        public TrakHoundConditionOperator Operator { get; set; }

        /// <summary>
        /// The Value to compare the Column to
        /// </summary>
        public object Value { get; set; }


        /// <summary>
        /// Creates an empty TrakHoundTableQuery
        /// </summary>
        public TrakHoundTableQuery() { }

        /// <summary>
        /// Creates a TrakHoundTableQuery with the specified Column, operator and Value
        /// </summary>
        public TrakHoundTableQuery(string column, TrakHoundConditionOperator conditionOperator, object value) 
        {
            Column = column;
            Operator = conditionOperator;
            Value = value;
        }
    }
}
