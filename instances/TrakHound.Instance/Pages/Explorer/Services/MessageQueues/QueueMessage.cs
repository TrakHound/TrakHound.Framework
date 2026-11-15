using System.Text;
using System.Text.Json;

namespace TrakHound.Instance.Pages.Explorer.Services.MessageQueues
{
    public enum QueueMessageStatus
    {
        /// <summary>
        /// Delivered without Auto Acknowledge and waiting to be Acknowledged or Rejected
        /// </summary>
        Pending,

        /// <summary>
        /// Acknowledged by the Broker when it was delivered (removed from the Queue)
        /// </summary>
        AutoAcknowledged,

        Acknowledged,

        /// <summary>
        /// Rejected and returned to the Queue
        /// </summary>
        Rejected,

        /// <summary>
        /// The Channel it was delivered on closed before it was Acknowledged, so the Broker returned it to the Queue
        /// </summary>
        Returned,

        Failed
    }

    public enum QueueMessageFormat
    {
        Auto,
        Text,
        Json,
        Bytes
    }

    public class QueueMessage
    {
        private const int _previewLength = 200;
        private const int _maxBytes = 64 * 1024;


        public long Id { get; set; }

        public string Queue { get; set; }

        public string DeliveryId { get; set; }

        public string MessageId { get; set; }

        public string ContentType { get; set; }

        public Dictionary<string, string> Headers { get; set; }

        /// <summary>
        /// Delivered before (and Rejected or not Acknowledged)
        /// </summary>
        public bool Redelivered { get; set; }

        public byte[] Content { get; set; }

        /// <summary>
        /// When the Message was Published (null if the Broker didn't report it)
        /// </summary>
        public DateTime? Published { get; set; }

        public DateTime Received { get; set; }

        /// <summary>
        /// "Pull" or "Subscribe"
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// The Subscription that delivered the Message (null if Pulled)
        /// </summary>
        public string SubscriptionId { get; set; }

        public QueueMessageStatus Status { get; set; }

        public string StatusMessage { get; set; }

        public int Size => Content?.Length ?? 0;

        public bool IsPending => Status == QueueMessageStatus.Pending;


        private string _preview;
        public string Preview
        {
            get
            {
                if (_preview == null)
                {
                    var text = GetText(Content) ?? "";
                    if (text.Length > _previewLength) text = text.Substring(0, _previewLength) + "…";
                    _preview = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
                }
                return _preview;
            }
        }

        public bool IsExpandable => Size > _previewLength || IsJson(Content) || (Content != null && Array.IndexOf(Content, (byte)'\n') >= 0);


        public string Format(QueueMessageFormat format)
        {
            if (Content == null) return null;

            switch (format)
            {
                case QueueMessageFormat.Json: return ConvertJson(Content) ?? GetText(Content);
                case QueueMessageFormat.Bytes: return ConvertBytes(Content);
                case QueueMessageFormat.Text: return GetText(Content);
                default: return (IsJson(Content) ? ConvertJson(Content) : null) ?? GetText(Content);
            }
        }

        private static string GetText(byte[] content)
        {
            if (content == null) return null;

            try
            {
                return Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, _maxBytes)) +
                    (content.Length > _maxBytes ? $"\n... {content.Length - _maxBytes:N0} more bytes" : "");
            }
            catch { }

            return null;
        }

        private static bool IsJson(byte[] content)
        {
            if (content == null) return false;

            // Check the first non-whitespace character before attempting to parse
            foreach (var b in content)
            {
                if (b == ' ' || b == '\t' || b == '\r' || b == '\n') continue;
                return b == '{' || b == '[';
            }

            return false;
        }

        private static string ConvertJson(byte[] content)
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(content);
                return JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { }

            return null;
        }

        private static string ConvertBytes(byte[] content)
        {
            // Hex dump : offset, 16 bytes per line, and the printable ASCII characters
            const int bytesPerLine = 16;
            var length = Math.Min(content.Length, _maxBytes);
            var builder = new StringBuilder();

            for (var offset = 0; offset < length; offset += bytesPerLine)
            {
                var count = Math.Min(bytesPerLine, length - offset);

                builder.Append(offset.ToString("X8")).Append("  ");
                for (var i = 0; i < bytesPerLine; i++)
                {
                    builder.Append(i < count ? content[offset + i].ToString("X2") + " " : "   ");
                    if (i == 7) builder.Append(' ');
                }

                builder.Append(' ');
                for (var i = 0; i < count; i++)
                {
                    var b = content[offset + i];
                    builder.Append(b >= 32 && b < 127 ? (char)b : '.');
                }

                builder.AppendLine();
            }

            if (content.Length > length) builder.AppendLine($"... {content.Length - length:N0} more bytes");

            return builder.ToString();
        }
    }
}
