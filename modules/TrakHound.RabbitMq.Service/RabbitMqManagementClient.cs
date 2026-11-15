using RabbitMQ.Client;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TrakHound.Services.MessageQueues;

namespace TrakHound.RabbitMq.Service
{
    /// <summary>
    /// Reads Queue Metrics (Message counts and rates) from the RabbitMQ management plugin's HTTP API. AMQP can only report
    /// the Ready count of a Queue that is already known, so the API is the only source for the Queue list and Message rates
    /// </summary>
    internal sealed class RabbitMqManagementClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _queuesUrl;


        public string BaseUrl { get; }


        public RabbitMqManagementClient(string baseUrl, string username, string password, string virtualHost, TimeSpan timeout)
        {
            BaseUrl = baseUrl?.TrimEnd('/');

            // The Virtual Host is a path segment ("/" is encoded as %2F)
            _queuesUrl = $"{BaseUrl}/api/queues/{Uri.EscapeDataString(!string.IsNullOrEmpty(virtualHost) ? virtualHost : "/")}";

            _httpClient = new HttpClient();
            _httpClient.Timeout = timeout;

            // Same credentials as the AMQP Connection (guest / guest when not configured, like the RabbitMQ client)
            var credentials = $"{(!string.IsNullOrEmpty(username) ? username : ConnectionFactory.DefaultUser)}:{(!string.IsNullOrEmpty(password) ? password : ConnectionFactory.DefaultPass)}";
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }


        /// <summary>
        /// Reads the Metrics of every Queue in the Virtual Host. Throws if the API can't be reached or returns an error
        /// </summary>
        public async Task<List<TrakHoundMessageQueueMetrics>> GetQueueMetrics(CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.GetAsync(_queuesUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"{(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var timestamp = UnixDateTime.Now;
            var results = new List<TrakHoundMessageQueueMetrics>();

            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var queue in document.RootElement.EnumerateArray())
                {
                    var name = GetString(queue, "name");
                    if (string.IsNullOrEmpty(name)) continue;

                    var metrics = new TrakHoundMessageQueueMetrics();
                    metrics.Queue = name;
                    metrics.MessageCount = GetLong(queue, "messages") ?? 0;
                    metrics.ReadyCount = GetLong(queue, "messages_ready") ?? 0;
                    metrics.UnacknowledgedCount = GetLong(queue, "messages_unacknowledged") ?? 0;
                    metrics.ConsumerCount = (int)(GetLong(queue, "consumers") ?? 0);
                    metrics.Timestamp = timestamp;

                    // Message Stats are only present once the Queue has had activity (no activity is a rate of 0)
                    queue.TryGetProperty("message_stats", out var stats);
                    metrics.PublishRate = GetRate(stats, "publish_details") ?? 0;
                    metrics.DeliverRate = GetRate(stats, "deliver_get_details") ?? 0;
                    metrics.AcknowledgeRate = GetRate(stats, "ack_details") ?? 0;
                    metrics.RedeliverRate = GetRate(stats, "redeliver_details") ?? 0;
                    metrics.PublishedTotal = GetLong(stats, "publish") ?? 0;
                    metrics.DeliveredTotal = GetLong(stats, "deliver_get") ?? 0;
                    metrics.AcknowledgedTotal = GetLong(stats, "ack") ?? 0;

                    results.Add(metrics);
                }
            }

            return results;
        }


        private static string GetString(JsonElement element, string property)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return null;
        }

        private static long? GetLong(JsonElement element, string property)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.TryGetInt64(out var l) ? l : (long)value.GetDouble();
            }

            return null;
        }

        private static double? GetRate(JsonElement stats, string detailsProperty)
        {
            if (stats.ValueKind == JsonValueKind.Object && stats.TryGetProperty(detailsProperty, out var details) &&
                details.ValueKind == JsonValueKind.Object && details.TryGetProperty("rate", out var rate) && rate.ValueKind == JsonValueKind.Number)
            {
                return rate.GetDouble();
            }

            return null;
        }
    }
}
