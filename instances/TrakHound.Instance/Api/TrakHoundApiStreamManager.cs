using TrakHound.Api;

namespace TrakHound.Instance.Api
{
    /// <summary>
    /// Keeps the open API Streams (from every Source, not only HTTP) with their activity, and the most recently closed ones (for the API Streams Diagnostics)
    /// </summary>
    public class TrakHoundApiStreamManager
    {
        public const int DefaultClosedHistory = TrakHoundInstanceMetricsConfiguration.DefaultHistory;
        public const int MaxClosedHistory = 10000;

        private readonly Dictionary<string, Entry> _open = new();
        private readonly object _lock = new object();
        private CircularBuffer<TrakHoundApiStreamInformation> _closed = new(DefaultClosedHistory);


        class Entry
        {
            public TrakHoundApiStreamInformation Information;
            public ITrakHoundStream Stream;
            public long MessageCount;
            public long ByteCount;
            public long LastMessageTicks; // UTC Ticks (0 = None), since a DateTime? can't be written atomically
            public string CloseReason;
            public string Error;
        }


        /// <summary>
        /// The most recently closed Streams kept (0 keeps none). Changing it clears the closed Streams kept so far.
        /// </summary>
        public int ClosedHistory
        {
            get => _closed?.Limit ?? 0;
            set
            {
                var capacity = Math.Clamp(value, 0, MaxClosedHistory);
                if (capacity == ClosedHistory) return;

                _closed = capacity > 0 ? new CircularBuffer<TrakHoundApiStreamInformation>(capacity) : null;
            }
        }


        /// <summary>
        /// Tracks an opened Stream until it is disposed
        /// </summary>
        public void Add(ITrakHoundStream<TrakHoundApiResponse> stream, TrakHoundApiStreamInformation information)
        {
            if (stream == null || string.IsNullOrEmpty(stream.Id) || information == null) return;

            information.StreamId = stream.Id;
            var entry = new Entry { Information = information, Stream = stream };

            lock (_lock)
            {
                if (_open.ContainsKey(stream.Id)) return;
                _open.Add(stream.Id, entry);
            }

            stream.Received += (s, response) =>
            {
                Interlocked.Increment(ref entry.MessageCount);
                var content = response.Content;
                if (content != null && content.CanSeek) Interlocked.Add(ref entry.ByteCount, content.Length);
                Interlocked.Exchange(ref entry.LastMessageTicks, DateTime.UtcNow.Ticks);
            };

            if (stream is TrakHoundStream<TrakHoundApiResponse> trakhoundStream)
            {
                trakhoundStream.Error += (s, exception) => entry.Error = exception?.Message;
            }

            stream.Disposed += (s, id) => Closed(id);
        }

        /// <summary>
        /// Closes (disposes) an open Stream. Returns false if no Stream with the ID is open.
        /// </summary>
        public bool Close(string streamId, string reason = TrakHoundApiStreamCloseReasons.ClosedByAdmin)
        {
            Entry entry;
            lock (_lock)
            {
                if (string.IsNullOrEmpty(streamId) || !_open.TryGetValue(streamId, out entry)) return false;
                entry.CloseReason ??= reason;
            }

            entry.Stream.Dispose();
            return true;
        }

        /// <summary>
        /// Sets why an open Stream is about to be closed (when it is closed by something other than its Source)
        /// </summary>
        public void SetCloseReason(string streamId, string reason)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(streamId) && _open.TryGetValue(streamId, out var entry)) entry.CloseReason ??= reason;
            }
        }

        public TrakHoundApiStreams Get()
        {
            List<Entry> open;
            lock (_lock) open = _open.Values.ToList();

            var closed = _closed;
            return new TrakHoundApiStreams
            {
                ClosedCapacity = closed?.Limit ?? 0,
                Open = open.Select(CreateSnapshot).OrderByDescending(o => o.Opened).ToList(),
                Closed = closed?.Get() ?? Array.Empty<TrakHoundApiStreamInformation>()
            };
        }


        private void Closed(string streamId)
        {
            Entry entry;
            lock (_lock)
            {
                if (string.IsNullOrEmpty(streamId) || !_open.Remove(streamId, out entry)) return;
            }

            var information = CreateSnapshot(entry);
            information.Closed = DateTime.UtcNow;
            information.CloseReason = entry.CloseReason ?? (entry.Error != null ? TrakHoundApiStreamCloseReasons.Error : TrakHoundApiStreamCloseReasons.ClosedBySource);
            _closed?.Add(information);
        }

        private static TrakHoundApiStreamInformation CreateSnapshot(Entry entry)
        {
            var information = entry.Information.Clone();
            information.MessageCount = Interlocked.Read(ref entry.MessageCount);
            information.ByteCount = Interlocked.Read(ref entry.ByteCount);
            var lastMessageTicks = Interlocked.Read(ref entry.LastMessageTicks);
            information.LastMessage = lastMessageTicks > 0 ? new DateTime(lastMessageTicks, DateTimeKind.Utc) : null;
            information.Error = entry.Error;
            return information;
        }
    }
}
