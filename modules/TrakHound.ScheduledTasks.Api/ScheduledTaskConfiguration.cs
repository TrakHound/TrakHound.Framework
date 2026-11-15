using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.ScheduledTasks
{
    public class ScheduledTaskConfiguration
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        /// <summary>
        /// The Job whose Parameters the Function is run with (no Parameters when empty)
        /// </summary>
        [JsonPropertyName("jobId")]
        public string JobId { get; set; }

        [JsonPropertyName("schedule")]
        public string Schedule { get; set; }

        /// <summary>
        /// The Time Zone the Schedule is in (IANA or Windows ID). The server's local Time Zone when empty
        /// </summary>
        [JsonPropertyName("timeZone")]
        public string TimeZone { get; set; }

        /// <summary>
        /// Run once when the Scheduled Tasks Worker starts if an occurrence was missed while it was stopped
        /// </summary>
        [JsonPropertyName("runMissed")]
        public bool RunMissed { get; set; }

        /// <summary>
        /// No longer used : Scheduled Tasks get their Parameters from a Job (kept so older Tasks aren't changed when saved)
        /// </summary>
        [JsonPropertyName("parameters")]
        public Dictionary<string, string> Parameters { get; set; }
    }
}
