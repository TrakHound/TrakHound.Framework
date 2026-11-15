using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// A named Parameter passed with a Query
    /// </summary>
    public class TrakHoundTableParameter
    {
        /// <summary>
        /// The name of the Parameter as used in the Query
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The Data Type of the Parameter (null infers it from the Value)
        /// </summary>
        [JsonPropertyName("dataType")]
        [JsonConverter(typeof(JsonStringEnumConverter<TrakHoundTableDataType>))]
        public TrakHoundTableDataType? DataType { get; set; }

        /// <summary>
        /// The value of the Parameter
        /// </summary>
        [JsonPropertyName("value")]
        public object Value { get; set; }
    }
}
