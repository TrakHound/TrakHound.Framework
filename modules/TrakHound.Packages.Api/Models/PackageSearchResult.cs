using System.Text.Json.Serialization;

namespace TrakHound.Packages.Api.Models
{
    /// <summary>
    /// A Package found by a search across Organizations, with the Organization it belongs to (TrakHoundPackageJson doesn't have one)
    /// </summary>
    public class PackageSearchResult
    {
        [JsonPropertyName("organization")]
        public string Organization { get; set; }

        [JsonPropertyName("package")]
        public TrakHoundPackageJson Package { get; set; }
    }
}
