namespace TrakHound.Services.Tables
{
    /// <summary>
    /// The Results returned from running a Query
    /// </summary>
    public class TrakHoundTableResponse
    {
        /// <summary>
        /// A Result for each result set returned by the Query
        /// </summary>
        public TrakHoundTableResult[] Results { get; set; }
    }
}
