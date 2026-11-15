using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// JSON representation of a TrakHoundTableResult
    /// </summary>
    public class TrakHoundTableResultJson
    {
        /// <summary>
        /// The Columns of the result set
        /// </summary>
        [JsonPropertyName("columns")]
        public TrakHoundTableColumn[] Columns { get; set; }

        /// <summary>
        /// The rows of the result set. Each row contains a value for each Column (in Column order)
        /// </summary>
        [JsonPropertyName("rows")]
        public object[][] Rows { get; set; }
    }
}
